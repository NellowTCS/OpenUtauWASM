using System;
using System.Runtime.InteropServices.JavaScript;
using Serilog;

namespace OpenUtau.App.Browser {
    public static partial class RecentPathService {
        private static bool initialized;

        [JSImport("saveRecentPath", "bookmarkHelper")]
        internal static partial void SaveRecentPathImpl(string path, string name);

        [JSImport("getRecentPath", "bookmarkHelper")]
        internal static partial string? GetRecentPathImpl(string name);

        public static async System.Threading.Tasks.Task EnsureInitializedAsync() {
            if (initialized) return;
            try {
                await JSHost.ImportAsync("bookmarkHelper", "../bookmarkHelper.js");
                initialized = true;
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize recent path service");
                throw;
            }
        }

        public static async System.Threading.Tasks.Task SaveRecentPath(string path, string name) {
            try {
                await EnsureInitializedAsync();
                SaveRecentPathImpl(path, name);
                Log.Information("Saved recent path: {Name} = {Path}", name, path);
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
