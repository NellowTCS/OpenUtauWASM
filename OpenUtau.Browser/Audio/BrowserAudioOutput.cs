using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Audio;
using Serilog;

namespace OpenUtau.Browser.Audio
{
    public partial class BrowserAudioOutput : IAudioOutput, IDisposable
    {
        const int Channels = 2;
        const int SampleRate = 44100;
        const int BufferFrames = 128;
        
        public PlaybackState PlaybackState { get; private set; }
        public int DeviceNumber { get; private set; }

        private ISampleProvider? sampleProvider;
        private double currentTimeMs;
        private bool eof;
        private bool isInitialized;
        private bool isWorkletReady;

        private readonly List<AudioOutputDevice> devices = new();
        
        // Ring buffer for thread-safe sample transfer
        private float[]? ringBuffer;
        private int ringWritePos;
        private int ringReadPos;
        private int ringAvailable;
        private readonly object ringLock = new();
        
        private const int RingBufferSize = 4096 * Channels; // ~93ms of audio

        // Singleton instance for static callback
        private static BrowserAudioOutput? Instance;

        public BrowserAudioOutput()
        {
            Instance = this;
            
            // Allocate ring buffer
            ringBuffer = new float[RingBufferSize];
            
            // Initialize async
            _ = InitializeAsync();
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                // Initialize Web Audio API
                Log.Information("BrowserAudioOutput: Calling InitAudio...");
                await AudioBridge.InitAudio();
                Log.Information("BrowserAudioOutput: InitAudio completed");
                
                // Load worldline WASM (includes miniaudio)
                Log.Information("BrowserAudioOutput: Calling InitWorldline...");
                await AudioBridge.InitWorldline();
                Log.Information("BrowserAudioOutput: InitWorldline completed");
                
                // Register AudioWorklet processor
                Log.Information("BrowserAudioOutput: Calling RegisterAudioWorklet...");
                await AudioBridge.RegisterAudioWorklet();
                Log.Information("BrowserAudioOutput: RegisterAudioWorklet completed");
                
                // Create AudioWorklet node
                Log.Information("BrowserAudioOutput: Calling CreateWorkletNode...");
                var nodeReady = await AudioBridge.CreateWorkletNode();
                Log.Information("BrowserAudioOutput: CreateWorkletNode completed: {NodeReady}", nodeReady);
                if (!nodeReady)
                {
                    Log.Warning("BrowserAudioOutput: AudioWorklet node creation failed, falling back");
                }
                else
                {
                    isWorkletReady = true;
                }
                
                // Set up the callback that JS will call via IntPtr
                AudioBridge.SetAudioCallback();
                
                isInitialized = true;
                Log.Information("BrowserAudioOutput: Initialized successfully (Worklet: {WorkletReady})", isWorkletReady);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BrowserAudioOutput: Failed to initialize");
            }
        }

        public void Init(ISampleProvider sampleProvider)
        {
            PlaybackState = PlaybackState.Stopped;
            eof = false;
            currentTimeMs = 0;
            ringWritePos = 0;
            ringReadPos = 0;
            ringAvailable = 0;

            // Resample if needed
            if (SampleRate != sampleProvider.WaveFormat.SampleRate)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, SampleRate);
            }

            this.sampleProvider = sampleProvider.ToStereo();
            
            Log.Information("BrowserAudioOutput: Initialized with sample provider");
        }

        public void Play()
        {
            if (!isInitialized)
            {
                Log.Warning("BrowserAudioOutput: Cannot play, not initialized");
                return;
            }

            // Ensure AudioContext is running (requires user gesture)
            _ = AudioBridge.ResumeAudio();
            
            // Enable JS-side playback flag
            AudioBridge.StartPlayback();
            
            // Start the worklet feed loop
            AudioBridge.StartContinuousFeed(20); // Feed every 20ms
            
            PlaybackState = PlaybackState.Playing;
            eof = false;
            
            Log.Information("BrowserAudioOutput: Play");
        }

        public void Pause()
        {
            if (PlaybackState == PlaybackState.Playing)
            {
                AudioBridge.StopContinuousFeed();
            }
            PlaybackState = PlaybackState.Paused;
            Log.Information("BrowserAudioOutput: Pause");
        }

        public void Stop()
        {
            AudioBridge.StopContinuousFeed();
            AudioBridge.StopPlayback();
            PlaybackState = PlaybackState.Stopped;
            
            // Clear ring buffer
            lock (ringLock)
            {
                ringAvailable = 0;
                ringWritePos = 0;
                ringReadPos = 0;
            }
            
            Log.Information("BrowserAudioOutput: Stop");
        }

        [JSExport]
        public static void OnAudioDataRequested()
        {
            Instance?.FillBuffer();
        }

        private void FillBuffer()
        {
            if (sampleProvider == null || PlaybackState != PlaybackState.Playing)
            {
                return;
            }

            // Read samples from provider
            float[] buffer = new float[BufferFrames * Channels];
            int samplesRead = sampleProvider.Read(buffer, 0, buffer.Length);
            
            if (samplesRead == 0)
            {
                eof = true;
                return;
            }

            // Update time position
            currentTimeMs += samplesRead / (double)Channels * 1000.0 / SampleRate;
            
            // Copy samples to ring buffer
            if (ringBuffer != null)
            {
                lock (ringLock)
                {
                    for (int i = 0; i < samplesRead; i++)
                    {
                        ringBuffer[ringWritePos] = buffer[i];
                        ringWritePos = (ringWritePos + 1) % ringBuffer.Length;
                    }
                    ringAvailable += samplesRead;
                }
            }
            
            // For now, send silence - audio transfer needs different approacback
            if (isWorkletReady)
            {
                // AudioBridge.FeedAudioData(); // TODO: fix array marshalling
            }
        }

        /// Play a test tone (440Hz sine wave) to verify audio works
        /// todo: Remove after testing
        public void PlayTestTone()
        {
            Log.Information("BrowserAudioOutput: Playing test tone!");
            
            // Create a simple test tone generator
            sampleProvider = new TestToneProvider(440, SampleRate);
            sampleProvider = sampleProvider.ToStereo();
            
            Play();
        }

        /// Simple sine wave generator for testing
        /// todo: Remove after testing
        private class TestToneProvider : ISampleProvider       {
            private readonly double frequency;
            private readonly int sampleRate;
            private double phase;
            
            public WaveFormat WaveFormat { get; }
            
            public TestToneProvider(double frequency, int sampleRate)
            {
                this.frequency = frequency;
                this.sampleRate = sampleRate;
                this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            }
            
            public int Read(float[] buffer, int offset, int count)
            {
                double phaseIncrement = 2 * Math.PI * frequency / sampleRate;
                
                for (int i = 0; i < count; i++)
                {
                    double sample = Math.Sin(phase) * 0.3; // 30% volume
                    buffer[offset + i] = (float)sample;
                    phase += phaseIncrement;
                    if (phase > 2 * Math.PI) phase -= 2 * Math.PI;
                }
                
                return count;
            }
        }

        public long GetPosition()
        {
            if (eof && PlaybackState == PlaybackState.Playing)
            {
                Stop();
            }
            return (long)(Math.Max(0, currentTimeMs) / 1000 * SampleRate * 2 * Channels);
        }

        public void SelectDevice(Guid guid, int deviceNumber)
        {
            // Browser uses default device, no selection needed
            DeviceNumber = deviceNumber;
        }

        public List<AudioOutputDevice> GetOutputDevices()
        {
            if (devices.Count == 0)
            {
                devices.Add(new AudioOutputDevice
                {
                    name = "Default Web Audio",
                    api = "Web Audio API",
                    deviceNumber = 0,
                    guid = Guid.Empty
                });
            }
            return devices;
        }

        #region IDisposable

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    ringBuffer = null;
                }
                disposedValue = true;
            }
        }

        ~BrowserAudioOutput()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    public static partial class AudioBridge
    {
        [JSImport("initAudio", "AudioBridge")]
        public static partial System.Threading.Tasks.Task<bool> InitAudio();

        [JSImport("initWorldline", "AudioBridge")]
        public static partial System.Threading.Tasks.Task<bool> InitWorldline();

        [JSImport("registerAudioWorklet", "AudioBridge")]
        public static partial System.Threading.Tasks.Task<bool> RegisterAudioWorklet();

        [JSImport("createWorkletNode", "AudioBridge")]
        public static partial System.Threading.Tasks.Task<bool> CreateWorkletNode();

        [JSImport("setAudioCallback", "AudioBridge")]
        public static partial void SetAudioCallback();

        [JSImport("startPlayback", "AudioBridge")]
        public static partial void StartPlayback();

        [JSImport("stopPlayback", "AudioBridge")]
        public static partial void StopPlayback();

        [JSImport("startContinuousFeed", "AudioBridge")]
        public static partial void StartContinuousFeed(int intervalMs);

        [JSImport("stopContinuousFeed", "AudioBridge")]
        public static partial void StopContinuousFeed();

        [JSImport("feedAudioData", "AudioBridge")]
        public static partial void FeedAudioData();

        [JSImport("resumeAudio", "AudioBridge")]
        public static partial System.Threading.Tasks.Task<bool> ResumeAudio();
    }
}
