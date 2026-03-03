using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App.Browser {
    public class BrowserFileSystem : IFileSystem {
        // Paths that are served by FS Access API (user-picked directories)
        // These are dynamically added when the user picks a singer directory.
        private readonly HashSet<string> fsAccessMountPaths = new HashSet<string>();

        // Paths served by OPFS (app storage)
        private readonly string opfsBasePath;

        public BrowserFileSystem(string opfsBasePath = "/openutau") {
            this.opfsBasePath = NormalizePath(opfsBasePath);
        }

        /// Register a mounted FS Access directory path.
        /// Called after the user picks a directory via the folder picker.
        public void AddFsAccessMount(string mountPath) {
            fsAccessMountPaths.Add(NormalizePath(mountPath));
            Log.Information("BrowserFileSystem: registered FS Access mount at {Path}", mountPath);
        }

        /// Get all registered FS Access mount paths.
        public IEnumerable<string> GetFsAccessMounts() => fsAccessMountPaths;

        private bool IsFsAccessPath(string path) {
            var normalized = NormalizePath(path);
            foreach (var mount in fsAccessMountPaths) {
                if (normalized.StartsWith(mount + "/") || normalized == mount) {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizePath(string path) {
            return ("/" + path.Replace('\\', '/'))
                .Replace("//", "/")
                .TrimEnd('/');
        }

        public bool FileExists(string path) {
            if (IsFsAccessPath(path)) {
                return FsAccessService.FileExistsAsync(path).Result;
            }
            // Fallback: OPFS or native
            try {
                return File.Exists(path);
            } catch {
                return false;
            }
        }

        public Stream OpenRead(string path) {
            if (IsFsAccessPath(path)) {
                var data = FsAccessService.ReadFileAsync(path).Result;
                if (data == null) {
                    throw new FileNotFoundException($"File not found via FS Access API: {path}");
                }
                return new MemoryStream(data, writable: false);
            }
            return File.OpenRead(path);
        }

        public Stream OpenFile(string path, FileMode mode, FileAccess access) {
            if (IsFsAccessPath(path)) {
                if (access == FileAccess.Read || access == FileAccess.ReadWrite) {
                    // For read operations, load into memory
                    var data = FsAccessService.ReadFileAsync(path).Result;
                    if (data == null) {
                        if (mode == FileMode.Open) {
                            throw new FileNotFoundException($"File not found via FS Access API: {path}");
                        }
                        return new MemoryStream();
                    }
                    return new MemoryStream(data, writable: access != FileAccess.Read);
                }
                // Write-only to FS Access not supported (read-only access from user filesystem)
                throw new NotSupportedException("Write access to FS Access API directories is not supported");
            }
            return new FileStream(path, mode, access);
        }

        public byte[] ReadAllBytes(string path) {
            if (IsFsAccessPath(path)) {
                var data = FsAccessService.ReadFileAsync(path).Result;
                if (data == null) {
                    throw new FileNotFoundException($"File not found via FS Access API: {path}");
                }
                return data;
            }
            return File.ReadAllBytes(path);
        }

        public string ReadAllText(string path, Encoding encoding) {
            if (IsFsAccessPath(path)) {
                var data = FsAccessService.ReadFileAsync(path).Result;
                if (data == null) {
                    throw new FileNotFoundException($"File not found via FS Access API: {path}");
                }
                return encoding.GetString(data);
            }
            return File.ReadAllText(path, encoding);
        }

        public string[] ReadAllLines(string path, Encoding encoding) {
            var text = ReadAllText(path, encoding);
            return text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        }

        public IEnumerable<string> ReadLines(string path, Encoding encoding) {
            return ReadAllLines(path, encoding);
        }

        public void WriteAllText(string path, string contents, Encoding encoding) {
            if (IsFsAccessPath(path)) {
                throw new NotSupportedException("Cannot write to FS Access API directories");
            }
            File.WriteAllText(path, contents, encoding);
        }

        public void WriteAllLines(string path, string[] contents, Encoding encoding) {
            if (IsFsAccessPath(path)) {
                throw new NotSupportedException("Cannot write to FS Access API directories");
            }
            File.WriteAllLines(path, contents, encoding);
        }

        public long GetFileLength(string path) {
            if (IsFsAccessPath(path)) {
                var size = FsAccessService.GetFileSizeAsync(path).Result;
                return size >= 0 ? size : 0;
            }
            try {
                return new FileInfo(path).Length;
            } catch {
                return 0;
            }
        }

        public void FileCopy(string source, string dest, bool overwrite = false) {
            if (IsFsAccessPath(source)) {
                // Read from FS Access, write to native/OPFS
                var data = FsAccessService.ReadFileAsync(source).Result;
                if (data == null) {
                    throw new FileNotFoundException($"Source file not found: {source}");
                }
                var dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }
                if (!overwrite && File.Exists(dest)) {
                    throw new IOException($"Destination file already exists: {dest}");
                }
                File.WriteAllBytes(dest, data);
                return;
            }
            File.Copy(source, dest, overwrite);
        }

        public void FileDelete(string path) {
            if (IsFsAccessPath(path)) {
                throw new NotSupportedException("Cannot delete files from FS Access API directories");
            }
            File.Delete(path);
        }

        public void WriteAllBytes(string path, byte[] data) {
            if (IsFsAccessPath(path)) {
                throw new NotSupportedException("Cannot write to FS Access API directories");
            }
            File.WriteAllBytes(path, data);
        }

        public FileAttributes GetFileAttributes(string path) {
            if (IsFsAccessPath(path)) {
                // FS Access API doesn't expose file attributes
                return FileExists(path) ? FileAttributes.Normal : 0;
            }
            try {
                return File.GetAttributes(path);
            } catch {
                return 0;
            }
        }

        public DateTime GetFileCreationTime(string path) {
            if (IsFsAccessPath(path)) {
                // FS Access API doesn't expose creation time
                return DateTime.MinValue;
            }
            try {
                return File.GetCreationTime(path);
            } catch {
                return DateTime.MinValue;
            }
        }

        public bool DirectoryExists(string path) {
            if (IsFsAccessPath(path)) {
                return FsAccessService.DirectoryExistsAsync(path).Result;
            }
            try {
                return Directory.Exists(path);
            } catch {
                return false;
            }
        }

        public void CreateDirectory(string path) {
            if (IsFsAccessPath(path)) {
                throw new NotSupportedException("Cannot create directories in FS Access API directories");
            }
            Directory.CreateDirectory(path);
        }

        public void DeleteDirectory(string path, bool recursive) {
            if (IsFsAccessPath(path)) {
                throw new NotSupportedException("Cannot delete FS Access API directories");
            }
            Directory.Delete(path, recursive);
        }

        public IEnumerable<string> EnumerateFiles(string path, string pattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly) {
            var normalizedPath = NormalizePath(path);
            IEnumerable<string> results;

            if (IsFsAccessPath(path)) {
                if (searchOption == SearchOption.AllDirectories) {
                    var json = FsAccessService.EnumerateFilesRecursiveAsync(normalizedPath, pattern).Result;
                    var relativePaths = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                    results = relativePaths.Select(rp => normalizedPath + "/" + rp.Replace('\\', '/'));
                } else {
                    string json;
                    if (pattern == "*" || pattern == "*.*") {
                        json = FsAccessService.EnumerateFilesAsync(normalizedPath).Result;
                    } else {
                        json = FsAccessService.EnumerateFilesWithPatternAsync(normalizedPath, pattern).Result;
                    }
                    var fileNames = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                    results = fileNames.Select(f => normalizedPath + "/" + f);
                }
            } else {
                try {
                    results = Directory.Exists(path)
                        ? Directory.EnumerateFiles(path, pattern, searchOption)
                        : Enumerable.Empty<string>();
                } catch {
                    results = Enumerable.Empty<string>();
                }
            }

            if (searchOption == SearchOption.AllDirectories && !IsFsAccessPath(path)) {
                foreach (var mount in fsAccessMountPaths) {
                    if (mount.StartsWith(normalizedPath + "/")) {
                        var json = FsAccessService.EnumerateFilesRecursiveAsync(mount, pattern).Result;
                        var relativePaths = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                        results = results.Concat(relativePaths.Select(rp => mount + "/" + rp.Replace('\\', '/')));
                    }
                }
            }

            return results;
        }

        public string[] GetDirectories(string path) {
            var normalizedPath = NormalizePath(path);
            string[] nativeDirs;

            if (IsFsAccessPath(path)) {
                var json = FsAccessService.EnumerateDirectoriesAsync(normalizedPath).Result;
                var dirNames = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                nativeDirs = dirNames.Select(d => normalizedPath + "/" + d).ToArray();
            } else {
                try {
                    nativeDirs = Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();
                } catch {
                    nativeDirs = Array.Empty<string>();
                }
            }

            // Merge in FS Access mounts that are direct children of this path.
            var fsAccessChildren = new List<string>();
            foreach (var mount in fsAccessMountPaths) {
                // Check if mount is a direct child of normalizedPath
                if (mount.StartsWith(normalizedPath + "/")) {
                    var remaining = mount.Substring(normalizedPath.Length + 1);
                    // Direct child: no more slashes in the remaining portion
                    if (!remaining.Contains('/')) {
                        fsAccessChildren.Add(mount);
                    }
                }
            }

            return nativeDirs.Concat(fsAccessChildren).Distinct().ToArray();
        }

        public string[] GetFiles(string path, string pattern = "*") {
            return EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }

    }
}
