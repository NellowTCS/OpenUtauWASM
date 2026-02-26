let opfsRoot = null;

async function init() {
    console.log("[OPFS] init called");
    if (!opfsRoot) {
        console.log("[OPFS] Getting storage directory...");
        opfsRoot = await navigator.storage.getDirectory();
        console.log("[OPFS] Got storage directory:", opfsRoot);
    }
    console.log("[OPFS] init done");
}

async function writeFile(fileName, uint8Array) {
    console.log("[OPFS] writeFile called:", fileName, "size:", uint8Array?.length);
    await init();
    console.log("[OPFS] Getting file handle:", fileName);
    const fileHandle = await opfsRoot.getFileHandle(fileName, { create: true });
    console.log("[OPFS] Creating writable...");
    const writable = await fileHandle.createWritable();
    console.log("[OPFS] Writing data...");
    await writable.write(uint8Array);
    console.log("[OPFS] Closing...");
    await writable.close();
    console.log("[OPFS] writeFile done:", fileName);
}

// Read file into provided buffer - JS fills the buffer at offset
async function readFileIntoBuffer(fileName, buffer, offset, length) {
    console.log("[OPFS] readFileIntoBuffer called:", fileName, "length:", length);
    await init();
    try {
        console.log("[OPFS] Getting file handle:", fileName);
        const fileHandle = await opfsRoot.getFileHandle(fileName);
        console.log("[OPFS] Getting file...");
        const file = await fileHandle.getFile();
        console.log("[OPFS] Getting arrayBuffer...");
        const arrayBuffer = await file.arrayBuffer();
        const uint8 = new Uint8Array(arrayBuffer);
        console.log("[OPFS] File size:", uint8.length, "Requested:", length);
        // Copy into provided buffer at offset
        for (let i = 0; i < length && i < uint8.length; i++) {
            buffer[offset + i] = uint8[i];
        }
        console.log("[OPFS] readFileIntoBuffer done, read:", Math.min(length, uint8.length));
        return Math.min(length, uint8.length);
    } catch (e) {
        console.error("[OPFS] readFileIntoBuffer error:", e);
        return -1;
    }
}

// Get file size
async function getFileSize(fileName) {
    console.log("[OPFS] getFileSize called:", fileName);
    await init();
    try {
        const fileHandle = await opfsRoot.getFileHandle(fileName);
        const file = await fileHandle.getFile();
        console.log("[OPFS] getFileSize done:", file.size);
        return file.size;
    } catch (e) {
        console.log("[OPFS] getFileSize file not found:", fileName);
        return -1;
    }
}

async function deleteFile(fileName) {
    console.log("[OPFS] deleteFile called:", fileName);
    await init();
    await opfsRoot.removeEntry(fileName);
    console.log("[OPFS] deleteFile done:", fileName);
}

async function fileExists(fileName) {
    console.log("[OPFS] fileExists called:", fileName);
    await init();
    try {
        await opfsRoot.getFileHandle(fileName);
        console.log("[OPFS] fileExists true:", fileName);
        return true;
    } catch (e) {
        console.log("[OPFS] fileExists false:", fileName);
        return false;
    }
}

async function createDir(dirName) {
    console.log("[OPFS] createDir called:", dirName);
    await init();
    await opfsRoot.getDirectoryHandle(dirName, { create: true });
    console.log("[OPFS] createDir done:", dirName);
}

async function deleteDir(dirName) {
    console.log("[OPFS] deleteDir called:", dirName);
    await init();
    await opfsRoot.removeEntry(dirName, { recursive: true });
    console.log("[OPFS] deleteDir done:", dirName);
}

export { init, writeFile, readFileIntoBuffer, getFileSize, deleteFile, fileExists, createDir, deleteDir };
