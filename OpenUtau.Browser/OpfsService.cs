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
                
                // First get file size
                var size = await GetFileSizeAsync(fileName);
                if (size < 0) {
                    return null;
                }
                if (size == 0) {
                    return Array.Empty<byte>();
                }
                
                // Allocate buffer in C# and pass to JS to fill
                var buffer = new byte[size];
                var bytesRead = await ReadFileIntoBufferAsync(fileName, buffer, 0, size);
                
                if (bytesRead < 0) {
                    return null;
                }
                if (bytesRead == 0) {
                    return Array.Empty<byte>();
                }
                
                Log.Information("OPFS loaded: {FileName}, size={Size}", fileName, bytesRead);
                if (bytesRead < size) {
                    var truncated = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, truncated, 0, bytesRead);
                    return truncated;
                }
                return buffer;
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

        public static async Task DeleteAsync(string fileName) {
            try {
                await EnsureInitialized();
                await DeleteFileAsync(fileName);
            } catch (Exception e) {
                Log.Error(e, "Failed to delete file from OPFS: {FileName}", fileName);
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

        public static async Task MkDirAsync(string dirName) {
            try {
                await EnsureInitialized();
                await CreateDirAsync(dirName);
            } catch (Exception e) {
                Log.Error(e, "Failed to create directory in OPFS: {DirName}", dirName);
            }
        }

        public static async Task RemoveDirAsync(string dirName) {
            try {
                await EnsureInitialized();
                await DeleteDirAsync(dirName);
            } catch (Exception e) {
                Log.Error(e, "Failed to delete directory in OPFS: {DirName}", dirName);
            }
        }
    }
}
