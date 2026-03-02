namespace OpenUtau.Core {
    // Singleton that provides the current platform's IFileSystem.
    public class FileSystemManager : Util.SingletonBase<FileSystemManager> {
        private IFileSystem fs;

        public IFileSystem FS {
            get {
                if (fs == null) {
                    fs = new NativeFileSystem();
                }
                return fs;
            }
        }

        // Set the file system implementation
        public void SetFileSystem(IFileSystem fileSystem) {
            fs = fileSystem;
        }
    }
}
