using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.App.Browser {
    public static partial class RecentPathService {
        private static bool initialized;
        private static readonly SemaphoreSlim initLock = new SemaphoreSlim(1, 1);

        [JSImport("saveRecentPath", "bookmarkHelper")]
        internal static partial void SaveRecentPathImpl(string path, string name);

        [JSImport("getRecentPath", "bookmarkHelper")]
        internal static partial string? GetRecentPathImpl(string name);

        public static async Task EnsureInitializedAsync() {
            if (initialized) return;
            await initLock.WaitAsync();
            try {
                if (initialized) return;
                await JSHost.ImportAsync("bookmarkHelper", "../bookmarkHelper.js");
                initialized = true;
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize recent path service");
                throw;
            } finally {
                initLock.Release();
            }
        }

        public static async System.Threading.Tasks.Task SaveRecentPath(string path, string name) {
            try {
                await EnsureInitializedAsync();
                SaveRecentPathImpl(path, name);
                var sanitizedPath = System.IO.Path.GetFileName(path);
                Log.Information("Saved recent path: {Name} = {Path}", name, sanitizedPath);
            } catch (Exception e) {
                Log.Error(e, "Failed to save recent path: {Name}", name);
                throw;
            }
        }

        public static async System.Threading.Tasks.Task<string?> GetRecentPath(string name) {
            try {
                await EnsureInitializedAsync();
                return GetRecentPathImpl(name);
            } catch (Exception e) {
                Log.Error(e, "Failed to get recent path: {Name}", name);
                throw;
            }
        }
    }
}
