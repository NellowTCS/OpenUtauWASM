using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.App.Browser {
    public class OpfsStorageBackend : Core.IStorageBackend {
        private readonly string basePath;

        private static Task EnsureReadyAsync() => OpfsService.EnsureInitialized();

        private string GetFullPath(string path) {
            return string.IsNullOrEmpty(basePath) ? path : Path.Combine(basePath, path);
        }

        public async Task<byte[]?> ReadAsync(string path) {
            try {
                await EnsureReadyAsync();
                return await OpfsService.LoadAsync(GetFullPath(path));
            } catch (Exception e) {
                Log.Error(e, "OPFS read failed: {Path}", path);
                return null;
            }
        }

        public async Task WriteAsync(string path, byte[] data) {
            try {
                await EnsureReadyAsync();
                await OpfsService.SaveAsync(GetFullPath(path), data);
            } catch (Exception e) {
                Log.Error(e, "OPFS write failed: {Path}", path);
                throw;
            }
        }

        public async Task DeleteAsync(string path) {
            try {
                await EnsureReadyAsync();
                await OpfsService.DeleteAsync(GetFullPath(path));
            } catch (Exception e) {
                Log.Error(e, "OPFS delete failed: {Path}", path);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string path) {
            try {
                await EnsureReadyAsync();
                return await OpfsService.ExistsAsync(GetFullPath(path));
            } catch (Exception e) {
                Log.Error(e, "OPFS exists check failed: {Path}", path);
                return false;
            }
        }

        public async Task CreateDirAsync(string path) {
            try {
                await EnsureReadyAsync();
                await OpfsService.CreateDirAsync(GetFullPath(path));
            } catch (Exception e) {
                Log.Error(e, "OPFS mkdir failed: {Path}", path);
                throw;
            }
        }

        public async Task DeleteDirAsync(string path) {
            try {
                await EnsureReadyAsync();
                await OpfsService.DeleteDirAsync(GetFullPath(path));
            } catch (Exception e) {
                Log.Error(e, "OPFS rmdir failed: {Path}", path);
                throw;
            }
        }
    }
}
