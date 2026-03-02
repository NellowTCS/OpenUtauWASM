using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.App.Browser {
    public static partial class FsAccessService {
        private static bool initialized;

        [JSImport("pickAndMountDirectory", "fsAccessHelper")]
        internal static partial Task<string> PickAndMountDirectoryAsync(string mountBasePath);

        [JSImport("getMountedPaths", "fsAccessHelper")]
        internal static partial string GetMountedPathsJson();

        [JSImport("directoryExists", "fsAccessHelper")]
        internal static partial Task<bool> DirectoryExistsAsync(string path);

        [JSImport("fileExists", "fsAccessHelper")]
        internal static partial Task<bool> FileExistsAsync(string path);

        [JSImport("readFileIntoBuffer", "fsAccessHelper")]
        internal static partial Task<int> ReadFileIntoBufferAsync(string path, byte[] buffer, int offset, int length);

        [JSImport("getFileSize", "fsAccessHelper")]
        internal static partial Task<int> GetFileSizeAsync(string path);

        [JSImport("enumerateFiles", "fsAccessHelper")]
        internal static partial Task<string> EnumerateFilesAsync(string dirPath);

        [JSImport("enumerateFilesWithPattern", "fsAccessHelper")]
        internal static partial Task<string> EnumerateFilesWithPatternAsync(string dirPath, string pattern);

        [JSImport("enumerateFilesRecursive", "fsAccessHelper")]
        internal static partial Task<string> EnumerateFilesRecursiveAsync(string dirPath, string pattern);

        [JSImport("enumerateDirectories", "fsAccessHelper")]
        internal static partial Task<string> EnumerateDirectoriesAsync(string dirPath);

        [JSImport("showOpenFilePicker", "fsAccessHelper")]
        internal static partial Task<string> ShowOpenFilePickerAsync(
            string acceptDescription, string acceptExtensions, bool multiple);

        [JSImport("saveFilePicker", "fsAccessHelper")]
        internal static partial Task<string> SaveFilePickerJSAsync(
            string suggestedName, string acceptDescription, string acceptExtensions, byte[] data);

        [JSImport("saveFileFromOpfs", "fsAccessHelper")]
        internal static partial Task<string> SaveFileFromOpfsAsync(
            string opfsPath, string suggestedName, string acceptDescription, string acceptExtensions);

        [JSImport("downloadFromOpfs", "fsAccessHelper")]
        internal static partial Task<bool> DownloadFromOpfsAsync(string opfsPath, string filename);

        [JSImport("cleanupPickerTemp", "fsAccessHelper")]
        internal static partial Task CleanupPickerTempJSAsync();

        [JSImport("openUrl", "fsAccessHelper")]
        internal static partial void OpenUrlJS(string url);

        [JSImport("confirmDialog", "fsAccessHelper")]
        internal static partial bool ConfirmDialogJS(string message);

        [JSImport("confirmYesNoCancel", "fsAccessHelper")]
        internal static partial string ConfirmYesNoCancelJS(string message);

        [JSImport("toggleFullScreen", "fsAccessHelper")]
        internal static partial void ToggleFullScreenJS();

        public static async Task EnsureInitialized() {
            if (initialized) return;
            try {
                Log.Information("Importing fsAccessHelper module...");
                await JSHost.ImportAsync("fsAccessHelper", "../fsAccessHelper.js");
                initialized = true;
                Log.Information("fsAccessHelper module imported successfully");
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize fsAccessHelper module");
                throw;
            }
        }

        // Show a directory picker and mount the selected directory under the given base path.
        public static async Task<string> PickSingerDirectoryAsync(string mountBasePath) {
            await EnsureInitialized();
            return await PickAndMountDirectoryAsync(mountBasePath);
        }

        // Read a file from the FS Access API into a byte array.
        public static async Task<byte[]?> ReadFileAsync(string path) {
            await EnsureInitialized();
            var size = await GetFileSizeAsync(path);
            if (size < 0) return null;
            if (size == 0) return Array.Empty<byte>();

            var buffer = new byte[size];
            var bytesRead = await ReadFileIntoBufferAsync(path, buffer, 0, size);
            if (bytesRead < 0) return null;
            if (bytesRead < size) {
                var truncated = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, truncated, 0, bytesRead);
                return truncated;
            }
            return buffer;
        }

        // Represents a file selected via the open file picker.
        public class PickedFile {
            public string Name { get; set; } = "";
            public string TempPath { get; set; } = "";
        }

        // Show an open file picker and copy selected files to OPFS temp paths.
        public static async Task<PickedFile[]> OpenFilePickerAsync(
            string description, string extensions, bool multiple = false) {
            await EnsureInitialized();
            var json = await ShowOpenFilePickerAsync(description, extensions, multiple);
            if (string.IsNullOrEmpty(json) || json == "[]") {
                return Array.Empty<PickedFile>();
            }
            try {
                var files = JsonSerializer.Deserialize<PickedFile[]>(json, new JsonSerializerOptions {
                    PropertyNameCaseInsensitive = true
                });
                return files ?? Array.Empty<PickedFile>();
            } catch (Exception e) {
                Log.Error(e, "Failed to parse open file picker result: {Json}", json);
                return Array.Empty<PickedFile>();
            }
        }

        // Show a save file picker and write data from an OPFS path to the user-chosen location.        
        public static async Task<string> SaveFilePickerFromOpfsAsync(
            string opfsPath, string suggestedName, string description, string extensions) {
            await EnsureInitialized();
            return await SaveFileFromOpfsAsync(opfsPath, suggestedName, description, extensions);
        }

        // Show a save file picker and write raw bytes to the user-chosen location.
        public static async Task<string> SaveFilePickerBytesAsync(
            string suggestedName, string description, string extensions, byte[] data) {
            await EnsureInitialized();
            return await SaveFilePickerJSAsync(suggestedName, description, extensions, data);
        }

        // Download a file from OPFS path (fallback for browsers without save picker).
        public static async Task<bool> DownloadFileAsync(string opfsPath, string filename) {
            await EnsureInitialized();
            return await DownloadFromOpfsAsync(opfsPath, filename);
        }

        // Download bytes directly to user-chosen location via save file picker.
        public static async Task<string> DownloadBytesAsync(string filename, byte[] data) {
            await EnsureInitialized();
            return await SaveFilePickerJSAsync(filename, "WAV Audio", ".wav", data);
        }

        // Open a URL in a new browser tab.
        public static void OpenUrl(string url) {
            OpenUrlJS(url);
        }

        // Show a browser confirm dialog. Returns true if OK was clicked.
        public static bool Confirm(string message) {
            return ConfirmDialogJS(message);
        }

        // Show a save/don't-save/cancel dialog. Returns "yes", "no", or "cancel".
        public static string ConfirmYesNoCancel(string message) {
            return ConfirmYesNoCancelJS(message);
        }

        // Toggle browser fullscreen mode.
        public static void ToggleFullScreen() {
            ToggleFullScreenJS();
        }

        // Clean up temp files from file picker operations.
        public static async Task CleanupPickerTempAsync() {
            await EnsureInitialized();
            await CleanupPickerTempJSAsync();
        }
    }
}
