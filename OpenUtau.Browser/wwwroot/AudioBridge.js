let worldline = null;
let audioContext = null;
let audioWorklet = null;
let isPlaying = false;

// Shared audio buffer for C# to write into
let sharedAudioBuffer = null;
const SAMPLE_RATE = 44100;
const CHANNELS = 2;
const BUFFER_FRAMES = 128;

// Initialize Web Audio API
export async function initAudio() {
    console.log('[AudioBridge] initAudio called');
    audioContext = new (window.AudioContext || window.webkitAudioContext)({
        sampleRate: SAMPLE_RATE,
        latencyHint: 'interactive'
    });
    
    if (audioContext.state === 'suspended') {
        await audioContext.resume();
    }
    
    // Allocate shared buffer for audio
    sharedAudioBuffer = new Float32Array(BUFFER_FRAMES * CHANNELS);
    
    console.log('[AudioBridge] Web Audio initialized, sampleRate:', audioContext.sampleRate);
    console.log('[AudioBridge] initAudio returning:', true);
    return true;
}

// Load worldline WASM
export async function initWorldline() {
    console.log('[AudioBridge] initWorldline called');
    if (worldline) {
        console.log('[AudioBridge] initWorldline returning:', true);
        return true;
    }
    
    if (typeof Worldline === 'function') {
        worldline = await Worldline();
    } else {
        for (let i = 0; i < 100 && !worldline; i++) {
            await new Promise(r => setTimeout(r, 50));
            worldline = typeof Worldline === 'function' ? await Worldline() : null;
        }
    }
    
    if (!worldline) {
        throw new Error('[AudioBridge] Worldline WASM failed to load');
    }
    
    console.log('[AudioBridge] Worldline loaded');
    console.log('[AudioBridge] initWorldline returning:', true);
    return true;
}

// Register AudioWorklet processor
export async function registerAudioWorklet() {
    console.log('[AudioBridge] registerAudioWorklet called');
    if (!audioContext) {
        console.error('[AudioBridge] No AudioContext');
        return false;
    }
    
    try {
        const workletCode = `
            class AudioProcessor extends AudioWorkletProcessor {
                constructor() {
                    super();
                    this.bufferSize = 128;
                    this.channelCount = 2;
                    this.samplesPerBlock = this.bufferSize * this.channelCount;
                    
                    this.ringBuffer = new Float32Array(this.samplesPerBlock * 16);
                    this.ringWritePos = 0;
                    this.ringReadPos = 0;
                    this.ringAvailable = 0;
                    
                    this.port.onmessage = (event) => {
                        if (event.data.type === 'audio') {
                            const samples = event.data.samples;
                            for (let i = 0; i < samples.length; i++) {
                                this.ringBuffer[this.ringWritePos] = samples[i];
                                this.ringWritePos = (this.ringWritePos + 1) % this.ringBuffer.length;
                            }
                            this.ringAvailable += samples.length;
                        }
                    };
                }
                
                process(inputs, outputs, parameters) {
                    const output = outputs[0];
                    const left = output[0];
                    const right = output[1];
                    
                    for (let i = 0; i < left.length; i++) {
                        if (this.ringAvailable >= this.channelCount) {
                            left[i] = this.ringBuffer[this.ringReadPos];
                            this.ringReadPos = (this.ringReadPos + 1) % this.ringBuffer.length;
                            right[i] = this.ringBuffer[this.ringReadPos];
                            this.ringReadPos = (this.ringReadPos + 1) % this.ringBuffer.length;
                            this.ringAvailable -= this.channelCount;
                        } else {
                            left[i] = 0;
                            right[i] = 0;
                        }
                    }
                    
                    return true;
                }
            }
            
            registerProcessor('audio-processor', AudioProcessor);
        `;
        
        const blob = new Blob([workletCode], { type: 'application/javascript' });
        const workletUrl = URL.createObjectURL(blob);
        
        const result = await audioContext.audioWorklet.addModule(workletUrl);
        console.log('[AudioBridge] addModule result:', result);
        
        console.log('[AudioBridge] AudioWorklet registered');
        console.log('[AudioBridge] registerAudioWorklet returning:', true);
        return true;
    } catch (e) {
        console.error('[AudioBridge] Failed to register AudioWorklet:', e);
        console.log('[AudioBridge] registerAudioWorklet returning:', false);
        return false;
    }
}

// Create the AudioWorklet node
export async function createWorkletNode() {
    console.log('[AudioBridge] createWorkletNode called');
    if (!audioContext) {
        console.log('[AudioBridge] createWorkletNode returning:', false);
        return false;
    }
    
    try {
        audioWorklet = new AudioWorkletNode(audioContext, 'audio-processor');
        audioWorklet.connect(audioContext.destination);
        
        console.log('[AudioBridge] AudioWorklet node created');
        console.log('[AudioBridge] createWorkletNode returning:', true);
        return true;
    } catch (e) {
        console.error('[AudioBridge] Failed to create AudioWorklet node:', e);
        console.log('[AudioBridge] createWorkletNode returning:', false);
        return false;
    }
}

export function setAudioCallback() {
    console.log('[AudioBridge] setAudioCallback called');
    console.log('[AudioBridge] Audio callback set');
}

// Start playback
export async function startPlayback() {
    if (!audioContext) return;
    
    if (audioContext.state === 'suspended') {
        await audioContext.resume();
    }
    
    if (!audioWorklet) {
        const workletReady = await registerAudioWorklet();
        if (!workletReady) return;
        await createWorkletNode();
    }
    
    isPlaying = true;
    console.log('[AudioBridge] Playback started');
}

// Stop playback
export function stopPlayback() {
    isPlaying = false;
    console.log('[AudioBridge] Playback stopped');
}

// C# will have written samples to the shared buffer before calling this
export function feedAudioData() {
    console.log('[AudioBridge] feedAudioData called');
    if (!audioWorklet || !isPlaying || !sharedAudioBuffer) {
        console.log('[AudioBridge] feedAudioData: early return');
        return;
    }
    
    audioWorklet.port.postMessage({
        type: 'audio',
        samples: sharedAudioBuffer
    });
    console.log('[AudioBridge] feedAudioData done');
}

// Start continuous feed loop
let feedInterval = null;

export function startContinuousFeed(intervalMs) {
    console.log('[AudioBridge] startContinuousFeed called with interval:', intervalMs);
    if (feedInterval) clearInterval(feedInterval);
    
    feedInterval = setInterval(async () => {
        if (!isPlaying || !audioWorklet) return;
        
        // Get shared buffer from C# via dotnet runtime
        // For now, we'll generate silence, this will be replaced with actual C# callback once i know how to do that properly 
        sharedAudioBuffer.fill(0);
        
        audioWorklet.port.postMessage({
            type: 'audio',
            samples: sharedAudioBuffer
        });
    }, intervalMs);
    console.log('[AudioBridge] startContinuousFeed done');
}

export function stopContinuousFeed() {
    console.log('[AudioBridge] stopContinuousFeed called');
    if (feedInterval) {
        clearInterval(feedInterval);
        feedInterval = null;
    }
    console.log('[AudioBridge] stopContinuousFeed done');
}

// Resume audio context
export async function resumeAudio() {
    console.log('[AudioBridge] resumeAudio called, state:', audioContext?.state);
    if (audioContext && audioContext.state === 'suspended') {
        await audioContext.resume();
    }
    const result = audioContext?.state === 'running';
    console.log('[AudioBridge] resumeAudio returning:', result);
    return result;
}

// Debug
export function log(message) {
    console.log('[AudioBridge]', message);
}
