namespace OpenUtau.Core {
    // Singleton that provides the current platform's IFileSystem.
    public class FileSystemManager : Util.SingletonBase<FileSystemManager> {
        private IFileSystem fs;
        private readonly object fsLock = new object();

        public IFileSystem FS {
            get {
                lock (fsLock) {
                    if (fs == null) {
                        fs = new NativeFileSystem();
                    }
                    return fs;
                }
            }
        }

        // Set the file system implementation
        public void SetFileSystem(IFileSystem fileSystem) {
            lock (fsLock) {
                fs = fileSystem;
            }
        }
    }
}
