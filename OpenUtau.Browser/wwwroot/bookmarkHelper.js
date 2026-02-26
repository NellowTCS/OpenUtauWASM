const bookmarksKey = 'openutau_folder_bookmarks';
const recentPathsKey = 'openutau_recent_paths';

async function saveRecentPath(path, name) {
  const recentPaths = JSON.parse(localStorage.getItem(recentPathsKey) || '{}');
  recentPaths[name] = path;
  localStorage.setItem(recentPathsKey, JSON.stringify(recentPaths));
}

function getRecentPath(name) {
  const recentPaths = JSON.parse(localStorage.getItem(recentPathsKey) || '{}');
  return recentPaths[name] || null;
}

async function saveBookmark(folder, name) {
  try {
    const bookmark = await folder.store();
    const bookmarks = JSON.parse(localStorage.getItem(bookmarksKey) || '{}');
    bookmarks[name] = bookmark;
    localStorage.setItem(bookmarksKey, JSON.stringify(bookmarks));
  } catch (e) {
    console.error('Failed to save bookmark:', e);
  }
}

function getBookmark(name) {
  const bookmarks = JSON.parse(localStorage.getItem(bookmarksKey) || '{}');
  return bookmarks[name] || null;
}

async function getFolderFromBookmark(bookmark) {
  try {
    return await window.showDirectoryPicker({ directory: true, startIn: bookmark });
  } catch (e) {
    return null;
  }
}

function removeBookmark(name) {
  const bookmarks = JSON.parse(localStorage.getItem(bookmarksKey) || '{}');
  delete bookmarks[name];
  localStorage.setItem(bookmarksKey, JSON.stringify(bookmarks));
}

export { saveRecentPath, getRecentPath, saveBookmark, getBookmark, getFolderFromBookmark, removeBookmark };
