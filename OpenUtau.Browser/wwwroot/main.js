import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

console.log('[main.js] Starting...');

const script = document.createElement('script');
await new Promise((resolve, reject) => {
    script.onload = resolve;
    script.onerror = () => reject(new Error(`Failed to load worldline_wasm.js`));
    script.src = '../runtimes/native/worldline_wasm.js';
    document.head.appendChild(script);
});
console.log('[main.js] Worldline loaded, type:', typeof Worldline);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const { setModuleImports, getConfig } = dotnetRuntime;

// TODO: Proper implementation of Blazor._internal.invokeJSJson and related functions
const blazorInternal = {
    Blazor: {
        _internal: {
            invokeJSJson: (...args) => {
                console.warn(`[main.js] Blazor._internal.invokeJSJson called unexpectedly: ${JSON.stringify(args)}`);
                return null;
            },
            endInvokeDotNetFromJS: () => {
            },
            receiveByteArray: () => {
            },
        },
    },
};

setModuleImports('blazor-internal', blazorInternal);
console.log('[main.js] blazor-internal setModuleImports done');

// Import AudioBridge as ES6 module
const audioBridge = await import('./AudioBridge.js');

setModuleImports('AudioBridge', audioBridge);
console.log('[main.js] AudioBridge setModuleImports done');

const config = getConfig();

console.log('[main.js] Running .NET main...');

// Use the runtime returned by `create()` to run the app
await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);

console.log('[main.js] App started successfully!');

// Show audio enable button on load
const audioOverlay = document.getElementById('audio-enable-overlay');
const audioBtn = document.getElementById('audio-enable-btn');

if (audioOverlay && audioBtn) {
    audioOverlay.style.display = 'flex';
    
    let isAudioInitializing = false;
    
    audioBtn.addEventListener('click', async () => {
        if (isAudioInitializing) return;
        isAudioInitializing = true;
        try {
            // Initialize audio if not already done (via AudioBridge)
            if (audioBridge.initAudio) {
                await audioBridge.initAudio();
            }
            if (audioBridge.resumeAudio) {
                await audioBridge.resumeAudio();
            }
            audioOverlay.style.display = 'none';
            console.log('[main.js] Audio enabled');
        } catch (e) {
            console.error('[main.js] Failed to enable audio:', e);
        } finally {
            isAudioInitializing = false;
        }
    });
}
