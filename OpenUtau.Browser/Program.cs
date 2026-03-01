using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using OpenUtau.App.Browser;
using OpenUtau.App.Browser;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using Serilog;
using Serilog.Sinks.BrowserConsole;

namespace OpenUtau.App {
    public class Program {
        [STAThread]
        public static async Task Main(string[] args) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            InitLogging();
            if (!OS.IsBrowser()) {
                string processName = Process.GetCurrentProcess().ProcessName;
                if (processName != "dotnet") {
                    var exists = Process.GetProcessesByName(processName).Count() > 1;
                    if (exists) {
                        Log.Information($"Process {processName} already open. Exiting.");
                        return;
                    }
                }
                Log.Information($"{Environment.OSVersion}");
                Log.Information($"{RuntimeInformation.OSDescription} " +
                    $"{RuntimeInformation.OSArchitecture} " +
                    $"{RuntimeInformation.ProcessArchitecture}");
            }
            Log.Information($"OpenUtau v{Assembly.GetEntryAssembly()?.GetName().Version} " +
                $"{RuntimeInformation.RuntimeIdentifier}");
            Log.Information($"Data path = {PathManager.Inst.DataPath}");
            Log.Information($"Cache path = {PathManager.Inst.CachePath}");
            Log.Information($"System encoding = {Encoding.GetEncoding(0)?.WebName ?? "null"}");
            try {
                await Run(args);
                Log.Information($"Exiting.");
            } finally {
                if (!OS.IsMacOS() && !OS.IsBrowser()) {
                    NetMQ.NetMQConfig.Cleanup(/*block=*/false);
                }
            }
            Log.Information($"Exited.");
        }

        public static AppBuilder BuildAvaloniaApp() {
            FontManagerOptions fontOptions = new();
            if (OS.IsLinux() && !OS.IsBrowser()) {
                using Process process = Process.Start(new ProcessStartInfo("fc-match")
                {
                    ArgumentList = { "-f", "%{family}" },
                    RedirectStandardOutput = true
                })!;
                process.WaitForExit();

                string fontFamily = process.StandardOutput.ReadToEnd();
                if (!string.IsNullOrEmpty(fontFamily)) {
                    string [] fontFamilies = fontFamily.Split(',');
                    fontOptions.DefaultFamilyName = fontFamilies[0];
                }
            } else if (OS.IsMacOS()) {
                fontOptions.DefaultFamilyName = "Hiragino Sans, Segoe UI, San Francisco, Helvetica Neue";
            }
            return AppBuilder.Configure<App>()
                .LogToTrace()
                .UseReactiveUI()
                .With(fontOptions);
        }

        public static async Task Run(string[] args) {
            if (OS.IsBrowser()) {
               try {
                    Log.Information("Importing opfsHelper...");
                    await JSHost.ImportAsync("opfsHelper", "../opfsHelper.js");
                    Log.Information("Importing bookmarkHelper...");
                    await JSHost.ImportAsync("bookmarkHelper", "../bookmarkHelper.js");
                    
                    Storage.SetBackend(new OpfsStorageBackend());
                    Log.Information("OPFS storage backend registered");
                } catch (Exception ex) {
                    Log.Error(ex, "Failed to import JS modules");
                }
                await BuildAvaloniaApp()
                    .UseBrowser()
                    .StartBrowserAppAsync("out");
            } else {
                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(
                        args, ShutdownMode.OnMainWindowClose);
            }
        }

        public static void InitLogging() {
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Verbose();
            
            if (OS.IsBrowser()) {
                // Browser: write to console (maps to browser console)
                loggerConfig.WriteTo.BrowserConsole();
            } else {
                // Desktop: write to debug and console
                loggerConfig
                    .WriteTo.Debug()
                    .WriteTo.Logger(lc => lc
                        .MinimumLevel.Information()
                        .WriteTo.Console())
                    .WriteTo.Logger(lc => lc
                        .MinimumLevel.ControlledBy(DebugViewModel.Sink.Inst.LevelSwitch)
                        .WriteTo.Sink(DebugViewModel.Sink.Inst));
            }
            
            Log.Logger = loggerConfig.CreateLogger();
            
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler((sender, args) => {
                Log.Error((Exception)args.ExceptionObject, "Unhandled exception");
            });
            Log.Information("Logging initialized.");
        }
    }
}
