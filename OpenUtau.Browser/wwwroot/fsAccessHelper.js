// Map of virtual mount paths -> { handle: FileSystemDirectoryHandle, name: string }
const mountedDirs = new Map();

// Cache for async file reads to support two-step transfer pattern
const fileCache = new Map();
let nextCacheId = 1;

/**
 * Show a directory picker dialog and mount the selected directory.
 * Returns the mount path
**/
export async function pickAndMountDirectory(mountBasePath) {
    try {
        const dirHandle = await window.showDirectoryPicker({ mode: 'read' });
        const name = dirHandle.name;
        const mountPath = normalizePath(mountBasePath + '/' + name);

        mountedDirs.set(mountPath, { handle: dirHandle, name: name });

        // Store in IndexedDB for persistence
        try {
            const { saveBookmark } = await import('./bookmarkHelper.js');
            await saveBookmark(dirHandle, 'singer_' + name);
        } catch (e) {
            console.warn('[FSAccess] Could not save bookmark:', e);
        }

        console.log('[FSAccess] Mounted directory:', name, 'at', mountPath);
        return mountPath;
    } catch (e) {
        if (e.name === 'AbortError') {
            console.log('[FSAccess] User cancelled directory picker');
            return '';
        }
        console.error('[FSAccess] pickAndMountDirectory error:', e);
        return '';
    }
}

// Get all currently mounted directory paths as a JSON array string.
export function getMountedPaths() {
    return JSON.stringify(Array.from(mountedDirs.keys()));
}

// Check if a directory exists at the given path (either a mounted root or a subdirectory)
export async function directoryExists(path) {
    try {
        const resolved = await resolveDirectoryHandle(path);
        return resolved !== null;
    } catch (e) {
        return false;
    }
}

// Check if a file exists at the given path.
export async function fileExists(path) {
    try {
        const resolved = await resolveFileHandle(path);
        return resolved !== null;
    } catch (e) {
        return false;
    }
}

// Read a file and return its contents as a Uint8Array
export async function readFileBytes(path) {
    try {
        const fileHandle = await resolveFileHandle(path);
        if (!fileHandle) return null;
        const file = await fileHandle.getFile();
        const buffer = await file.arrayBuffer();
        const uint8 = new Uint8Array(buffer);
        console.log('[FSAccess] readFileBytes:', path, 'size:', uint8.length, 'first 10 bytes:', Array.from(uint8.slice(0, 10)));
        return new Uint8Array(buffer);
    } catch (e) {
        console.error('[FSAccess] readFileBytes error for', path, ':', e);
        return null;
    }
}

// Read file asynchronously and cache it
// Returns a unique integer ID that can be used to retrieve the cached data
export async function readFileAsyncAndCache(path) {
    console.log('[FSAccess] readFileAsyncAndCache called:', path);
    try {
        const fileHandle = await resolveFileHandle(path);
        if (!fileHandle) return -1;
        const file = await fileHandle.getFile();
        const arrayBuffer = await file.arrayBuffer();
        const uint8 = new Uint8Array(arrayBuffer);
        
        // Store in cache with unique ID
        const cacheId = nextCacheId++;
        fileCache.set(cacheId, uint8);
        console.log('[FSAccess] readFileAsyncAndCache cached file with ID:', cacheId, 'size:', uint8.length);
        return cacheId;
    } catch (e) {
        console.error('[FSAccess] readFileAsyncAndCache error for', path, ':', e);
        return -1;
    }
}

// Get cached file data as a byte array (returns as Uint8Array)
// Cleans up the cache entry after retrieval
export function getCachedFileBytes(cacheId) {
    console.log('[FSAccess] getCachedFileBytes called: cacheId:', cacheId);
    try {
        const uint8 = fileCache.get(cacheId);
        if (!uint8) {
            console.error('[FSAccess] Cache ID not found:', cacheId);
            return new Uint8Array(0);
        }
        
        // Create a copy to return (so modifications don't affect cache before cleanup)
        const result = new Uint8Array(uint8);
        console.log('[FSAccess] getCachedFileBytes returning', result.length, 'bytes, first 10:', Array.from(result.slice(0, 10)));
        
        // Clean up cache entry
        fileCache.delete(cacheId);
        return result;
    } catch (e) {
        console.error('[FSAccess] getCachedFileBytes error:', e);
        return new Uint8Array(0);
    }
}

// Read file into a provided buffer at offset (for C# interop, avoids extra copy)
export async function readFileIntoBuffer(path, buffer, offset, length) {
    try {
        const fileHandle = await resolveFileHandle(path);
        if (!fileHandle) return -1;
        const file = await fileHandle.getFile();
        const arrayBuffer = await file.arrayBuffer();
        const uint8 = new Uint8Array(arrayBuffer);
        const toRead = Math.min(length, uint8.length);
        console.log('[FSAccess] readFileIntoBuffer:', path, 'requested:', length, 'available:', uint8.length, 'first 10 bytes:', Array.from(uint8.slice(0, 10)));
        buffer.set(uint8.subarray(0, toRead), offset);
        return toRead;
    } catch (e) {
        console.error('[FSAccess] readFileIntoBuffer error for', path, ':', e);
        return -1;
    }
}

// Get file size
export async function getFileSize(path) {
    try {
        const fileHandle = await resolveFileHandle(path);
        if (!fileHandle) return -1;
        const file = await fileHandle.getFile();
        return file.size;
    } catch (e) {
        return -1;
    }
}

// Enumerate files in a directory (non-recursive)
export async function enumerateFiles(dirPath) {
    try {
        const dirHandle = await resolveDirectoryHandle(dirPath);
        if (!dirHandle) return '[]';
        const files = [];
        for await (const [name, handle] of dirHandle.entries()) {
            if (handle.kind === 'file') {
                files.push(name);
            }
        }
        return JSON.stringify(files);
    } catch (e) {
        console.error('[FSAccess] enumerateFiles error for', dirPath, ':', e);
        return '[]';
    }
}

// Enumerate files in a directory matching a pattern (non-recursive)
export async function enumerateFilesWithPattern(dirPath, pattern) {
    try {
        const dirHandle = await resolveDirectoryHandle(dirPath);
        if (!dirHandle) return '[]';
        const regex = patternToRegex(pattern);
        const files = [];
        for await (const [name, handle] of dirHandle.entries()) {
            if (handle.kind === 'file' && regex.test(name)) {
                files.push(name);
            }
        }
        return JSON.stringify(files);
    } catch (e) {
        console.error('[FSAccess] enumerateFilesWithPattern error for', dirPath, pattern, ':', e);
        return '[]';
    }
}

// Recursively enumerate files matching a pattern.
export async function enumerateFilesRecursive(dirPath, pattern) {
    try {
        const dirHandle = await resolveDirectoryHandle(dirPath);
        if (!dirHandle) return '[]';
        const regex = patternToRegex(pattern);
        const results = [];
        await walkDirectory(dirHandle, '', regex, results);
        return JSON.stringify(results);
    } catch (e) {
        console.error('[FSAccess] enumerateFilesRecursive error for', dirPath, pattern, ':', e);
        return '[]';
    }
}

// Enumerate subdirectories in a directory (non-recursive)
export async function enumerateDirectories(dirPath) {
    try {
        const dirHandle = await resolveDirectoryHandle(dirPath);
        if (!dirHandle) return '[]';
        const dirs = [];
        for await (const [name, handle] of dirHandle.entries()) {
            if (handle.kind === 'directory') {
                dirs.push(name);
            }
        }
        return JSON.stringify(dirs);
    } catch (e) {
        console.error('[FSAccess] enumerateDirectories error for', dirPath, ':', e);
        return '[]';
    }
}

/**
 * Show a file open picker dialog. Returns file contents as bytes written to OPFS temp files.
 * The files are copied to OPFS at /openutau/Temp/picker/ so C# can read them via normal file paths.
 */
// TODO: don't copy to OPFS, properly do
export async function showOpenFilePicker(acceptDescription, acceptExtensions, multiple) {
    try {
        if (!window.showOpenFilePicker) {
            console.error('[FSAccess] showOpenFilePicker not supported in this browser');
            return '[]';
        }

        const extensions = acceptExtensions.split(',').map(s => s.trim()).filter(Boolean);
        const options = {
            multiple: multiple,
            types: [{
                description: acceptDescription,
                accept: { 'application/octet-stream': extensions }
            }]
        };

        const handles = await window.showOpenFilePicker(options);
        if (!handles || handles.length === 0) return '[]';

        // Ensure temp directory exists in OPFS
        const opfsRoot = await navigator.storage.getDirectory();
        const openutauDir = await opfsRoot.getDirectoryHandle('openutau', { create: true });
        const tempDir = await openutauDir.getDirectoryHandle('Temp', { create: true });
        const pickerDir = await tempDir.getDirectoryHandle('picker', { create: true });

        const results = [];
        for (const fileHandle of handles) {
            const file = await fileHandle.getFile();
            const buffer = await file.arrayBuffer();
            const bytes = new Uint8Array(buffer);

            // Write to OPFS temp path with a unique name to avoid collisions
            const tempName = Date.now() + '_' + file.name;
            const tempFileHandle = await pickerDir.getFileHandle(tempName, { create: true });
            const writable = await tempFileHandle.createWritable();
            await writable.write(bytes);
            await writable.close();

            results.push({
                name: file.name,
                tempPath: '/openutau/Temp/picker/' + tempName
            });
        }

        return JSON.stringify(results);
    } catch (e) {
        if (e.name === 'AbortError') {
            console.log('[FSAccess] User cancelled file open picker');
            return '[]';
        }
        console.error('[FSAccess] showOpenFilePicker error:', e);
        return '[]';
    }
}

// Show a file save picker dialog and write the given bytes to the chosen file
export async function saveFilePicker(suggestedName, acceptDescription, acceptExtensions, data) {
    try {
        if (!window.showSaveFilePicker) {
            // Fallback: trigger download via blob
            return downloadFallback(suggestedName, data);
        }

        const extensions = acceptExtensions.split(',').map(s => s.trim()).filter(Boolean);
        const options = {
            suggestedName: suggestedName,
            types: [{
                description: acceptDescription,
                accept: { 'application/octet-stream': extensions }
            }]
        };

        const handle = await window.showSaveFilePicker(options);
        const writable = await handle.createWritable();
        await writable.write(data);
        await writable.close();

        return handle.name;
    } catch (e) {
        if (e.name === 'AbortError') {
            console.log('[FSAccess] User cancelled file save picker');
            return '';
        }
        console.error('[FSAccess] saveFilePicker error:', e);
        return '';
    }
}

// Save bytes from an OPFS file path through the save file picker
export async function saveFileFromOpfs(opfsPath, suggestedName, acceptDescription, acceptExtensions) {
    try {
        // Read from OPFS
        const normalizedPath = opfsPath.replace(/\\/g, '/').replace(/^\//, '');
        const segments = normalizedPath.split('/').filter(Boolean);
        
        const opfsRoot = await navigator.storage.getDirectory();
        let current = opfsRoot;
        for (let i = 0; i < segments.length - 1; i++) {
            current = await current.getDirectoryHandle(segments[i]);
        }
        const fileHandle = await current.getFileHandle(segments[segments.length - 1]);
        const file = await fileHandle.getFile();
        const buffer = await file.arrayBuffer();
        const data = new Uint8Array(buffer);

        return await saveFilePicker(suggestedName, acceptDescription, acceptExtensions, data);
    } catch (e) {
        console.error('[FSAccess] saveFileFromOpfs error:', e);
        return '';
    }
}

// Fallback download via blob URL for browsers without showSaveFilePicker (Firefox/Safari, the stragglers)
function downloadFallback(filename, data) {
    const blob = new Blob([data], { type: 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
    return filename;
}

// Download a file from an OPFS path (fallback)
export async function downloadFromOpfs(opfsPath, filename) {
    try {
        const normalizedPath = opfsPath.replace(/\\/g, '/').replace(/^\//, '');
        const segments = normalizedPath.split('/').filter(Boolean);
        
        const opfsRoot = await navigator.storage.getDirectory();
        let current = opfsRoot;
        for (let i = 0; i < segments.length - 1; i++) {
            current = await current.getDirectoryHandle(segments[i]);
        }
        const fileHandle = await current.getFileHandle(segments[segments.length - 1]);
        const file = await fileHandle.getFile();
        const buffer = await file.arrayBuffer();
        
        downloadFallback(filename, new Uint8Array(buffer));
        return true;
    } catch (e) {
        console.error('[FSAccess] downloadFromOpfs error:', e);
        return false;
    }
}

// Download raw bytes directly (no OPFS)
export function downloadBytes(filename, data) {
    try {
        downloadFallback(filename, data);
        return true;
    } catch (e) {
        console.error('[FSAccess] downloadBytes error:', e);
        return false;
    }
}

// Open a URL in a new browser tab.
export function openUrl(url) {
    window.open(url, '_blank');
}


// Show a browser confirm dialog (OK/Cancel).
// TODO: move to a avalonia modal
export function confirmDialog(message) {
    return window.confirm(message);
}

// Show a browser confirm dialog with Yes/No/Cancel
// TODO: move to a avalonia modal
export function confirmYesNoCancel(message) {
    // Browser has no native 3-button dialog, so we use confirm for yes/no for now.  
    // and treat page unload cancel specially.
    // First ask "Do you want to save?"
    const save = window.confirm(message + "\n\nOK = Save, Cancel = Don't save");
    if (save) return 'yes';
    // If they clicked Cancel, ask if they want to continue without saving
    const cont = window.confirm("Continue without saving?");
    if (cont) return 'no';
    return 'cancel';
}

// Toggle browser fullscreen mode
export function toggleFullScreen() {
    if (!document.fullscreenElement) {
        document.documentElement.requestFullscreen().catch(e => 
            console.warn('[FSAccess] Fullscreen request failed:', e));
    } else {
        document.exitFullscreen().catch(e => 
            console.warn('[FSAccess] Exit fullscreen failed:', e));
    }
}

// Clean up temp picker files from OPFS
export async function cleanupPickerTemp() {
    try {
        const opfsRoot = await navigator.storage.getDirectory();
        const openutauDir = await opfsRoot.getDirectoryHandle('openutau');
        const tempDir = await openutauDir.getDirectoryHandle('Temp');
        await tempDir.removeEntry('picker', { recursive: true });
    } catch (e) {
        // Ignore, directory may not exist
    }
}

/// TODO: move to opfsHelper

// Navigate OPFS directory tree to resolve a directory handle.
async function resolveOpfsDirectory(segments, create = false) {
    let current = await navigator.storage.getDirectory();
    for (const seg of segments) {
        try {
            current = await current.getDirectoryHandle(seg, { create: create });
        } catch {
            return null;
        }
    }
    return current;
}

// Parse an OPFS virtual path into segments.
function opfsPathSegments(path) {
    return path.replace(/\\/g, '/').replace(/^\//, '').split('/').filter(Boolean);
}

// Read a file from OPFS and copy it into a pre-allocated byte buffer.
export async function opfsReadFile(path, buffer, offset, length) {
    try {
        const segments = opfsPathSegments(path);
        if (segments.length === 0) return -1;
        const dirSegments = segments.slice(0, -1);
        const fileName = segments[segments.length - 1];

        const dirHandle = await resolveOpfsDirectory(dirSegments);
        if (!dirHandle) return -1;

        const fileHandle = await dirHandle.getFileHandle(fileName);
        const file = await fileHandle.getFile();
        const arrayBuffer = await file.arrayBuffer();
        const bytes = new Uint8Array(arrayBuffer);

        const toCopy = Math.min(bytes.length, length);
        buffer.set(bytes.subarray(0, toCopy), offset);
        return toCopy;
    } catch (e) {
        console.error('[OPFS] readFile error:', path, e);
        return -1;
    }
}

// Get the size of a file in OPFS.
export async function opfsGetFileSize(path) {
    try {
        const segments = opfsPathSegments(path);
        if (segments.length === 0) return -1;
        const dirSegments = segments.slice(0, -1);
        const fileName = segments[segments.length - 1];

        const dirHandle = await resolveOpfsDirectory(dirSegments);
        if (!dirHandle) return -1;

        const fileHandle = await dirHandle.getFileHandle(fileName);
        const file = await fileHandle.getFile();
        return file.size;
    } catch {
        return -1;
    }
}

// Check if a file exists in OPFS
export async function opfsFileExists(path) {
    try {
        const segments = opfsPathSegments(path);
        if (segments.length === 0) return false;
        const dirSegments = segments.slice(0, -1);
        const fileName = segments[segments.length - 1];

        const dirHandle = await resolveOpfsDirectory(dirSegments);
        if (!dirHandle) return false;

        await dirHandle.getFileHandle(fileName);
        return true;
    } catch {
        return false;
    }
}

// Check if a directory exists in OPFS.
export async function opfsDirectoryExists(path) {
    try {
        const segments = opfsPathSegments(path);
        if (segments.length === 0) return true; // root always exists
        const dirHandle = await resolveOpfsDirectory(segments);
        return dirHandle !== null;
    } catch {
        return false;
    }
}

// Create a directory (and parents) in OPFS
export async function opfsCreateDirectory(path) {
    try {
        const segments = opfsPathSegments(path);
        if (segments.length === 0) return true;
        const result = await resolveOpfsDirectory(segments, true);
        return result !== null;
    } catch (e) {
        console.error('[OPFS] createDirectory error:', path, e);
        return false;
    }
}

// Write a byte array to a file in OPFS. Creates parent directories as needed
export async function opfsWriteFile(path, data) {
    try {
        const segments = opfsPathSegments(path);
        if (segments.length === 0) return false;
        const dirSegments = segments.slice(0, -1);
        const fileName = segments[segments.length - 1];

        // Create parent directories
        const dirHandle = await resolveOpfsDirectory(dirSegments, true);
        if (!dirHandle) return false;

        const fileHandle = await dirHandle.getFileHandle(fileName, { create: true });
        const writable = await fileHandle.createWritable();
        await writable.write(data);
        await writable.close();
        return true;
    } catch (e) {
        console.error('[OPFS] writeFile error:', path, e);
        return false;
    }
}

// Delete a file from OPFS.
export async function opfsDeleteFile(path) {
    try {
        const segments = opfsPathSegments(path);
        if (segments.length === 0) return false;
        const dirSegments = segments.slice(0, -1);
        const fileName = segments[segments.length - 1];

        const dirHandle = await resolveOpfsDirectory(dirSegments);
        if (!dirHandle) return false;

        await dirHandle.removeEntry(fileName);
        return true;
    } catch {
        return false;
    }
}

// Delete a directory from OPFS.
export async function opfsDeleteDirectory(path, recursive) {
    try {
        const segments = opfsPathSegments(path);
        if (segments.length === 0) return false;
        const parentSegments = segments.slice(0, -1);
        const dirName = segments[segments.length - 1];

        const parentHandle = await resolveOpfsDirectory(parentSegments);
        if (!parentHandle) return false;

        await parentHandle.removeEntry(dirName, { recursive: recursive });
        return true;
    } catch {
        return false;
    }
}

/**
 * List files in an OPFS directory.
 * Returns JSON array of file names.
 */
export async function opfsEnumerateFiles(path) {
    try {
        const segments = opfsPathSegments(path);
        const dirHandle = await resolveOpfsDirectory(segments);
        if (!dirHandle) return '[]';

        const files = [];
        for await (const [name, handle] of dirHandle.entries()) {
            if (handle.kind === 'file') {
                files.push(name);
            }
        }
        return JSON.stringify(files);
    } catch {
        return '[]';
    }
}

/**
 * List subdirectories in an OPFS directory.
 * Returns JSON array of directory names.
 */
export async function opfsEnumerateDirectories(path) {
    try {
        const segments = opfsPathSegments(path);
        const dirHandle = await resolveOpfsDirectory(segments);
        if (!dirHandle) return '[]';

        const dirs = [];
        for await (const [name, handle] of dirHandle.entries()) {
            if (handle.kind === 'directory') {
                dirs.push(name);
            }
        }
        return JSON.stringify(dirs);
    } catch {
        return '[]';
    }
}

/**
 * Recursively enumerate files in an OPFS directory matching a pattern.
 * Returns JSON array of relative paths.
 */
export async function opfsEnumerateFilesRecursive(path, pattern) {
    try {
        const segments = opfsPathSegments(path);
        const dirHandle = await resolveOpfsDirectory(segments);
        if (!dirHandle) return '[]';

        const regex = patternToRegex(pattern);
        const results = [];
        await walkDirectory(dirHandle, '', regex, results);
        return JSON.stringify(results);
    } catch {
        return '[]';
    }

}

/// TODO: move to a new JS file, fsUtils or something

function normalizePath(path) {
    return ('/' + path.replace(/\\/g, '/'))
        .replace(/\/+/g, '/')
        .replace(/\/$/, '') || '/';
}

// Find the mount entry that contains the given path, then navigate into subdirectories
async function resolveDirectoryHandle(path) {
    path = normalizePath(path);

    // Direct mount hit
    if (mountedDirs.has(path)) {
        return mountedDirs.get(path).handle;
    }

    // Find the longest matching mount prefix
    let bestMount = null;
    let bestMountPath = '';
    for (const [mountPath, entry] of mountedDirs) {
        if (path.startsWith(mountPath + '/') || path === mountPath) {
            if (mountPath.length > bestMountPath.length) {
                bestMount = entry;
                bestMountPath = mountPath;
            }
        }
    }

    if (!bestMount) return null;

    // Navigate into subdirectories
    const remaining = path.substring(bestMountPath.length).replace(/^\//, '');
    if (!remaining) return bestMount.handle;

    const segments = remaining.split('/').filter(Boolean);
    let current = bestMount.handle;
    for (const seg of segments) {
        try {
            current = await current.getDirectoryHandle(seg);
        } catch {
            // Case-insensitive fallback: search for matching entry
            const match = await findHandleCaseInsensitive(current, seg, 'directory');
            if (!match) return null;
            current = match;
        }
    }
    return current;
}

//  Resolve a file handle from a virtual path.
async function resolveFileHandle(path) {
    path = normalizePath(path);
    const lastSlash = path.lastIndexOf('/');
    const dirPath = lastSlash > 0 ? path.substring(0, lastSlash) : '/';
    const fileName = path.substring(lastSlash + 1);

    if (!fileName) return null;

    const dirHandle = await resolveDirectoryHandle(dirPath);
    if (!dirHandle) return null;

    try {
        return await dirHandle.getFileHandle(fileName);
    } catch {
        // Case-insensitive fallback: search for matching entry
        const match = await findHandleCaseInsensitive(dirHandle, fileName, 'file');
        return match;
    }
}

// Case-insensitive fallback for resolving file/directory handles.
async function findHandleCaseInsensitive(dirHandle, name, kind) {
    const lowerName = name.toLowerCase();
    try {
        for await (const [entryName, handle] of dirHandle.entries()) {
            if (handle.kind === kind && entryName.toLowerCase() === lowerName) {
                return handle;
            }
        }
    } catch (e) {
        // Directory may not be readable
    }
    return null;
}

// Convert a simple file pattern to regex.
function patternToRegex(pattern) {
    if (!pattern || pattern === '*') {
        return /^.+$/;
    }
    // Special case: *.* means "must have at least one dot"
    if (pattern === '*.*') {
        return /^.+\..+$/;
    }
    const escaped = pattern
        .replace(/[.+^${}()|[\]\\]/g, '\\$&')
        .replace(/\*/g, '.*')
        .replace(/\?/g, '.');
    return new RegExp('^' + escaped + '$', 'i');
}

// Recursively walk a directory, collecting files matching pattern.
async function walkDirectory(dirHandle, relativePath, regex, results) {
    for await (const [name, handle] of dirHandle.entries()) {
        const childPath = relativePath ? relativePath + '/' + name : name;
        if (handle.kind === 'file' && regex.test(name)) {
            results.push(childPath);
        } else if (handle.kind === 'directory') {
            await walkDirectory(handle, childPath, regex, results);
        }
    }
}
