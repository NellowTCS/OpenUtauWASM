using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.App.Browser {
    public static partial class OpfsService {
        private static bool initialized;

        [JSImport("writeFile", "opfsHelper")]
        internal static partial Task WriteFileAsync(string fileName, byte[] data);

        [JSImport("readFileIntoBuffer", "opfsHelper")]
        internal static partial Task<int> ReadFileIntoBufferAsync(string fileName, byte[] buffer, int offset, int length);

        // Two-step async: read file and cache it in JS, returns unique ID
        [JSImport("readFileAsync", "opfsHelper")]
        internal static partial Task<int> ReadFileAsyncAsync(string fileName);

        // Get cached file as byte array
        [JSImport("getCachedFileBytes", "opfsHelper")]
        internal static partial byte[] GetCachedFileBytesJs(int cacheId);

        [JSImport("getFileSize", "opfsHelper")]
        internal static partial Task<int> GetFileSizeAsync(string fileName);

        [JSImport("deleteFile", "opfsHelper")]
        internal static partial Task DeleteFileAsync(string fileName);

        [JSImport("fileExists", "opfsHelper")]
        internal static partial Task<bool> FileExistsAsync(string fileName);

        [JSImport("createDir", "opfsHelper")]
        internal static partial Task CreateDirAsync(string dirName);

        [JSImport("deleteDir", "opfsHelper")]
        internal static partial Task DeleteDirAsync(string dirName);

        [JSImport("init", "opfsHelper")]
        internal static partial Task InitAsync();

        public static async Task EnsureInitialized() {
            if (initialized) return;
            try {
                Log.Information("Importing OPFS module...");
                await JSHost.ImportAsync("opfsHelper", "../opfsHelper.js");
                await InitAsync();
                initialized = true;
                Log.Information("OPFS module imported successfully");
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize OPFS module");
                throw;
            }
        }

        public static async Task SaveAsync(string fileName, byte[] data) {
            try {
                await EnsureInitialized();
                await WriteFileAsync(fileName, data);
                Log.Information("OPFS saved: {FileName}, size={Size}", fileName, data.Length);
            } catch (Exception e) {
                Log.Error(e, "Failed to save file to OPFS: {FileName}", fileName);
                throw;
            }
        }

        public static async Task SaveTextAsync(string fileName, string content) {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            await SaveAsync(fileName, bytes);
        }

        public static async Task<byte[]?> LoadAsync(string fileName) {
            try {
                await EnsureInitialized();
                
                // Async read and cache the file
                var cacheId = await ReadFileAsyncAsync(fileName);
                if (cacheId < 0) {
                    Log.Error("Failed to read file asynchronously: {FileName}", fileName);
                    return null;
                }

                // Retrieve the cached bytes directly as byte array
                var data = GetCachedFileBytesJs(cacheId);
                if (data == null || data.Length == 0) {
                    Log.Warning("File is empty or retrieval failed: {FileName}", fileName);
                    return Array.Empty<byte>();
                }
                Log.Information("OPFS loaded: {FileName}, size={Size}", fileName, data.Length);
                return data;
            } catch (Exception e) {
                Log.Error(e, "Failed to load file from OPFS: {FileName}", fileName);
                return null;
            }
        }

        public static async Task<string?> LoadTextAsync(string fileName) {
            var bytes = await LoadAsync(fileName);
            if (bytes == null) return null;
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        public static async Task<bool> DeleteAsync(string fileName) {
            try {
                await EnsureInitialized();
                await DeleteFileAsync(fileName);
                return true;
            } catch (Exception e) {
                Log.Error(e, "Failed to delete file from OPFS: {FileName}", fileName);
                return false;
            }
        }

        public static async Task<bool> ExistsAsync(string fileName) {
            try {
                await EnsureInitialized();
                return await FileExistsAsync(fileName);
            } catch (Exception e) {
                Log.Error(e, "Failed to check file existence in OPFS: {FileName}", fileName);
                return false;
            }
        }

        public static async Task<bool> MkDirAsync(string dirName) {
            try {
                await EnsureInitialized();
                await CreateDirAsync(dirName);
                return true;
            } catch (Exception e) {
                Log.Error(e, "Failed to create directory in OPFS: {DirName}", dirName);
                return false;
            }
        }

        public static async Task<bool> RemoveDirAsync(string dirName) {
            try {
                await EnsureInitialized();
                await DeleteDirAsync(dirName);
                return true;
            } catch (Exception e) {
                Log.Error(e, "Failed to delete directory in OPFS: {DirName}", dirName);
                return false;
            }
        }
    }
}
