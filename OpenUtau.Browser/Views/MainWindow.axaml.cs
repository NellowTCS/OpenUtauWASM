using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.Browser;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Analysis.Some;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using Serilog;
using Point = Avalonia.Point;

namespace OpenUtau.App.Views {
    public partial class BrowserMainWindow : UserControl, IMainWindow, IMainWindowMarker {
        private readonly KeyModifiers cmdKey =
            OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        private readonly MainWindowViewModel viewModel;

        private PianoRoll? pianoRoll;
        private PartEditState? partEditState;
        private readonly DispatcherTimer timer;

        private bool shouldOpenPartsContextMenu;

        private readonly ReactiveCommand<UPart, Unit> PartRenameCommand;
        private readonly ReactiveCommand<UPart, Unit> PartGotoFileCommand;
        private readonly ReactiveCommand<UPart, Unit> PartReplaceAudioCommand;
        private readonly ReactiveCommand<UPart, Unit> PartTranscribeCommand;
        private readonly ReactiveCommand<UPart, Unit> PartMergeCommand;

        private Panel? dialogHost;

        public BrowserMainWindow() {
            Log.Information("Creating browser main window.");
            InitializeComponent();
            DataContext = viewModel = new MainWindowViewModel {
                AskIfSaveAndContinue = AskIfSaveAndContinue
            };

            viewModel.NewProject();

            timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(15),
                DispatcherPriority.Normal,
                (sender, args) => PlaybackManager.Inst.UpdatePlayPos());
            timer.Start();

            PartRenameCommand = ReactiveCommand.Create<UPart>(part => RenamePart(part));
            PartGotoFileCommand = ReactiveCommand.Create<UPart>(part => GotoFile(part));
            PartReplaceAudioCommand = ReactiveCommand.Create<UPart>(part => ReplaceAudio(part));
            PartTranscribeCommand = ReactiveCommand.Create<UPart>(part => Transcribe(part));
            PartMergeCommand = ReactiveCommand.Create<UPart>(part => MergePart(part));

            AddHandler(DragDrop.DropEvent, OnDrop);

            dialogHost = this.FindControl<Panel>("DialogHost");
            Log.Information("Created browser main window.");
        }

        public void InitProject() {
            viewModel.InitProject();
        }

        public async Task OpenSingersWindowAsync() {
            var dialog = new BrowserSingersDialog();
            dialog.DataContext = new SingersViewModel();
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is Window window) {
                await dialog.ShowDialog(window);
            }
        }

        public void SetPianoRollAttachment() {
        }

        public async Task Save() {
            if (!viewModel.ProjectSaved) {
                await SaveAs();
            } else {
                // In browser, Ustx.Save() does OPFS I/O which uses .Result on JS interop.
                // Must run on a worker thread to avoid deadlocking the UI/JS thread.
                // We call Ustx.Save() directly instead of going through SaveProjectNotification,
                // which would post back to the UI thread and deadlock.
                var project = DocManager.Inst.Project;
                var filePath = project.FilePath;
                await Task.Run(() => Ustx.Save(filePath, project));
                string message = ThemeManager.GetString("progress.saved");
                message = string.Format(message, DateTime.Now);
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, message));
            }
        }

        public async Task ShowDialog(Control dialog) {
            if (dialogHost == null) return;
            
            dialogHost.IsHitTestVisible = true;

            var overlay = new Border {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#80000000")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                ZIndex = 1000,
            };

            var container = new Panel {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                ZIndex = 1001,
            };
            container.Children.Add(dialog);
            overlay.Child = container;

            void CloseDialog() {
                dialogHost!.Children.Remove(overlay);
                dialogHost.IsHitTestVisible = false;
            }

            overlay.PointerPressed += (s, e) => {
                if (e.Source == overlay) {
                    CloseDialog();
                }
            };

            dialogHost.Children.Add(overlay);
            
            await Task.CompletedTask;
        }

        void OnEditTimeSignature(object sender, PointerPressedEventArgs args) {
            var project = DocManager.Inst.Project;
            var timeSig = project.timeSignatures[0];
            var dialog = new TimeSignatureDialog(timeSig.beatPerBar, timeSig.beatUnit);
            dialog.OnOk = (beatPerBar, beatUnit) => {
                viewModel.PlaybackViewModel.SetTimeSignature(beatPerBar, beatUnit);
            };
            _ = ShowDialog(dialog);
            args.Pointer.Capture(null);
        }

        void OnEditBpm(object sender, PointerPressedEventArgs args) {
            var project = DocManager.Inst.Project;
            var dialog = new TypeInDialog();
            dialog.SetTitle("BPM");
            dialog.SetText(project.tempos[0].bpm.ToString());
            dialog.ViewModel.OnFinish = s => {
                if (double.TryParse(s, out double bpm)) {
                    viewModel.PlaybackViewModel.SetBpm(bpm);
                }
            };
            _ = ShowDialog(dialog);
            args.Pointer.Capture(null);
        }

        void OnPlayOrPause(object sender, RoutedEventArgs args) {
            viewModel.PlaybackViewModel.PlayOrPause();
        }

        void OnMenuNew(object sender, RoutedEventArgs args) => NewProject();
        async void NewProject() {
            if (!DocManager.Inst.ChangesSaved && !await AskIfSaveAndContinue()) {
                return;
            }
            viewModel.NewProject();
            viewModel.Page = 1;
        }

        void OnMenuOpen(object sender, RoutedEventArgs args) => Open();
        async void Open() {
            if (!DocManager.Inst.ChangesSaved && !await AskIfSaveAndContinue()) {
                return;
            }
            try {
                var picked = await FsAccessService.OpenFilePickerAsync(
                    "Project Files",
                    ".ustx,.ust,.vsqx,.mid,.midi,.ufdata,.musicxml",
                    multiple: false);
                if (picked.Length == 0) return;

                var files = picked.Select(p => p.TempPath).ToArray();
                await Task.Run(() => viewModel.OpenProject(files));
                viewModel.Page = 1;

                // Clean up temp files after a delay to allow reading to complete
                _ = Task.Run(async () => {
                    await Task.Delay(2000);
                    try { await FsAccessService.CleanupPickerTempAsync(); } catch { }
                });
            } catch (Exception e) {
                Log.Error(e, "Failed to open files");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void OnMainMenuOpened(object sender, RoutedEventArgs args) {
            viewModel.RefreshOpenRecent();
            Task.Run(() => {
                var fs = FileSystemManager.Inst.FS;
                var templatesPath = PathManager.Inst.TemplatesPath;
                if (!fs.DirectoryExists(templatesPath)) {
                    fs.CreateDirectory(templatesPath);
                }
                return fs.GetFiles(templatesPath, "*.ustx");
            }).ContinueWith(t => {
                if (t.IsCompletedSuccessfully) {
                    viewModel.RefreshTemplatesFromFiles(t.Result);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
            viewModel.RefreshCacheSize();
        }

        void OnMainMenuClosed(object sender, RoutedEventArgs args) { Focus(); }
        void OnMainMenuPointerLeave(object sender, PointerEventArgs args) { Focus(); }

        void OnMenuOpenProjectLocation(object sender, RoutedEventArgs args) {
            // Cannot open folder in browser - no-op
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, "Open project location is not available in the browser."));
        }
        async void OnMenuSave(object sender, RoutedEventArgs args) => await Save();
        async void OnMenuSaveAs(object sender, RoutedEventArgs args) => await SaveAs();
        async Task SaveAs() {
            try {
                // Save to a temp OPFS path first
                var project = DocManager.Inst.Project;
                var suggestedName = string.IsNullOrEmpty(project.FilePath)
                    ? "project.ustx"
                    : Path.GetFileName(project.FilePath);
                if (!suggestedName.EndsWith(".ustx")) suggestedName = "project.ustx";

                var tempPath = Path.Combine(PathManager.Inst.CachePath, "export_" + suggestedName);
                // Ustx.Save does filesystem I/O (OPFS) which uses .Result on JS interop.
                // Must run on a worker thread to avoid deadlocking the UI/JS thread.
                await Task.Run(() => Ustx.Save(tempPath, project));

                // Now push the saved file to the user via save picker
                var result = await FsAccessService.SaveFilePickerFromOpfsAsync(
                    tempPath, suggestedName, "USTX Project", ".ustx");

                if (!string.IsNullOrEmpty(result)) {
                    string message = ThemeManager.GetString("progress.saved");
                    message = string.Format(message, DateTime.Now);
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, message));
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to save project");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void OnMenuSaveTemplate(object sender, RoutedEventArgs args) {
            var project = DocManager.Inst.Project;
            var dialog = new TypeInDialog { DataContext = new TypeInDialogViewModel { Title = ThemeManager.GetString("menu.file.savetemplate") } };
            dialog.SetText("default");
            dialog.ViewModel.OnFinish = file => {
                if (string.IsNullOrEmpty(file)) return;
                file = Path.GetFileNameWithoutExtension(file);
                file = $"{file}.ustx";
                file = Path.Combine(PathManager.Inst.TemplatesPath, file);
                // Ustx.Save does filesystem I/O (OPFS) which uses .Result on JS interop.
                // Must run on a worker thread.
                _ = Task.Run(() => Ustx.Save(file, project.CloneAsTemplate()));
            };
            _ = ShowDialog(dialog);
        }

        async void OnMenuImportTracks(object sender, RoutedEventArgs args) {
            try {
                var picked = await FsAccessService.OpenFilePickerAsync(
                    "Project Files",
                    ".ustx,.ust,.vsqx,.mid,.midi,.ufdata,.musicxml",
                    multiple: true);
                if (picked.Length == 0) return;

                var files = picked.Select(p => p.TempPath).ToArray();
                // ReadProjects does filesystem I/O which uses .Result on JS interop.
                // Must run on a worker thread to avoid deadlocking the UI/JS thread.
                var loadedProjects = await Task.Run(() => Formats.ReadProjects(files));
                if (loadedProjects == null || loadedProjects.Length == 0) return;

                // In browser we skip the tempo import dialog and default to importing
                // tempo only if the current project has no parts
                bool importTempo = DocManager.Inst.Project.parts.Count == 0;
                viewModel.ImportTracks(loadedProjects, importTempo);

                _ = Task.Run(async () => {
                    await Task.Delay(2000);
                    try { await FsAccessService.CleanupPickerTempAsync(); } catch { }
                });
            } catch (Exception e) {
                Log.Error(e, "Failed to import tracks");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
        async void OnMenuImportAudio(object sender, RoutedEventArgs args) {
            try {
                var picked = await FsAccessService.OpenFilePickerAsync(
                    "Audio Files",
                    ".wav,.mp3,.ogg,.opus,.flac",
                    multiple: true);
                if (picked.Length == 0) return;

                foreach (var file in picked) {
                    try {
                        // ImportAudio loads audio data via IFileSystem which uses .Result
                        // on JS interop. Must run on a worker thread.
                        await Task.Run(() => viewModel.ImportAudio(file.TempPath));
                    } catch (Exception e) {
                        Log.Error(e, "Failed to import audio file {Name}", file.Name);
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                    }
                }

                _ = Task.Run(async () => {
                    await Task.Delay(2000);
                    try { await FsAccessService.CleanupPickerTempAsync(); } catch { }
                });
            } catch (Exception e) {
                Log.Error(e, "Failed to import audio");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
        async void OnMenuExportMixdown(object sender, RoutedEventArgs args) {
            try {
                var project = DocManager.Inst.Project;
                var name = string.IsNullOrEmpty(project.FilePath)
                    ? "mixdown.wav"
                    : Path.GetFileNameWithoutExtension(project.FilePath) + "_mixdown.wav";
                // Use /tmp/ (emscripten in-memory FS) because NAudio's WaveFileWriter
                // uses System.IO.FileStream directly — it can't write to OPFS.
                var tempDir = "/tmp/wav_export";
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, name);
                await PlaybackManager.Inst.RenderMixdown(project, tempPath);

                // Read back from emscripten FS and deliver via save picker
                var bytes = await Task.Run(() => File.ReadAllBytes(tempPath));
                var result = await FsAccessService.SaveFilePickerBytesAsync(
                    name, "WAV Audio", ".wav", bytes);
                if (!string.IsNullOrEmpty(result)) {
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Exported {result}"));
                }
                // Clean up temp file
                try { File.Delete(tempPath); } catch { }
            } catch (Exception e) {
                Log.Error(e, "Failed to export mixdown");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
        async void OnMenuExportWav(object sender, RoutedEventArgs args) {
            try {
                var project = DocManager.Inst.Project;
                var name = string.IsNullOrEmpty(project.FilePath)
                    ? "export.wav"
                    : Path.GetFileNameWithoutExtension(project.FilePath) + ".wav";
                // Use /tmp/ (emscripten FS) because NAudio's WaveFileWriter uses System.IO directly
                var tempDir = "/tmp/wav_export/Export";
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, name);

                await PlaybackManager.Inst.RenderToFiles(project, tempPath);

                // Read WAV files from emscripten FS and trigger downloads
                if (Directory.Exists(tempDir)) {
                    var wavFiles = Directory.GetFiles(tempDir, "*.wav");
                    foreach (var wavFile in wavFiles) {
                        var wavName = Path.GetFileName(wavFile);
                        var bytes = await Task.Run(() => File.ReadAllBytes(wavFile));
                        await FsAccessService.DownloadWavBytesAsync(wavName, bytes);
                    }
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Exported {wavFiles.Length} file(s)"));
                    // Clean up
                    try { foreach (var f in Directory.GetFiles(tempDir)) File.Delete(f); } catch { }
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to export WAV");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
        async void OnMenuExportWavTo(object sender, RoutedEventArgs args) {
            try {
                var project = DocManager.Inst.Project;
                var name = string.IsNullOrEmpty(project.FilePath)
                    ? "export.wav"
                    : Path.GetFileNameWithoutExtension(project.FilePath) + ".wav";
                // Use /tmp/ (emscripten FS) because NAudio's WaveFileWriter uses System.IO directly
                var tempDir = "/tmp/wav_export/ExportTo";
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, name);

                await PlaybackManager.Inst.RenderToFiles(project, tempPath);

                if (Directory.Exists(tempDir)) {
                    var wavFiles = Directory.GetFiles(tempDir, "*.wav");
                    foreach (var wavFile in wavFiles) {
                        var wavName = Path.GetFileName(wavFile);
                        var bytes = await Task.Run(() => File.ReadAllBytes(wavFile));
                        var result = await FsAccessService.SaveFilePickerBytesAsync(
                            wavName, "WAV Audio", ".wav", bytes);
                        if (string.IsNullOrEmpty(result)) break; // User cancelled
                    }
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, "Export complete"));
                    // Clean up
                    try { foreach (var f in Directory.GetFiles(tempDir)) File.Delete(f); } catch { }
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to export WAV");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
        async void OnMenuExportDsTo(object sender, RoutedEventArgs e) {
            await ExportDiffSingerScript(v2: false, exportPitch: true);
        }
        async void OnMenuExportDsV2To(object sender, RoutedEventArgs e) {
            await ExportDiffSingerScript(v2: true, exportPitch: true);
        }
        async void OnMenuExportDsV2WithoutPitchTo(object sender, RoutedEventArgs e) {
            await ExportDiffSingerScript(v2: true, exportPitch: false);
        }
        async Task ExportDiffSingerScript(bool v2, bool exportPitch) {
            try {
                var project = DocManager.Inst.Project;
                // Use /tmp/ (emscripten FS) because DiffSingerScript.SavePart uses
                // raw File.WriteAllText which writes to emscripten in-memory FS.
                var tempDir = "/tmp/ds_export";
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                for (var i = 0; i < project.parts.Count; i++) {
                    var part = project.parts[i];
                    if (part is UVoicePart voicePart) {
                        var partName = voicePart.DisplayName;
                        var tempPath = Path.Combine(tempDir, $"export_{partName}_{i}.ds");
                        await Task.Run(() => DiffSingerScript.SavePart(project, voicePart, tempPath, v2, exportPitch));

                        var suggestedName = $"{partName}.ds";
                        var bytes = await Task.Run(() => File.ReadAllBytes(tempPath));
                        var result = await FsAccessService.SaveFilePickerBytesAsync(
                            suggestedName, "DiffSinger Script", ".ds", bytes);
                        if (string.IsNullOrEmpty(result)) break; // User cancelled
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Exported {result}"));
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to export DiffSinger script");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }
        async void OnMenuExportUst(object sender, RoutedEventArgs e) {
            try {
                var project = DocManager.Inst.Project;
                for (var i = 0; i < project.parts.Count; i++) {
                    var part = project.parts[i];
                    if (part is UVoicePart voicePart) {
                        var partName = voicePart.DisplayName;
                        var tempPath = Path.Combine(PathManager.Inst.CachePath, $"export_{partName}_{i}.ust");
                        // Ust.SavePart does filesystem I/O via IFileSystem. Must run on worker thread.
                        await Task.Run(() => Ust.SavePart(project, voicePart, tempPath));

                        var suggestedName = $"{partName}.ust";
                        await FsAccessService.DownloadFileAsync(tempPath, suggestedName);
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Exported {suggestedName}"));
                    }
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to export UST");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }
        async void OnMenuExportUstTo(object sender, RoutedEventArgs e) {
            try {
                var project = DocManager.Inst.Project;
                for (var i = 0; i < project.parts.Count; i++) {
                    var part = project.parts[i];
                    if (part is UVoicePart voicePart) {
                        var partName = voicePart.DisplayName;
                        var tempPath = Path.Combine(PathManager.Inst.CachePath, $"export_{partName}_{i}.ust");
                        // Ust.SavePart does filesystem I/O via IFileSystem. Must run on worker thread.
                        await Task.Run(() => Ust.SavePart(project, voicePart, tempPath));

                        var suggestedName = $"{partName}.ust";
                        var result = await FsAccessService.SaveFilePickerFromOpfsAsync(
                            tempPath, suggestedName, "UST File", ".ust");
                        if (string.IsNullOrEmpty(result)) break;
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Exported {result}"));
                    }
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to export UST");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }
        async void OnMenuExportMidi(object sender, RoutedEventArgs e) {
            try {
                var project = DocManager.Inst.Project;
                var name = string.IsNullOrEmpty(project.FilePath)
                    ? "export.mid"
                    : Path.GetFileNameWithoutExtension(project.FilePath) + ".mid";
                var tempPath = Path.Combine(PathManager.Inst.CachePath, name);
                // MidiWriter.Save does filesystem I/O via IFileSystem. Must run on worker thread.
                await Task.Run(() => MidiWriter.Save(tempPath, project));

                var result = await FsAccessService.SaveFilePickerFromOpfsAsync(
                    tempPath, name, "MIDI File", ".mid,.midi");
                if (!string.IsNullOrEmpty(result)) {
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Exported {result}"));
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to export MIDI");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void OnMenuUndo(object sender, RoutedEventArgs args) => viewModel.Undo();
        void OnMenuRedo(object sender, RoutedEventArgs args) => viewModel.Redo();

        void OnMenuExpressionss(object sender, RoutedEventArgs args) {
            var dialog = new ExpressionsDialog { DataContext = new ExpressionsViewModel() };
            _ = ShowDialog(dialog);
        }

        void OnMenuRemapTimeaxis(object sender, RoutedEventArgs e) {
            var project = DocManager.Inst.Project;
            var dialog = new TypeInDialog {
                DataContext = new TypeInDialogViewModel { 
                    Title = ThemeManager.GetString("menu.project.remaptimeaxis"),
                    Prompt = ThemeManager.GetString("dialogs.remaptimeaxis.message")
                }
            };
            dialog.SetText(project.tempos[0].bpm.ToString());
            dialog.ViewModel.OnFinish = s => {
                try {
                    if (double.TryParse(s, out double bpm)) {
                        DocManager.Inst.StartUndoGroup("command.project.tempo");
                        var oldTimeAxis = project.timeAxis.Clone();
                        DocManager.Inst.ExecuteCmd(new BpmCommand(project, bpm));
                        foreach (var tempo in project.tempos.Skip(1)) {
                            DocManager.Inst.ExecuteCmd(new DelTempoChangeCommand(project, tempo.position));
                        }
                        viewModel.RemapTimeAxis(oldTimeAxis, project.timeAxis.Clone());
                        DocManager.Inst.EndUndoGroup();
                    }
                } catch (Exception ex) {
                    Log.Error(ex, "Failed to remap time axis");
                }
            };
            _ = ShowDialog(dialog);
        }

        async void OnMenuSingers(object sender, RoutedEventArgs args) { await OpenSingersWindowAsync(); }
        async void OnMenuInstallSinger(object sender, RoutedEventArgs args) {
            try {
                Log.Information("OnMenuInstallSinger: showing directory picker...");
                // The mount base path is where the singer directory will appear in the virtual FS.
                // We mount under the standard singers path so the existing search finds it.
                var singersPath = PathManager.Inst.SingersPath; // "/openutau/Singers"
                var mountPath = await FsAccessService.PickSingerDirectoryAsync(singersPath);
                if (string.IsNullOrEmpty(mountPath)) {
                    Log.Information("OnMenuInstallSinger: user cancelled directory picker");
                    return;
                }
                Log.Information("OnMenuInstallSinger: mounted singer at {Path}", mountPath);

                // Register the mount with BrowserFileSystem so file operations route correctly
                if (FileSystemManager.Inst.FS is Browser.BrowserFileSystem browserFs) {
                    browserFs.AddFsAccessMount(mountPath);
                }

                // Re-scan all singers on a background thread
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, "Loading singer..."));
                await Task.Run(() => {
                    SingerManager.Inst.SearchAllSingers();
                });
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Singer loaded from {mountPath}"));
                DocManager.Inst.ExecuteCmd(new OtoChangedNotification(external: true));
                Log.Information("OnMenuInstallSinger: singer scan complete");
            } catch (Exception e) {
                Log.Error(e, "OnMenuInstallSinger failed");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void OnMenuPackageManager(object sender, RoutedEventArgs args) {
            // TODO: Create a browser-compatible package manager dialog.
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0,
                "Package manager is not yet available in the browser."));
        }

        void OnMenuInstallWavtoolResampler(object sender, RoutedEventArgs args) {
            // Wavtool/resampler installation requires native executables, not available in browser
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, "Wavtool/resampler installation is not available in the browser."));
        }

        void OnMenuPreferences(object sender, RoutedEventArgs args) {
            PreferencesViewModel dataContext;
            try { dataContext = new PreferencesViewModel(); }
            catch (Exception e) {
                Log.Error(e, "Failed to load prefs");
                Preferences.Reset();
                dataContext = new PreferencesViewModel();
            }
            var dialog = new PreferencesDialog { DataContext = dataContext };
            _ = ShowDialog(dialog);
        }

        void OnMenuFullScreen(object sender, RoutedEventArgs args) {
            try { FsAccessService.ToggleFullScreen(); }
            catch (Exception e) { Log.Error(e, "Failed to toggle fullscreen"); }
        }
        void OnMenuClearCache(object sender, RoutedEventArgs args) {
            Task.Run(() => {
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ThemeManager.GetString("progress.clearingcache")));
                PathManager.Inst.ClearCache();
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ThemeManager.GetString("progress.cachecleared")));
            });
        }
        void OnMenuDebugWindow(object sender, RoutedEventArgs args) {
            // Debug window is not available in browser
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, "Debug window is not available in the browser."));
        }
        void OnMenuPhoneticAssistant(object sender, RoutedEventArgs args) {
            // Phonetic assistant is not available in browser (requires separate window)
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, "Phonetic assistant is not available in the browser."));
        }
        void OnMenuCheckUpdate(object sender, RoutedEventArgs args) {
            var dialog = new UpdaterDialog();
            _ = ShowDialog(dialog);
        }
        void OnMenuLogsLocation(object sender, RoutedEventArgs args) {
            // Logs are in OPFS - cannot open in file explorer from browser
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, "Logs location is not accessible in the browser. Check browser console (F12) for logs."));
        }
        void OnMenuReportIssue(object sender, RoutedEventArgs args) {
            try {
                FsAccessService.OpenUrl("https://github.com/stakira/OpenUtau/issues");
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
        void OnMenuWiki(object sender, RoutedEventArgs args) {
            try {
                FsAccessService.OpenUrl("https://github.com/stakira/OpenUtau/wiki/Getting-Started");
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void OnMenuLayoutReset(object sender, RoutedEventArgs args) {
            // Layout management is not applicable in browser (single-window mode)
        }
        void OnMenuLayoutVSplit11(object sender, RoutedEventArgs args) { }
        void OnMenuLayoutVSplit12(object sender, RoutedEventArgs args) { }
        void OnMenuLayoutVSplit13(object sender, RoutedEventArgs args) { }
        void OnMenuLayoutHSplit11(object sender, RoutedEventArgs args) { }
        void OnMenuLayoutHSplit12(object sender, RoutedEventArgs args) { }
        void OnMenuLayoutHSplit13(object sender, RoutedEventArgs args) { }

        void OnKeyDown(object sender, KeyEventArgs args) {
            if (PianoRollContainer?.IsKeyboardFocusWithin == true) {
                args.Handled = false;
                return;
            }
            var tracksVm = viewModel.TracksViewModel;
            if (args.KeyModifiers == KeyModifiers.None) {
                args.Handled = true;
                switch (args.Key) {
                    case Key.Delete: viewModel.TracksViewModel.DeleteSelectedParts(); break;
                    case Key.Space: viewModel.PlaybackViewModel.PlayOrPause(); break;
                    case Key.Home: viewModel.PlaybackViewModel.MovePlayPos(0); break;
                    case Key.End:
                        if (viewModel.TracksViewModel.Parts.Count > 0) {
                            int endTick = viewModel.TracksViewModel.Parts.Max(part => part.End);
                            viewModel.PlaybackViewModel.MovePlayPos(endTick);
                        }
                        break;
                    default: args.Handled = false; break;
                }
            } else if (args.KeyModifiers == cmdKey) {
                args.Handled = true;
                switch (args.Key) {
                    case Key.A: viewModel.TracksViewModel.SelectAllParts(); break;
                    case Key.N: NewProject(); break;
                    case Key.O: Open(); break;
                    case Key.S: _ = Save(); break;
                    case Key.Z: viewModel.Undo(); break;
                    case Key.Y: viewModel.Redo(); break;
                    case Key.C: tracksVm.CopyParts(); break;
                    case Key.X: tracksVm.CutParts(); break;
                    case Key.V: tracksVm.PasteParts(); break;
                    default: args.Handled = false; break;
                }
            } else if (args.KeyModifiers == KeyModifiers.Shift) {
                args.Handled = true;
                switch (args.Key) {
                    case Key.S:
                        if (viewModel.TracksViewModel.SelectedParts.Count > 0) {
                            var part = viewModel.TracksViewModel.SelectedParts.First();
                            var track = DocManager.Inst.Project.tracks[part.trackNo];
                            MessageBus.Current.SendMessage(new TracksSoloEvent(part.trackNo, !track.Solo, false));
                        }
                        break;
                    case Key.M:
                        if (viewModel.TracksViewModel.SelectedParts.Count > 0) {
                            var part = viewModel.TracksViewModel.SelectedParts.First();
                            MessageBus.Current.SendMessage(new TracksMuteEvent(part.trackNo, false));
                        }
                        break;
                    default: args.Handled = false; break;
                }
            } else if (args.KeyModifiers == (cmdKey | KeyModifiers.Shift)) {
                args.Handled = true;
                switch (args.Key) {
                    case Key.Z: viewModel.Redo(); break;
                    case Key.S: _ = SaveAs(); break;
                    default: args.Handled = false; break;
                }
            }
        }

        void OnPointerPressed(object? sender, PointerPressedEventArgs args) {
            if (!PianoRollContainer?.IsPointerOver == true && !args.Handled && args.ClickCount == 1) {
                this.Focus();
            }
        }

        async void OnDrop(object? sender, DragEventArgs args) {
            // In browser, drag-and-drop from OS file manager has limited support.
            // Avalonia Browser backend may pass through file data in some cases.
            try {
                var files = args.Data?.GetFiles()?
                    .Where(i => i != null)
                    .Select(i => i.Path.LocalPath)
                    .ToArray() ?? Array.Empty<string>();
                if (files.Length == 0) {
                    return;
                }

                string[] ProjectExts = { ".ustx", ".ust", ".vsqx", ".ufdata", ".musicxml", ".mid", ".midi" };
                string[] AudioExts = { ".mp3", ".wav", ".ogg", ".flac" };

                var projectFiles = files.Where(f => ProjectExts.Contains(Path.GetExtension(f).ToLower())).ToArray();
                var audioFiles = files.Where(f => AudioExts.Contains(Path.GetExtension(f).ToLower())).ToArray();

                if (projectFiles.Length == 0 && audioFiles.Length == 0) {
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0,
                        "Unsupported file type. Use File > Open or File > Import instead."));
                    return;
                }

                viewModel.Page = 1;

                if (projectFiles.Length > 0) {
                    try {
                        // ReadProjects does filesystem I/O which may use .Result on JS interop.
                        // Must run on a worker thread to avoid deadlocking the UI/JS thread.
                        var loadedProjects = await Task.Run(() => Formats.ReadProjects(projectFiles));
                        bool importTempo = DocManager.Inst.Project.parts.Count == 0;
                        viewModel.ImportTracks(loadedProjects, importTempo);
                    } catch (Exception e) {
                        Log.Error(e, "Failed to import dropped project files");
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                    }
                }

                foreach (var audioFile in audioFiles) {
                    try {
                        // ImportAudio loads audio data via IFileSystem. Must run on worker thread.
                        await Task.Run(() => viewModel.ImportAudio(audioFile));
                    } catch (Exception e) {
                        Log.Error(e, "Failed to import dropped audio file");
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                    }
                }
            } catch (Exception e) {
                Log.Error(e, "OnDrop failed");
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0,
                    "Drag and drop may not be fully supported in the browser. Use File > Open instead."));
            }
        }

        public void HScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            if (sender is ScrollBar scrollbar) {
                scrollbar.Value = Math.Max(scrollbar.Minimum, Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * args.Delta.Y));
            }
        }

        public void VScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            if (sender is ScrollBar scrollbar) {
                scrollbar.Value = Math.Max(scrollbar.Minimum, Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * args.Delta.Y));
            }
        }

        public void TimelinePointerWheelChanged(object sender, PointerWheelEventArgs args) {
            if (sender is Control control) {
                var position = args.GetCurrentPoint((Visual)sender).Position;
                var size = control.Bounds.Size;
                position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
                viewModel.TracksViewModel.OnXZoomed(position, 0.1 * args.Delta.Y);
            }
        }

        public void ViewScalerPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            viewModel.TracksViewModel.OnYZoomed(new Point(0, 0.5), 0.1 * args.Delta.Y);
        }

        public void TimelinePointerPressed(object sender, PointerPressedEventArgs args) {
            if (sender is Control control) {
                var point = args.GetCurrentPoint(control);
                if (point.Properties.IsLeftButtonPressed) {
                    args.Pointer.Capture(control);
                    viewModel.TracksViewModel.PointToLineTick(point.Position, out int left, out int right);
                    viewModel.PlaybackViewModel.MovePlayPos(left);
                } else if (point.Properties.IsRightButtonPressed) {
                    int tick = viewModel.TracksViewModel.PointToTick(point.Position);
                    viewModel.RefreshTimelineContextMenu(tick);
                }
            }
        }

        public void TimelinePointerMoved(object sender, PointerEventArgs args) {
            if (sender is Control control) {
                var point = args.GetCurrentPoint(control);
                if (point.Properties.IsLeftButtonPressed) {
                    viewModel.TracksViewModel.PointToLineTick(point.Position, out int left, out int right);
                    viewModel.PlaybackViewModel.MovePlayPos(left);
                }
            }
        }

        public void TimelinePointerReleased(object sender, PointerReleasedEventArgs args) {
            args.Pointer.Capture(null);
        }

        public void PartsCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            if (sender is Control control) {
                var point = args.GetCurrentPoint(control);
                var hitControl = control.InputHitTest(point.Position);
                if (partEditState != null) return;

                if (point.Properties.IsLeftButtonPressed) {
                    if (args.KeyModifiers == cmdKey) {
                        partEditState = new PartSelectionEditState(control, viewModel, SelectionBox);
                    } else if (hitControl == control) {
                        viewModel.TracksViewModel.DeselectParts();
                        var part = viewModel.TracksViewModel.MaybeAddPart(point.Position);
                        if (part != null) {
                            partEditState = new PartMoveEditState(control, viewModel, part);
                        }
                    } else if (hitControl is PartControl partControl) {
                        bool isVoice = partControl.part is UVoicePart;
                        if (isVoice) {
                            bool trim = point.Position.X > partControl.Bounds.Right - ViewConstants.ResizeMargin;
                            bool skip = point.Position.X < partControl.Bounds.Left + ViewConstants.ResizeMargin;
                            if (trim || skip) {
                                partEditState = new PartResizeEditState(control, viewModel, partControl.part, skip);
                            } else {
                                partEditState = new PartMoveEditState(control, viewModel, partControl.part);
                            }
                        }
                    }
                } else if (point.Properties.IsRightButtonPressed) {
                    if (hitControl is PartControl partControl) {
                        if (!viewModel.TracksViewModel.SelectedParts.Contains(partControl.part)) {
                            viewModel.TracksViewModel.DeselectParts();
                            viewModel.TracksViewModel.SelectPart(partControl.part);
                        }
                        if (viewModel.TracksViewModel.SelectedParts.Count > 0) {
                            shouldOpenPartsContextMenu = true;
                        }
                    } else {
                        viewModel.TracksViewModel.DeselectParts();
                    }
                } else if (point.Properties.IsMiddleButtonPressed) {
                    partEditState = new PartPanningState(control, viewModel);
                }

                if (partEditState != null) {
                    partEditState.Begin(point.Pointer, point.Position);
                    partEditState.Update(point.Pointer, point.Position);
                }
            }
        }

        public void PartsCanvasPointerMoved(object sender, PointerEventArgs args) {
            if (sender is Control control) {
                var point = args.GetCurrentPoint(control);
                if (partEditState != null) {
                    partEditState.Update(point.Pointer, point.Position);
                }
            }
        }

        public void PartsCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (partEditState != null) {
                if (sender is Control control) {
                    var point = args.GetCurrentPoint(control);
                    partEditState.Update(point.Pointer, point.Position);
                    partEditState.End(point.Pointer, point.Position);
                }
                partEditState = null;
            }
        }

        public async void PartsCanvasDoubleTapped(object sender, TappedEventArgs args) {
            if (sender is Canvas canvas) {
                var control = canvas.InputHitTest(args.GetPosition(canvas));
                if (control is PartControl partControl && partControl.part is UVoicePart) {
                    var model = await Task.Run<PianoRollViewModel>(() => new PianoRollViewModel());
                    pianoRoll = new PianoRoll(model) { MainWindow = this };
                    viewModel!.ShowPianoRoll = true;
                    PianoRollContainer.Content = pianoRoll;
                    await Task.Run(() => pianoRoll.InitializePianoRollWindowAsync());
                    pianoRoll.ViewModel.PlaybackViewModel = viewModel.PlaybackViewModel;
                    viewModel.ShowPianoRoll = true;
                    int tick = viewModel.TracksViewModel.PointToTick(args.GetPosition(canvas));
                    DocManager.Inst.ExecuteCmd(new LoadPartNotification(partControl.part, DocManager.Inst.Project, tick));
                    pianoRoll.AttachExpressions();
                }
            }
        }

        public void MainPagePointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var delta = args.Delta;
            if (args.KeyModifiers == KeyModifiers.None || args.KeyModifiers == KeyModifiers.Shift) {
                if (args.KeyModifiers == KeyModifiers.Shift) delta = new Vector(delta.Y, delta.X);
                if (delta.X != 0 && HScrollBar != null) {
                    HScrollBar.Value = Math.Max(HScrollBar.Minimum, Math.Min(HScrollBar.Maximum, HScrollBar.Value - HScrollBar.SmallChange * delta.X));
                }
                if (delta.Y != 0 && VScrollBar != null) {
                    VScrollBar.Value = Math.Max(VScrollBar.Minimum, Math.Min(VScrollBar.Maximum, VScrollBar.Value - VScrollBar.SmallChange * delta.Y));
                }
            } else if (args.KeyModifiers == KeyModifiers.Alt) {
                ViewScalerPointerWheelChanged(VScaler, args);
            } else if (args.KeyModifiers == cmdKey) {
                TimelinePointerWheelChanged(TimelineCanvas, args);
            }
            if (partEditState != null && sender is Control c) {
                var point = args.GetCurrentPoint(c);
                partEditState.Update(point.Pointer, point.Position);
            }
        }

        public void PartsContextMenuOpening(object sender, CancelEventArgs args) {
            if (!shouldOpenPartsContextMenu) args.Cancel = true;
            shouldOpenPartsContextMenu = false;
        }

        public void PartsContextMenuClosing(object sender, CancelEventArgs args) {
            if (PartsContextMenu != null) PartsContextMenu.DataContext = null;
        }

        void RenamePart(UPart part) {
            var dialog = new TypeInDialog {
                DataContext = new TypeInDialogViewModel { 
                    Title = ThemeManager.GetString("context.part.rename"),
                    Text = part.name
                }
            };
            dialog.ViewModel.OnFinish = name => {
                if (!string.IsNullOrWhiteSpace(name) && name != part.name) {
                    DocManager.Inst.StartUndoGroup("command.part.edit");
                    DocManager.Inst.ExecuteCmd(new RenamePartCommand(DocManager.Inst.Project, part, name));
                    DocManager.Inst.EndUndoGroup();
                }
            };
            _ = ShowDialog(dialog);
        }

        void GotoFile(UPart part) {
            if (part is UWavePart wavePart) {
                // Cannot open file location in browser - show path info instead
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0,
                    $"File location: {wavePart.FilePath} (not accessible in browser)"));
            }
        }

        async void ReplaceAudio(UPart part) {
            try {
                var picked = await FsAccessService.OpenFilePickerAsync(
                    "Audio Files", ".wav,.mp3,.ogg,.opus,.flac", multiple: false);
                if (picked.Length == 0) return;

                var file = picked[0];
                // UWavePart.Load() reads audio data via IFileSystem which may use .Result on JS interop.
                // Must run on a worker thread.
                var newPart = await Task.Run(() => {
                    var wp = new UWavePart() {
                        FilePath = file.TempPath,
                        trackNo = part.trackNo,
                        position = part.position
                    };
                    wp.Load(DocManager.Inst.Project);
                    return wp;
                });
                DocManager.Inst.StartUndoGroup("command.import.audio");
                DocManager.Inst.ExecuteCmd(new ReplacePartCommand(DocManager.Inst.Project, part, newPart));
                DocManager.Inst.EndUndoGroup();

                _ = Task.Run(async () => {
                    await Task.Delay(2000);
                    try { await FsAccessService.CleanupPickerTempAsync(); } catch { }
                });
            } catch (Exception e) {
                Log.Error(e, "Failed to replace audio");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
        void Transcribe(UPart part) {
            // SOME transcription requires ONNX Runtime which is not available in browser WASM
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, "Transcription is not available in the browser."));
        }

        public async void OnWelcomeRecovery(object sender, RoutedEventArgs args) {
            // OpenProject does filesystem I/O via IFileSystem which uses .Result on JS interop.
            // Must run on a worker thread to avoid deadlocking the UI/JS thread.
            await Task.Run(() => viewModel.OpenProject(new[] { viewModel.RecoveryPath }));
            viewModel.Page = 1;
        }

        public async void OnWelcomeRecent(object sender, PointerPressedEventArgs args) {
            if (sender is StackPanel panel && panel.DataContext is RecentFileInfo fileInfo) {
                if (!DocManager.Inst.ChangesSaved && !await AskIfSaveAndContinue()) return;
                // OpenRecent does filesystem I/O. Must run on worker thread.
                await Task.Run(() => viewModel.OpenRecent(fileInfo.PathName));
            }
        }

        public async void OnWelcomeTemplate(object sender, PointerPressedEventArgs args) {
            if (sender is StackPanel panel && panel.DataContext is RecentFileInfo fileInfo) {
                if (!DocManager.Inst.ChangesSaved && !await AskIfSaveAndContinue()) return;
                // OpenTemplate does filesystem I/O. Must run on worker thread.
                await Task.Run(() => viewModel.OpenTemplate(fileInfo.PathName));
            }
        }

        void MergePart(UPart part) {
            var selectedParts = viewModel.TracksViewModel.SelectedParts;
            if (!selectedParts.All(p => p.trackNo.Equals(part.trackNo))) return;
            if (selectedParts.Count() <= 1) return;
            
            var voiceParts = selectedParts.OfType<UVoicePart>().ToList();
            if (voiceParts.Count != selectedParts.Count) return;

            var mergedPart = voiceParts.Aggregate((merging, nextup) => {
                var (leftPart, rightPart) = merging.position < nextup.position ? (merging, nextup) : (nextup, merging);
                int newPosition = leftPart.position;
                int deltaPos = rightPart.position - leftPart.position;
                var shiftPart = new UVoicePart();
                foreach (var note in rightPart.notes) {
                    var shiftNote = note.Clone();
                    shiftNote.position += deltaPos;
                    shiftPart.notes.Add(shiftNote);
                }
                foreach (var curve in rightPart.curves) {
                    var shiftCurve = curve.Clone();
                    for (var i = 0; i < shiftCurve.xs.Count; i++) shiftCurve.xs[i] += deltaPos;
                    shiftPart.curves.Add(shiftCurve);
                }
                return new UVoicePart {
                    name = part.name,
                    comment = merging.comment + nextup.comment,
                    trackNo = part.trackNo,
                    position = newPosition,
                    notes = new SortedSet<UNote>(leftPart.notes.Concat(shiftPart.notes)),
                    curves = UCurve.MergeCurves(leftPart.curves, shiftPart.curves),
                    Duration = Math.Max(leftPart.End, rightPart.End) - newPosition,
                };
            });

            DocManager.Inst.StartUndoGroup("command.part.edit");
            for (int i = selectedParts.Count - 1; i >= 0; i--) {
                DocManager.Inst.ExecuteCmd(new RemovePartCommand(DocManager.Inst.Project, selectedParts[i]));
            }
            DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, mergedPart));
            DocManager.Inst.EndUndoGroup();
        }

        private async Task<bool> AskIfSaveAndContinue() {
            try {
                var message = ThemeManager.GetString("dialogs.exitsave.message");
                var result = FsAccessService.ConfirmYesNoCancel(message);
                switch (result) {
                    case "yes":
                        await Save();
                        return true;
                    case "no":
                        return true; // Continue without saving
                    default:
                        return false; // Cancel
                }
            } catch (Exception e) {
                Log.Error(e, "AskIfSaveAndContinue failed");
                return false; // Cancel on error to prevent data loss
            }
        }
    }
}
