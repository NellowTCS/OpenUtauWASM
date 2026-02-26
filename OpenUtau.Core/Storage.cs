using System;
using System.IO;
using System.Threading.Tasks;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    public interface IStorageBackend {
        Task<byte[]?> ReadAsync(string path);
        Task WriteAsync(string path, byte[] data);
        Task DeleteAsync(string path);
        Task<bool> ExistsAsync(string path);
        Task CreateDirAsync(string path);
        Task DeleteDirAsync(string path);
    }

    public class NativeFileStorage : IStorageBackend {
        public Task<byte[]?> ReadAsync(string path) {
            try {
                if (File.Exists(path)) {
                    return Task.FromResult<byte[]?>(File.ReadAllBytes(path));
                }
                return Task.FromResult<byte[]?>(null);
            } catch (Exception e) {
                Log.Error(e, "Failed to read file: {Path}", path);
                return Task.FromResult<byte[]?>(null);
            }
        }

        public Task WriteAsync(string path, byte[] data) {
            try {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllBytes(path, data);
                return Task.CompletedTask;
            } catch (Exception e) {
                Log.Error(e, "Failed to write file: {Path}", path);
                throw;
            }
        }

        public Task DeleteAsync(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
                return Task.CompletedTask;
            } catch (Exception e) {
                Log.Error(e, "Failed to delete file: {Path}", path);
                throw;
            }
        }

        public Task<bool> ExistsAsync(string path) {
            return Task.FromResult(File.Exists(path));
        }

        public Task CreateDirAsync(string path) {
            try {
                if (!Directory.Exists(path)) {
                    Directory.CreateDirectory(path);
                }
                return Task.CompletedTask;
            } catch (Exception e) {
                Log.Error(e, "Failed to create directory: {Path}", path);
                throw;
            }
        }

        public Task DeleteDirAsync(string path) {
            try {
                if (Directory.Exists(path)) {
                    Directory.Delete(path, true);
                }
                return Task.CompletedTask;
            } catch (Exception e) {
                Log.Error(e, "Failed to delete directory: {Path}", path);
                throw;
            }
        }
    }

    public static class Storage {
        private static IStorageBackend? backend;

        public static void SetBackend(IStorageBackend storageBackend) {
            backend = storageBackend;
        }

        public static IStorageBackend Backend {
            get {
                if (backend == null) {
                    backend = new NativeFileStorage();
                }
                return backend;
            }
        }

        public static async Task<string?> ReadTextAsync(string path) {
            var bytes = await Backend.ReadAsync(path);
            if (bytes == null) {
                return null;
            }
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        public static async Task WriteTextAsync(string path, string content) {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            await Backend.WriteAsync(path, bytes);
        }
    }
}
