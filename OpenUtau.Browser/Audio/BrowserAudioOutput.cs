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
        const int BufferFrames = 512;
        
        public PlaybackState PlaybackState { get; private set; }
        public int DeviceNumber { get; private set; }

        private ISampleProvider? sampleProvider;
        private double currentTimeMs;
        private bool eof;
        private bool isInitialized;
        private bool isWorkletReady;
        private int feedInProgress;
        private int targetSampleRate = 44100;
        private readonly float[] callbackBuffer = new float[BufferFrames * Channels];
        private readonly double[] callbackBufferJs = new double[BufferFrames * Channels];

        private readonly List<AudioOutputDevice> devices = new();
        
        // Singleton instance for static callback
        private static BrowserAudioOutput? Instance;

        public BrowserAudioOutput()
        {
            Instance = this;
            
            // Initialize async
            _ = InitializeAsync();
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                isInitialized = false;
                isWorkletReady = false;

                // Initialize Web Audio API
                Log.Information("BrowserAudioOutput: Calling InitAudio...");
                var initAudioReady = await AudioBridge.InitAudio();
                if (!initAudioReady)
                {
                    Log.Error("BrowserAudioOutput: InitAudio returned false");
                    return;
                }
                Log.Information("BrowserAudioOutput: InitAudio completed");

                var browserSampleRate = AudioBridge.GetSampleRate();
                if (browserSampleRate > 0)
                {
                    targetSampleRate = browserSampleRate;
                }
                Log.Information("BrowserAudioOutput: Browser sample rate = {SampleRate}", targetSampleRate);
                
                // Load worldline WASM (includes miniaudio)
                Log.Information("BrowserAudioOutput: Calling InitWorldline...");
                var initWorldlineReady = await AudioBridge.InitWorldline();
                if (!initWorldlineReady)
                {
                    Log.Error("BrowserAudioOutput: InitWorldline returned false");
                    return;
                }
                Log.Information("BrowserAudioOutput: InitWorldline completed");
                
                // Register AudioWorklet processor
                Log.Information("BrowserAudioOutput: Calling RegisterAudioWorklet...");
                var registerWorkletReady = await AudioBridge.RegisterAudioWorklet();
                if (!registerWorkletReady)
                {
                    Log.Error("BrowserAudioOutput: RegisterAudioWorklet returned false");
                    return;
                }
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
                isInitialized = false;
                Log.Error(ex, "BrowserAudioOutput: Failed to initialize");
            }
        }

        public void Init(ISampleProvider sampleProvider)
        {
            PlaybackState = PlaybackState.Stopped;
            eof = false;
            currentTimeMs = 0;

            // Resample if needed
            if (targetSampleRate != sampleProvider.WaveFormat.SampleRate)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, targetSampleRate);
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
            AudioBridge.StartContinuousFeed(10); // Feed every 10ms
            
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

            Log.Information("BrowserAudioOutput: Stop");
        }

        [JSExport]
        public static System.Threading.Tasks.Task<bool> OnAudioDataRequested()
        {
            Instance?.FillBuffer();
            return System.Threading.Tasks.Task.FromResult(true);
        }

        private void FillBuffer()
        {
            if (System.Threading.Interlocked.Exchange(ref feedInProgress, 1) == 1)
            {
                return;
            }

            try
            {
            if (sampleProvider == null || PlaybackState != PlaybackState.Playing)
            {
                return;
            }

            // Read samples from provider
            int samplesRead = sampleProvider.Read(callbackBuffer, 0, callbackBuffer.Length);
            
            if (samplesRead == 0)
            {
                eof = true;
                return;
            }

            // Update time position
            currentTimeMs += samplesRead / (double)Channels * 1000.0 / targetSampleRate;
            
            // Send samples to JS worklet
            if (isWorkletReady && samplesRead > 0)
            {
                for (int i = 0; i < samplesRead; i++)
                {
                    callbackBufferJs[i] = callbackBuffer[i];
                }
                AudioBridge.FeedAudioData(callbackBufferJs, samplesRead);
            }
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref feedInProgress, 0);
            }
        }

        /// Play a test tone (440Hz sine wave) to verify audio works
        /// todo: Remove after testing
        public void PlayTestTone()
        {
            Log.Information("BrowserAudioOutput: Playing test tone!");
            
            // Create a simple test tone generator
            sampleProvider = new TestToneProvider(440, targetSampleRate);
            
            Play();

            _ = System.Threading.Tasks.Task.Run(async () => {
                await System.Threading.Tasks.Task.Delay(5000);
                if (PlaybackState == PlaybackState.Playing)
                {
                    Stop();
                    Log.Information("BrowserAudioOutput: Test tone stopped after 5 seconds");
                }
            });
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
                this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
            }
            
            public int Read(float[] buffer, int offset, int count)
            {
                double phaseIncrement = 2 * Math.PI * frequency / sampleRate;

                int samplePairs = count / 2;
                for (int i = 0; i < samplePairs; i++)
                {
                    double sample = Math.Sin(phase) * 0.3; // 30% volume
                    int baseIndex = offset + i * 2;
                    buffer[baseIndex] = (float)sample;
                    buffer[baseIndex + 1] = (float)sample;
                    phase += phaseIncrement;
                    if (phase > 2 * Math.PI) phase -= 2 * Math.PI;
                }

                return samplePairs * 2;
            }
        }

        public long GetPosition()
        {
            if (eof && PlaybackState == PlaybackState.Playing)
            {
                Stop();
            }
            return (long)(Math.Max(0, currentTimeMs) / 1000 * targetSampleRate * 2 * Channels);
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
        public static partial void FeedAudioData(double[] samples, int sampleCount);

        [JSImport("resumeAudio", "AudioBridge")]
        public static partial System.Threading.Tasks.Task<bool> ResumeAudio();

        [JSImport("getSampleRate", "AudioBridge")]
        public static partial int GetSampleRate();
    }
}
