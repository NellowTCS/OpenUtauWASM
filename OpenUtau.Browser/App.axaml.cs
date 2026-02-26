using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Browser;
using OpenUtau.App.Views;
using OpenUtau.Browser.Audio;
using OpenUtau.App.Browser;
using OpenUtau.Colors;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App {
    public class App : Application {
        public override void Initialize() {
            Log.Information("Initializing application.");
            AvaloniaXamlLoader.Load(this);
            InitializeCulture();
            InitializeTheme();
            Log.Information("Initialized application.");
        }

        public override void OnFrameworkInitializationCompleted() {
            Log.Information("Framework initialization completed.");
            
            var mainThread = Thread.CurrentThread;
            var mainScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            
            Task.Run(() => {
                Log.Information("Initializing OpenUtau.");
                try {
                    OpenUtau.Classic.ToolsManager.Inst.Initialize();
                    Log.Information("ToolsManager initialized");
                    OpenUtau.Core.SingerManager.Inst.Initialize();
                    Log.Information("SingerManager initialized");
                    DocManager.Inst.Initialize(mainThread, mainScheduler);
                    Log.Information("DocManager initialized");
                    DocManager.Inst.PostOnUIThread = action => Avalonia.Threading.Dispatcher.UIThread.Post(action);
                    Log.Information("OpenUtau initialized.");
                } catch (Exception ex) {
                    Log.Error(ex, "Failed to initialize OpenUtau");
                    Console.WriteLine("[App] ERROR: " + ex);
                }
            }).ContinueWith(t => {
                if (t.IsFaulted) {
                    Log.Error(t.Exception?.Flatten(), "Failed to initialize OpenUtau.");
                    return;
                }
Log.Information("Creating MainWindow");
                if (ApplicationLifetime is ISingleViewApplicationLifetime singleView) {
                    var mainWindow = new BrowserMainWindow();
                    Log.Information("MainWindow created, initializing project");
                    mainWindow.InitProject();
                    Log.Information("Setting MainView");
                    singleView.MainView = mainWindow;
                    Log.Information("MainView set");
                    
                    if (OS.IsBrowser()) {
                        InitializeBrowserAudio();
                    }
                }
            }, CancellationToken.None, TaskContinuationOptions.None, mainScheduler);

            base.OnFrameworkInitializationCompleted();
        }

        public void InitializeCulture() {
            Log.Information("Initializing culture.");
            string sysLang = CultureInfo.InstalledUICulture.Name;
            string prefLang = Core.Util.Preferences.Default.Language;
            var languages = GetLanguages();
            if (languages.ContainsKey(prefLang)) {
                SetLanguage(prefLang);
            } else if (languages.ContainsKey(sysLang)) {
                SetLanguage(sysLang);
                Core.Util.Preferences.Default.Language = sysLang;
                Core.Util.Preferences.Save();
            } else {
                SetLanguage("en-US");
            }

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            Log.Information("Initialized culture.");
        }

        public static Dictionary<string, IResourceProvider> GetLanguages() {
            if (Current == null) {
                return new();
            }
            var result = new Dictionary<string, IResourceProvider>();
            foreach (string key in Current.Resources.Keys.OfType<string>()) {
                if (key.StartsWith("strings-") &&
                    Current.Resources.TryGetResource(key, ThemeVariant.Default, out var res) &&
                    res is IResourceProvider rp) {
                    result.Add(key.Replace("strings-", ""), rp);
                }
            }
            return result;
        }

        public static void SetLanguage(string language) {
            if (Current == null) {
                return;
            }
            var languages = GetLanguages();
            foreach (var res in languages.Values) {
                Current.Resources.MergedDictionaries.Remove(res);
            }
            if (language != "en-US") {
                Current.Resources.MergedDictionaries.Add(languages["en-US"]);
            }
            if (languages.TryGetValue(language, out var res1)) {
                Current.Resources.MergedDictionaries.Add(res1);
            }
        }

        static void InitializeTheme() {
            Log.Information("Initializing theme.");
            SetTheme();
            Log.Information("Initialized theme.");
        }

        public static void SetTheme() {
            if (Current == null) {
                return;
            }
            var light = (IResourceDictionary) Current.Resources["themes-light"]!;
            var dark = (IResourceDictionary) Current.Resources["themes-dark"]!;
            var custom = (IResourceDictionary) Current.Resources["themes-custom"]!;
            switch (Core.Util.Preferences.Default.ThemeName) { 
                case "Light":
                    ApplyTheme(light);
                    Current.RequestedThemeVariant = ThemeVariant.Light;
                    break;
                case "Dark":
                    ApplyTheme(dark);
                    Current.RequestedThemeVariant = ThemeVariant.Dark;
                    break;
                default:
                    ApplyTheme(custom);
                    CustomTheme.ApplyTheme(Core.Util.Preferences.Default.ThemeName);
                    if (CustomTheme.Default.IsDarkMode == true) {
                        Current.RequestedThemeVariant = ThemeVariant.Dark;
                    } else {
                        Current.RequestedThemeVariant = ThemeVariant.Light;
                    }
                    break;
            }
            ThemeManager.LoadTheme();
        }

        private static void ApplyTheme(IResourceDictionary resDict) { 
            var res = Current?.Resources;
            foreach (var item in resDict) {
                res![item.Key] = item.Value;
            }
        }

private static async void InitializeBrowserAudio() {
            Console.WriteLine("[Audio] InitializeBrowserAudio START");
            try {
              
                Console.WriteLine("[Audio] Importing AudioBridge...");
                
                // Import the AudioBridge JS module
                await JSHost.ImportAsync("AudioBridge", "./AudioBridge.js");
                  Console.WriteLine("[Audio] Creating BrowserAudioOutput...");
                // Set BrowserAudioOutput as the audio output
                var audioOutput = new OpenUtau.Browser.Audio.BrowserAudioOutput();
                PlaybackManager.Inst.AudioOutput = audioOutput;
                
                Console.WriteLine("[Audio] BrowserAudioOutput created");
                Log.Information("Browser audio initialized successfully");
                
                // Play test tone after a short delay to verify audio works
                await System.Threading.Tasks.Task.Delay(3000);
                Console.WriteLine("[Audio] Playing test tone now!");
                audioOutput.PlayTestTone();
                Console.WriteLine("[Audio] Test tone triggered. you should hear a 440Hz sine wave");
                
            } catch (Exception ex) {
                Console.WriteLine("[Audio] ERROR: " + ex);
                Log.Error(ex, "Failed to initialize browser audio");
            }
            Console.WriteLine("[Audio] InitializeBrowserAudio END");
        }
    }
}
