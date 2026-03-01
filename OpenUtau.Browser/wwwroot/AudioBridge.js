let worldline = null;
let audioContext = null;
let audioWorklet = null;
let isPlaying = false;
let audioDataCallback = null;
let lastStatsLogAt = 0;
let workletRingAvailable = 0;
let workletRingCapacity = 1;
let hasWorkletStats = false;
let createNodePromise = null;
const AUDIO_DEBUG = false;
const TARGET_LOW_FILL = 0.70;
const TARGET_HIGH_FILL = 0.92;
let primeCallbacksRemaining = 0;
let callbackInFlight = false;

function debugLog(...args) {
    if (AUDIO_DEBUG) {
        console.log(...args);
    }
}

// Shared audio buffer for C# to write into
let sharedAudioBuffer = null;
const CHANNELS = 2;
const BUFFER_FRAMES = 128;
const CALLBACK_CHUNK_SAMPLES = 512 * CHANNELS;

// Initialize Web Audio API
export async function initAudio() {
    debugLog('[AudioBridge] initAudio called');
    audioContext = new (window.AudioContext || window.webkitAudioContext)({
        latencyHint: 'interactive'
    });
    
    if (audioContext.state === 'suspended') {
        await audioContext.resume();
    }
    
    // Allocate shared buffer for audio
    sharedAudioBuffer = new Float32Array(BUFFER_FRAMES * CHANNELS);
    
    debugLog('[AudioBridge] Web Audio initialized, sampleRate:', audioContext.sampleRate);
    debugLog('[AudioBridge] initAudio returning:', true);
    return true;
}

export function getSampleRate() {
    return Math.round(audioContext?.sampleRate || 0);
}

// Load worldline WASM
export async function initWorldline() {
    debugLog('[AudioBridge] initWorldline called');
    if (worldline) {
        debugLog('[AudioBridge] initWorldline returning:', true);
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
    
    debugLog('[AudioBridge] Worldline loaded');
    debugLog('[AudioBridge] initWorldline returning:', true);
    return true;
}

// Register AudioWorklet processor
export async function registerAudioWorklet() {
    debugLog('[AudioBridge] registerAudioWorklet called');
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
                    this.lowWater = this.samplesPerBlock * 64;
                    
                    this.ringBuffer = new Float32Array(this.samplesPerBlock * 128);
                    this.ringWritePos = 0;
                    this.ringReadPos = 0;
                    this.ringAvailable = 0;
                    this.underrunCount = 0;
                    this.overflowCount = 0;
                    this.processCount = 0;
                    this.playing = false;
                    this.requestPending = false;
                    
                    this.port.onmessage = (event) => {
                        if (event.data.type === 'audio') {
                            const samples = event.data.samples;
                            const sampleCount = samples.length - (samples.length % this.channelCount);
                            for (let i = 0; i < sampleCount; i++) {
                                if (this.ringAvailable >= this.ringBuffer.length - (this.channelCount - 1)) {
                                    this.ringReadPos = (this.ringReadPos + this.channelCount) % this.ringBuffer.length;
                                    this.ringAvailable -= this.channelCount;
                                    this.overflowCount++;
                                }
                                this.ringBuffer[this.ringWritePos] = samples[i];
                                this.ringWritePos = (this.ringWritePos + 1) % this.ringBuffer.length;
                                this.ringAvailable++;
                            }
                            this.requestPending = false;
                        } else if (event.data.type === 'resetStats') {
                            this.underrunCount = 0;
                            this.overflowCount = 0;
                            this.requestPending = false;
                        } else if (event.data.type === 'setPlaying') {
                            this.playing = !!event.data.playing;
                            if (!this.playing) {
                                this.requestPending = false;
                            }
                        } else if (event.data.type === 'resetBuffer') {
                            this.ringWritePos = 0;
                            this.ringReadPos = 0;
                            this.ringAvailable = 0;
                            this.requestPending = false;
                        }
                    };
                }
                
                process(inputs, outputs, parameters) {
                    const output = outputs[0];
                    if (!output || output.length === 0) {
                        return true;
                    }
                    const left = output[0];
                    const right = output[1] || left;
                    if (!left) {
                        return true;
                    }

                    const hasStereoOutput = output.length > 1 && output[1];

                    if (!this.playing) {
                        for (let i = 0; i < left.length; i++) {
                            left[i] = 0;
                            if (hasStereoOutput) {
                                right[i] = 0;
                            }
                        }
                        return true;
                    }
                    
                    for (let i = 0; i < left.length; i++) {
                        if (this.ringAvailable >= this.channelCount) {
                            const sampleL = this.ringBuffer[this.ringReadPos];
                            this.ringReadPos = (this.ringReadPos + 1) % this.ringBuffer.length;
                            const sampleR = this.ringBuffer[this.ringReadPos];
                            this.ringReadPos = (this.ringReadPos + 1) % this.ringBuffer.length;
                            this.ringAvailable -= this.channelCount;

                            if (hasStereoOutput) {
                                left[i] = sampleL;
                                right[i] = sampleR;
                            } else {
                                left[i] = 0.5 * (sampleL + sampleR);
                            }
                        } else {
                            left[i] = 0;
                            if (hasStereoOutput) {
                                right[i] = 0;
                            }
                            this.underrunCount++;
                        }
                    }

                    this.processCount++;
                    if ((this.processCount % 4) === 0) {
                        this.port.postMessage({
                            type: 'stats',
                            ringAvailable: this.ringAvailable,
                            ringCapacity: this.ringBuffer.length,
                            underrunCount: this.underrunCount,
                            overflowCount: this.overflowCount,
                        });
                    }

                    if (this.ringAvailable <= this.lowWater && !this.requestPending) {
                        this.requestPending = true;
                        this.port.postMessage({
                            type: 'needData',
                            ringAvailable: this.ringAvailable,
                            ringCapacity: this.ringBuffer.length,
                        });
                    }
                    
                    return true;
                }
            }
            
            registerProcessor('audio-processor', AudioProcessor);
        `;
        
        const blob = new Blob([workletCode], { type: 'application/javascript' });
        const workletUrl = URL.createObjectURL(blob);
        
        const result = await audioContext.audioWorklet.addModule(workletUrl);
        debugLog('[AudioBridge] addModule result:', result);
        
        debugLog('[AudioBridge] AudioWorklet registered');
        debugLog('[AudioBridge] registerAudioWorklet returning:', true);
        return true;
    } catch (e) {
        console.error('[AudioBridge] Failed to register AudioWorklet:', e);
        debugLog('[AudioBridge] registerAudioWorklet returning:', false);
        return false;
    }
}

// Create the AudioWorklet node
export async function createWorkletNode() {
    debugLog('[AudioBridge] createWorkletNode called');
    if (!audioContext) {
        debugLog('[AudioBridge] createWorkletNode returning:', false);
        return false;
    }

    if (audioWorklet) {
        return true;
    }
    if (createNodePromise) {
        return await createNodePromise;
    }
    createNodePromise = (async () => {
    
    try {
        audioWorklet = new AudioWorkletNode(audioContext, 'audio-processor', {
            numberOfInputs: 0,
            numberOfOutputs: 1,
            outputChannelCount: [2],
            channelCount: 2,
            channelCountMode: 'explicit',
            channelInterpretation: 'speakers',
        });
        audioWorklet.port.onmessage = (event) => {
            if (event.data?.type === 'stats') {
                workletRingAvailable = event.data.ringAvailable || 0;
                workletRingCapacity = event.data.ringCapacity || 1;
                hasWorkletStats = true;

                const now = performance.now();
                if (AUDIO_DEBUG && now - lastStatsLogAt > 1000) {
                    const fill = event.data.ringCapacity > 0
                        ? ((event.data.ringAvailable / event.data.ringCapacity) * 100).toFixed(1)
                        : '0.0';
                    console.log(
                        '[AudioBridge] stats fill=' + fill + '% underrun=' + event.data.underrunCount + ' overflow=' + event.data.overflowCount,
                    );
                    lastStatsLogAt = now;
                }
            } else if (event.data?.type === 'needData') {
                requestAudioData('worklet', event.data.ringAvailable, event.data.ringCapacity);
            }
        };
        audioWorklet.connect(audioContext.destination);
        
        debugLog('[AudioBridge] AudioWorklet node created');
        debugLog('[AudioBridge] createWorkletNode returning:', true);
        return true;
    } catch (e) {
        console.error('[AudioBridge] Failed to create AudioWorklet node:', e);
        audioWorklet = null;
        debugLog('[AudioBridge] createWorkletNode returning:', false);
        return false;
    } finally {
        createNodePromise = null;
    }
    })();
    return await createNodePromise;
}

export async function setAudioCallback() {
    debugLog('[AudioBridge] setAudioCallback called');
    try {
        const runtime = await globalThis.getDotnetRuntime?.(0);
        if (!runtime) {
            console.warn('[AudioBridge] .NET runtime not available for audio callback');
            return;
        }

        const exports = await runtime.getAssemblyExports('OpenUtau.Browser.dll');

        const findFunction = (obj, name) => {
            if (!obj || typeof obj !== 'object') {
                return null;
            }
            if (typeof obj[name] === 'function') {
                return obj[name];
            }
            for (const value of Object.values(obj)) {
                if (value && typeof value === 'object') {
                    const found = findFunction(value, name);
                    if (found) {
                        return found;
                    }
                }
            }
            return null;
        };

        audioDataCallback = findFunction(exports, 'OnAudioDataRequested');
        if (!audioDataCallback) {
            console.warn('[AudioBridge] Could not find OnAudioDataRequested export');
        }
    } catch (error) {
        console.error('[AudioBridge] Failed to bind audio callback:', error);
    }
    debugLog('[AudioBridge] Audio callback set');
}

// Start playback
export function startPlayback() {
    if (!audioContext) return;
    
    if (audioContext.state === 'suspended') {
        audioContext.resume();
    }
    
    if (!audioWorklet) {
        registerAudioWorklet().then(workletReady => {
            if (!workletReady) return;
            createWorkletNode().then(() => {
                isPlaying = true;
                audioWorklet?.port.postMessage({ type: 'setPlaying', playing: true });
                audioWorklet?.port.postMessage({ type: 'resetStats' });
                debugLog('[AudioBridge] Playback started');
            });
        });
    } else {
        isPlaying = true;
        audioWorklet?.port.postMessage({ type: 'setPlaying', playing: true });
        audioWorklet?.port.postMessage({ type: 'resetStats' });
        debugLog('[AudioBridge] Playback started');
    }
}

// Stop playback
export function stopPlayback() {
    isPlaying = false;
    audioWorklet?.port.postMessage({ type: 'setPlaying', playing: false });
    debugLog('[AudioBridge] Playback stopped');
}

// C# will have written samples to the shared buffer before calling this
export function feedAudioData(samplesView, sampleCount) {
    if (!audioWorklet || !isPlaying || !samplesView) {
        return;
    }

    let source = null;

    if (samplesView instanceof Float32Array) {
        source = samplesView;
    } else if (samplesView instanceof Float64Array) {
        source = new Float32Array(samplesView.length);
        for (let i = 0; i < samplesView.length; i++) {
            source[i] = samplesView[i];
        }
    } else if (ArrayBuffer.isView(samplesView)) {
        const len = samplesView.length ?? Math.floor(samplesView.byteLength / samplesView.BYTES_PER_ELEMENT);
        source = new Float32Array(len);
        for (let i = 0; i < len; i++) {
            source[i] = samplesView[i];
        }
    } else if (samplesView instanceof ArrayBuffer) {
        source = new Float32Array(samplesView);
    } else if (Array.isArray(samplesView)) {
        source = new Float32Array(samplesView.length);
        for (let i = 0; i < samplesView.length; i++) {
            source[i] = samplesView[i];
        }
    } else {
        const arr = Array.from(samplesView);
        source = new Float32Array(arr.length);
        for (let i = 0; i < arr.length; i++) {
            source[i] = arr[i];
        }
    }

    if (!source || source.length === 0) {
        return;
    }

    let effectiveLength = source.length;
    if (typeof sampleCount === 'number' && sampleCount >= 0 && sampleCount < effectiveLength) {
        effectiveLength = sampleCount;
    }

    const alignedLength = effectiveLength - (effectiveLength % CHANNELS);
    if (alignedLength <= 0) {
        return;
    }

    const payload = new Float32Array(alignedLength);
    for (let i = 0; i < alignedLength; i++) {
        payload[i] = source[i];
    }
    
    audioWorklet.port.postMessage({
        type: 'audio',
        samples: payload
    }, [payload.buffer]);
}

// Start continuous feed loop
let feedInterval = null;

async function requestAudioData(source, ringAvailable, ringCapacity) {
    if (!isPlaying || !audioWorklet || !audioDataCallback) {
        return;
    }
    if (callbackInFlight) {
        return;
    }
    callbackInFlight = true;
    try {
        let callbacksToRun = 1;
        if (typeof ringAvailable === 'number' && typeof ringCapacity === 'number' && ringCapacity > 0) {
            const targetSamples = Math.floor(ringCapacity * TARGET_HIGH_FILL);
            const missingSamples = Math.max(0, targetSamples - ringAvailable);
            callbacksToRun = Math.max(1, Math.min(8, Math.ceil(missingSamples / CALLBACK_CHUNK_SAMPLES)));
        }

        for (let i = 0; i < callbacksToRun && isPlaying; i++) {
            await audioDataCallback();
        }
    } catch (error) {
        console.error('[AudioBridge] Audio callback failed:', error, 'source=', source);
    } finally {
        callbackInFlight = false;
    }
}

export function startContinuousFeed(intervalMs) {
    debugLog('[AudioBridge] startContinuousFeed called with interval:', intervalMs);
    if (feedInterval) clearInterval(feedInterval);
    primeCallbacksRemaining = 10;

    if (audioWorklet) {
        audioWorklet.port.postMessage({ type: 'resetBuffer' });
        audioWorklet.port.postMessage({ type: 'resetStats' });
    }

    void (async () => {
        for (let i = 0; i < primeCallbacksRemaining && isPlaying; i++) {
            await requestAudioData('prime');
        }
    })();

    feedInterval = null;
    debugLog('[AudioBridge] startContinuousFeed done');
}

export function stopContinuousFeed() {
    debugLog('[AudioBridge] stopContinuousFeed called');
    if (feedInterval) {
        clearInterval(feedInterval);
        feedInterval = null;
    }
    callbackInFlight = false;
    primeCallbacksRemaining = 0;
    if (audioWorklet) {
        audioWorklet.port.postMessage({ type: 'setPlaying', playing: false });
        audioWorklet.port.postMessage({ type: 'resetBuffer' });
    }
    debugLog('[AudioBridge] stopContinuousFeed done');
}

// Resume audio context
export async function resumeAudio() {
    debugLog('[AudioBridge] resumeAudio called, state:', audioContext?.state);
    if (audioContext && audioContext.state === 'suspended') {
        await audioContext.resume();
    }
    const result = audioContext?.state === 'running';
    debugLog('[AudioBridge] resumeAudio returning:', result);
    return result;
}

// Debug
export function log(message) {
    console.log('[AudioBridge]', message);
}
