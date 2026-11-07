using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenUtau.Core {
    // Platform-agnostic file system abstraction thing
    
    public interface IFileSystem {

        // Check if a file exists at the given path.
        bool FileExists(string path);

        // Open a file for reading. Returns a readable Stream.
        Stream OpenRead(string path);

        // Open a file with specified mode and access.
        Stream OpenFile(string path, FileMode mode, FileAccess access);

        // Read all bytes from a file.
        byte[] ReadAllBytes(string path);

        // Read all text from a file with the given encoding.
        string ReadAllText(string path, Encoding encoding);

        // Read all lines from a file with the given encoding.
        string[] ReadAllLines(string path, Encoding encoding);

        // Lazily read lines from a file with the given encoding.
        IEnumerable<string> ReadLines(string path, Encoding encoding);

        // Copy a file from source to destination.
        void FileCopy(string source, string dest, bool overwrite = false);

        // Delete a file.
        void FileDelete(string path);

        // Write all bytes to a file (creates or overwrites).
        void WriteAllBytes(string path, byte[] data);

        // Write all text to a file (creates or overwrites) with the given encoding.
        void WriteAllText(string path, string contents, Encoding encoding);

        // Write all lines to a file (creates or overwrites) with the given encoding.
        void WriteAllLines(string path, string[] contents, Encoding encoding);

        // Get file attributes (returns 0/default on platforms that don't support it).
        FileAttributes GetFileAttributes(string path);

        // Get file creation time (returns DateTime.MinValue on platforms that don't support it).
        DateTime GetFileCreationTime(string path);

        // Get the length of a file in bytes.
        long GetFileLength(string path);

        // Check if a directory exists.
        bool DirectoryExists(string path);

        // Create a directory (and parents if needed).
        void CreateDirectory(string path);

        // Delete a directory.
        void DeleteDirectory(string path, bool recursive);

        // Enumerate files in a directory.
        IEnumerable<string> EnumerateFiles(string path, string pattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly);

        // Get subdirectories of a directory.
        string[] GetDirectories(string path);
 
        // Get files in a directory matching a pattern 
        string[] GetFiles(string path, string pattern = "*");
    }
}
