const bookmarksKey = 'openutau_folder_bookmarks';
const recentPathsKey = 'openutau_recent_paths';
const DB_NAME = 'OpenUtauBookmarks';
const DB_VERSION = 1;
const STORE_NAME = 'handles';

// Initialize IndexedDB
function openDB() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION);
    
    request.onerror = () => reject(request.error);
    request.onsuccess = () => resolve(request.result);
    
    request.onupgradeneeded = (event) => {
      const db = event.target.result;
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME);
      }
    };
  });
}

// Recent paths (strings) - localStorage is fine for these
async function saveRecentPath(path, name) {
  const recentPaths = JSON.parse(localStorage.getItem(recentPathsKey) || '{}');
  recentPaths[name] = path;
  localStorage.setItem(recentPathsKey, JSON.stringify(recentPaths));
}

function getRecentPath(name) {
  const recentPaths = JSON.parse(localStorage.getItem(recentPathsKey) || '{}');
  return recentPaths[name] || null;
}

// Bookmarks (FileSystemDirectoryHandle) - must use IndexedDB
async function saveBookmark(folder, name) {
  try {
    const db = await openDB();
    const transaction = db.transaction(STORE_NAME, 'readwrite');
    const store = transaction.objectStore(STORE_NAME);
    
    // Store the handle directly (structured-cloneable)
    store.put(folder, name);
    
    await new Promise((resolve, reject) => {
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
    });
    
    console.log(`Bookmark '${name}' saved successfully`);
  } catch (e) {
    console.error('Failed to save bookmark:', e);
  }
}

async function getBookmark(name) {
  try {
    const db = await openDB();
    const transaction = db.transaction(STORE_NAME, 'readonly');
    const store = transaction.objectStore(STORE_NAME);
    
    return await new Promise((resolve, reject) => {
      const request = store.get(name);
      request.onsuccess = () => resolve(request.result || null);
      request.onerror = () => reject(request.error);
    });
  } catch (e) {
    console.error('Failed to get bookmark:', e);
    return null;
  }
}

async function getFolderFromBookmark(name) {
  try {
    const handle = await getBookmark(name);
    if (!handle) {
      console.log('No bookmark found');
      return null;
    }
    
    // Check current permission state first
    const permission = await handle.queryPermission({ mode: 'readwrite' });
    
    if (permission === 'granted') {
      // Permission persisted - use it directly!
      console.log(`Permission for '${name}' already granted`);
      return handle;
    }
    
    // Only request if not granted (requires user gesture)
    if (permission === 'prompt') {
      console.log(`Requesting permission for '${name}'`);
      const newPermission = await handle.requestPermission({ mode: 'readwrite' });
      if (newPermission === 'granted') {
        return handle;
      }
    }
    
    console.log(`Permission denied for '${name}'`);
    return null;
  } catch (e) {
    console.error('Failed to restore folder from bookmark:', e);
    return null;
  }
}

async function removeBookmark(name) {
  try {
    const db = await openDB();
    const transaction = db.transaction(STORE_NAME, 'readwrite');
    const store = transaction.objectStore(STORE_NAME);
    
    store.delete(name);
    
    await new Promise((resolve, reject) => {
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
    });
    
    console.log(`Bookmark '${name}' removed successfully`);
  } catch (e) {
    console.error('Failed to remove bookmark:', e);
  }
}

export {
  saveRecentPath,
  getRecentPath,
  saveBookmark,
  getBookmark,
  getFolderFromBookmark,
  removeBookmark
};