let opfsRoot = null;

// Map to cache file data during async read -> sync transfer
// Key: unique integer ID, Value: Uint8Array of file contents
const fileCache = new Map();
let nextCacheId = 1;

async function init() {
    console.log("[OPFS] init called");
    if (!opfsRoot) {
        console.log("[OPFS] Getting storage directory...");
        opfsRoot = await navigator.storage.getDirectory();
        console.log("[OPFS] Got storage directory:", opfsRoot);
    }
    console.log("[OPFS] init done");
}

function normalizeSegments(path) {
    return String(path ?? "")
        .replaceAll("\\", "/")
        .split("/")
        .filter(Boolean);
}

async function getParentDirAndName(path, createParents = false) {
    await init();
    const segments = normalizeSegments(path);
    if (segments.length === 0) {
        throw new TypeError("Path must include at least one segment.");
    }

    let dir = opfsRoot;
    for (let i = 0; i < segments.length - 1; i++) {
        dir = await dir.getDirectoryHandle(segments[i], { create: createParents });
    }

    return { dir, name: segments[segments.length - 1] };
}

async function writeFile(fileName, uint8Array) {
    console.log("[OPFS] writeFile called:", fileName, "size:", uint8Array?.length);
    const { dir, name } = await getParentDirAndName(fileName, true);
    console.log("[OPFS] Getting file handle:", fileName);
    const fileHandle = await dir.getFileHandle(name, { create: true });
    console.log("[OPFS] Creating writable...");
    const writable = await fileHandle.createWritable();
    console.log("[OPFS] Writing data...");
    await writable.write(uint8Array);
    console.log("[OPFS] Closing...");
    await writable.close();
    console.log("[OPFS] writeFile done:", fileName);
}

// Read file asynchronously and cache it
// Returns a unique integer ID that can be used to retrieve the cached data
async function readFileAsync(fileName) {
    console.log("[OPFS] readFileAsync called:", fileName);
    try {
        const { dir, name } = await getParentDirAndName(fileName, false);
        console.log("[OPFS] Getting file handle:", fileName);
        const fileHandle = await dir.getFileHandle(name);
        console.log("[OPFS] Getting file...");
        const file = await fileHandle.getFile();
        console.log("[OPFS] Getting arrayBuffer...");
        const arrayBuffer = await file.arrayBuffer();
        const uint8 = new Uint8Array(arrayBuffer);
        
        // Store in cache with unique ID
        const cacheId = nextCacheId++;
        fileCache.set(cacheId, uint8);
        console.log("[OPFS] readFileAsync cached file with ID:", cacheId, "size:", uint8.length);
        return cacheId;
    } catch (e) {
        console.error("[OPFS] readFileAsync error:", e);
         return -1;
    }
}

// Get cached file data as a byte array (returns as Uint8Array)
// Cleans up the cache entry after retrieval
function getCachedFileBytes(cacheId) {
    console.log("[OPFS] getCachedFileBytes called: cacheId:", cacheId);
    try {
        const uint8 = fileCache.get(cacheId);
        if (!uint8) {
            console.error("[OPFS] Cache ID not found:", cacheId);
            return new Uint8Array(0);
        }
        
        // Create a copy to return (so modifications don't affect cache before cleanup)
        const result = new Uint8Array(uint8);
        console.log("[OPFS] getCachedFileBytes returning", result.length, "bytes, first 10:", Array.from(result.slice(0, 10)));
        
        // Clean up cache entry
        fileCache.delete(cacheId);
        return result;
    } catch (e) {
        console.error("[OPFS] getCachedFileBytes error:", e);
        return new Uint8Array(0);
    }
}

// Legacy read file into provided buffer, JS fills the buffer at offset
async function readFileIntoBuffer(fileName, buffer, offset, length) {
    console.log("[OPFS] readFileIntoBuffer called:", fileName, "length:", length);
    try {
        const { dir, name } = await getParentDirAndName(fileName, false);
        console.log("[OPFS] Getting file handle:", fileName);
        const fileHandle = await dir.getFileHandle(name);
        console.log("[OPFS] Getting file...");
        const file = await fileHandle.getFile();
        console.log("[OPFS] Getting arrayBuffer...");
        const arrayBuffer = await file.arrayBuffer();
        const uint8 = new Uint8Array(arrayBuffer);
        console.log("[OPFS] File size:", uint8.length, "Requested:", length);
        // Copy into provided buffer at offset using bulk copy
        const toRead = Math.min(length, uint8.length);
        buffer.set(uint8.subarray(0, toRead), offset);
        console.log("[OPFS] readFileIntoBuffer done, read:", toRead);
        return toRead;
    } catch (e) {
        console.error("[OPFS] readFileIntoBuffer error:", e);
        return -1;
    }
}

// Get file size
async function getFileSize(fileName) {
    console.log("[OPFS] getFileSize called:", fileName);
    try {
        const { dir, name } = await getParentDirAndName(fileName, false);
        const fileHandle = await dir.getFileHandle(name);
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
    const { dir, name } = await getParentDirAndName(fileName, false);
    await dir.removeEntry(name);
    console.log("[OPFS] deleteFile done:", fileName);
}

async function fileExists(fileName) {
    console.log("[OPFS] fileExists called:", fileName);
    try {
        const { dir, name } = await getParentDirAndName(fileName, false);
        await dir.getFileHandle(name);
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
    const segments = normalizeSegments(dirName);
    if (segments.length === 0) {
        throw new TypeError("Directory path must include at least one segment.");
    }
    let dir = opfsRoot;
    for (const segment of segments) {
        dir = await dir.getDirectoryHandle(segment, { create: true });
    }
    console.log("[OPFS] createDir done:", dirName);
}

async function deleteDir(dirName) {
    console.log("[OPFS] deleteDir called:", dirName);
    const { dir, name } = await getParentDirAndName(dirName, false);
    await dir.removeEntry(name, { recursive: true });
    console.log("[OPFS] deleteDir done:", dirName);
}

export { init, writeFile, readFileIntoBuffer, readFileAsync, getCachedFileBytes, getFileSize, deleteFile, fileExists, createDir, deleteDir };
