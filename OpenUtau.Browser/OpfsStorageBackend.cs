using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.App.Browser {
    public class OpfsStorageBackend : Core.IStorageBackend {
        private readonly string basePath;
        private bool initialized;

        private string GetFullPath(string path) {
            return string.IsNullOrEmpty(basePath) ? path : Path.Combine(basePath, path);
        }

        public async Task<byte[]?> ReadAsync(string path) {
            try {
                if (!initialized) {
                    await OpfsService.EnsureInitialized();
                    initialized = true;
                }
                return await OpfsService.LoadAsync(GetFullPath(path));
            } catch (Exception e) {
                Log.Error(e, "OPFS read failed: {Path}", path);
                return null;
            }
        }

        public async Task WriteAsync(string path, byte[] data) {
            try {
                if (!initialized) {
                    await OpfsService.EnsureInitialized();
                    initialized = true;
                }
                await OpfsService.SaveAsync(GetFullPath(path), data);
            } catch (Exception e) {
                Log.Error(e, "OPFS write failed: {Path}", path);
            }
        }

        public async Task DeleteAsync(string path) {
            try {
                if (!initialized) {
                    await OpfsService.EnsureInitialized();
                    initialized = true;
                }
                await OpfsService.DeleteAsync(GetFullPath(path));
            } catch (Exception e) {
                Log.Error(e, "OPFS delete failed: {Path}", path);
            }
        }

        public async Task<bool> ExistsAsync(string path) {
            try {
                if (!initialized) {
                    await OpfsService.EnsureInitialized();
                    initialized = true;
                }
                return await OpfsService.ExistsAsync(GetFullPath(path));
            } catch (Exception e) {
                Log.Error(e, "OPFS exists check failed: {Path}", path);
                return false;
            }
        }

        public async Task CreateDirAsync(string path) {
            try {
                if (!initialized) {
                    await OpfsService.EnsureInitialized();
                    initialized = true;
                }
                await OpfsService.CreateDirAsync(GetFullPath(path));
            } catch (Exception e) {
                Log.Error(e, "OPFS mkdir failed: {Path}", path);
            }
        }

        public async Task DeleteDirAsync(string path) {
            try {
                if (!initialized) {
                    await OpfsService.EnsureInitialized();
                    initialized = true;
                }
                await OpfsService.DeleteDirAsync(GetFullPath(path));
            } catch (Exception e) {
                Log.Error(e, "OPFS rmdir failed: {Path}", path);
            }
        }
    }
}
