import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

console.log('[main.js] Starting...');

const script = document.createElement('script');
await new Promise((resolve, reject) => {
    script.onload = resolve;
    script.onerror = () => reject(new Error(`Failed to load ./runtimes/wasm/worldline_wasm.js`));
    script.src = './runtimes/wasm/worldline_wasm.js';
    document.head.appendChild(script);
});
console.log('[main.js] Worldline loaded, type:', typeof Worldline);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const { setModuleImports, getConfig } = dotnetRuntime;

// Import AudioBridge as ES6 module
const audioBridge = await import('./AudioBridge.js');

setModuleImports('AudioBridge', audioBridge);
console.log('[main.js] AudioBridge setModuleImports done');

const config = getConfig();

console.log('[main.js] Running .NET main...');

// Use the runtime returned by `create()` to run the app
await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);

console.log('[main.js] App started successfully!');
