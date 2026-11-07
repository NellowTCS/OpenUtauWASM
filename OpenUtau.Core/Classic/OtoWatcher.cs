using System;
using System.IO;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.Classic {
    class OtoWatcher : IDisposable {
        public bool Paused { get; set; }

        private ClassicSinger singer;
        private FileSystemWatcher? watcher;

        public OtoWatcher(ClassicSinger singer, string path) {
            this.singer = singer;
            if (OS.IsBrowser()) {
                // FileSystemWatcher is not supported in the browser environment.
                // Singer reloading will need to be triggered manually.
                // There are experimental watchers in the File System Access API, 
                // but those are really new afaik and not baseline
                // TODO: look into a better approach
                
                return;
            }
            watcher = new FileSystemWatcher(path);
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.Error += OnError;
            watcher.Filter = "oto.ini";
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e) {
            if (Paused) {
                return;
            }
            Log.Information($"File \"{e.FullPath}\" {e.ChangeType}");
            SingerManager.Inst.ScheduleReload(singer);
        }

        private void OnError(object sender, ErrorEventArgs e) {
            Log.Error($"Watcher error {e}");
        }

        public void Dispose() {
            watcher?.Dispose();
        }
    }
}
