using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenUtau.Core {
    public class NativeFileSystem : IFileSystem {
        public bool FileExists(string path) => File.Exists(path);

        public Stream OpenRead(string path) => File.OpenRead(path);

        public Stream OpenFile(string path, FileMode mode, FileAccess access)
            => new FileStream(path, mode, access);

        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public string ReadAllText(string path, Encoding encoding) => File.ReadAllText(path, encoding);

        public string[] ReadAllLines(string path, Encoding encoding) => File.ReadAllLines(path, encoding);

        public IEnumerable<string> ReadLines(string path, Encoding encoding) => File.ReadLines(path, encoding);

        public void FileCopy(string source, string dest, bool overwrite = false)
            => File.Copy(source, dest, overwrite);

        public void FileDelete(string path) => File.Delete(path);

        public void WriteAllBytes(string path, byte[] data) => File.WriteAllBytes(path, data);

        public void WriteAllText(string path, string contents, Encoding encoding)
            => File.WriteAllText(path, contents, encoding);

        public void WriteAllLines(string path, string[] contents, Encoding encoding)
            => File.WriteAllLines(path, contents, encoding);

        public FileAttributes GetFileAttributes(string path) => File.GetAttributes(path);

        public DateTime GetFileCreationTime(string path) => File.GetCreationTime(path);

        public long GetFileLength(string path) => new FileInfo(path).Length;

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void DeleteDirectory(string path, bool recursive)
            => Directory.Delete(path, recursive);

        public IEnumerable<string> EnumerateFiles(string path, string pattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
            => Directory.EnumerateFiles(path, pattern, searchOption);

        public string[] GetDirectories(string path) => Directory.GetDirectories(path);

        public string[] GetFiles(string path, string pattern = "*")
            => Directory.GetFiles(path, pattern);
    }
}
