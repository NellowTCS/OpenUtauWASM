using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {

    public class PathManager {
        private static readonly Lazy<PathManager> inst = new Lazy<PathManager>(() => new PathManager());
        public static PathManager Inst => inst.Value;
        public PathManager() {
            try {
                var assembly = Assembly.GetEntryAssembly();
                RootPath = assembly != null ? Path.GetDirectoryName(assembly.Location) ?? "" : "";
            } catch {
                RootPath = "";
            }
            if (OS.IsBrowser()) {
                DataPath = "/openutau";
                CachePath = "/openutau/cache";
                RootPath = "/openutau";
                HomePathIsAscii = true;
            } else if (OS.IsMacOS()) {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                DataPath = Path.Combine(userHome, "Library", "OpenUtau");
                CachePath = Path.Combine(userHome, "Library", "Caches", "OpenUtau");
                HomePathIsAscii = true;
                try {
                    // Deletes old cache.
                    string oldCache = Path.Combine(DataPath, "Cache");
                    if (Directory.Exists(oldCache)) {
                        Directory.Delete(oldCache, true);
                    }
                } catch { }
            } else if (OS.IsLinux()) {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrEmpty(dataHome)) {
                    dataHome = Path.Combine(userHome, ".local", "share");
                }
                DataPath = Path.Combine(dataHome, "OpenUtau");
                string cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(cacheHome)) {
                    cacheHome = Path.Combine(userHome, ".cache");
                }
                CachePath = Path.Combine(cacheHome, "OpenUtau");
                HomePathIsAscii = true;
            } else {
                string exePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                IsInstalled = File.Exists(Path.Combine(exePath, "installed.txt"));
                if (!IsInstalled) {
                    DataPath = exePath;
                } else {
                    string dataHome = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                    DataPath = Path.Combine(dataHome, "OpenUtau");
                }
                CachePath = Path.Combine(DataPath, "Cache");
                HomePathIsAscii = true;
                var etor = StringInfo.GetTextElementEnumerator(DataPath);
                while (etor.MoveNext()) {
                    string s = etor.GetTextElement();
                    if (s.Length != 1 || s[0] >= 128) {
                        HomePathIsAscii = false;
                        break;
                    }
                }
            }
        }

        public string RootPath { get; private set; }
        public string DataPath { get; private set; }
        public string CachePath { get; private set; }
        public bool HomePathIsAscii { get; private set; }
        public bool IsInstalled { get; private set; }
        public string SingersPathOld => Path.Combine(DataPath, "Content", "Singers");
        public string SingersPath => Path.Combine(DataPath, "Singers");
        public string AdditionalSingersPath => Preferences.Default.AdditionalSingerPath;
        public string SingersInstallPath => Preferences.Default.InstallToAdditionalSingersPath
            && !string.IsNullOrEmpty(Preferences.Default.AdditionalSingerPath)
                ? AdditionalSingersPath
                : SingersPath;
        public string ResamplersPath => Path.Combine(DataPath, "Resamplers");
        public string WavtoolsPath => Path.Combine(DataPath, "Wavtools");
        public string DependencyPath => Path.Combine(DataPath, "Dependencies");
        public string PluginsPath => Path.Combine(DataPath, "Plugins");
        public string DictionariesPath => Path.Combine(DataPath, "Dictionaries");
        public string TemplatesPath => Path.Combine(DataPath, "Templates");
        public string LogsPath => Path.Combine(DataPath, "Logs");
        public string LogFilePath => Path.Combine(DataPath, "Logs", "log.txt");
        public string PrefsFilePath => Path.Combine(DataPath, "prefs.json");
        public string ThemesPath => Path.Combine(DataPath, "Themes");
        public string NotePresetsFilePath => Path.Combine(DataPath, "notepresets.json");
        public string BackupsPath => Path.Combine(DataPath, "Backups");

        private readonly List<string> additionalSingersPaths = new List<string>();

        public void AddSingersPath(string path) {
            if (!string.IsNullOrEmpty(path) && !additionalSingersPaths.Contains(path)) {
                additionalSingersPaths.Add(path);
            }
        }

        public List<string> SingersPaths {
            get {
                var fs = FileSystemManager.Inst.FS;
                var list = new List<string> { SingersPath };
                if (fs.DirectoryExists(SingersPathOld)) {
                    list.Add(SingersPathOld);
                }
                if (fs.DirectoryExists(AdditionalSingersPath)) {
                    list.Add(AdditionalSingersPath);
                }
                foreach (var path in additionalSingersPaths) {
                    list.Add(path);
                }
                return list.Distinct().ToList();
            }
        }

        Regex invalid = new Regex("[\\x00-\\x1f<>:\"/\\\\|?*]|^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9]|CLOCK\\$)(\\.|$)|[\\.]$", RegexOptions.IgnoreCase);

        public string GetPartSavePath(string exportPath, string partName, int partNo) {
            var dir = Path.GetDirectoryName(exportPath);
            var fs = FileSystemManager.Inst.FS;
            fs.CreateDirectory(dir);
            var filename = Path.GetFileNameWithoutExtension(exportPath);
            var name = invalid.Replace(partName, "_");
            if (DocManager.Inst.Project.parts.FindAll(p => p is UVoicePart).Count(p => p.DisplayName == partName) > 1) {
                name += $"_{partNo:D2}";
            }
            return Path.Combine(dir, $"{filename}_{name}.ust");
        }

        public string GetExportPath(string exportPath, UTrack track) {
            var dir = Path.GetDirectoryName(exportPath);
            var fs = FileSystemManager.Inst.FS;
            fs.CreateDirectory(dir);
            var filename = Path.GetFileNameWithoutExtension(exportPath);
            var trackName = invalid.Replace(track.TrackName, "_");
            if (DocManager.Inst.Project.tracks.Count(t => t.TrackName == track.TrackName) > 1) {
                trackName += $"_{track.TrackNo:D2}";
            }
            return Path.Combine(dir, $"{filename}_{trackName}.wav");
        }

        public void ClearCache() {
            var fs = FileSystemManager.Inst.FS;
            var files = fs.GetFiles(CachePath);
            foreach (var file in files) {
                try {
                    fs.FileDelete(file);
                } catch (Exception e) {
                    Log.Error(e, $"Failed to delete file {file}");
                }
            }
            var dirs = fs.GetDirectories(CachePath);
            foreach (var dir in dirs) {
                try {
                    fs.DeleteDirectory(dir, true);
                } catch (Exception e) {
                    Log.Error(e, $"Failed to delete dir {dir}");
                }
            }
        }

        readonly static string[] sizes = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
        public string GetCacheSize() {
            var fs = FileSystemManager.Inst.FS;
            if (!fs.DirectoryExists(CachePath)) {
                return "0B";
            }
            double size = fs.EnumerateFiles(CachePath, "*", SearchOption.AllDirectories)
                .Sum(f => {
                    try { return fs.GetFileLength(f); } catch { return 0L; }
                });
            int order = 0;
            while (size >= 1024 && order < sizes.Length - 1) {
                order++;
                size = size / 1024;
            }
            return $"{size:0.##}{sizes[order]}";
        }
    }
}
