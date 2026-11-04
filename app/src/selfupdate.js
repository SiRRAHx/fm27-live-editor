// App updates from GitHub Releases. The app checks on start (and when the
// user presses "Check for app updates"); if a newer version exists the window
// asks before downloading it, then offers to restart and install. Updates only
// work in the installed app, not when running from source or a portable .exe.
const { app, ipcMain } = require('electron');

let state = { state: 'idle' }; // latest status, for a window that opens later

function setupSelfUpdate(send) {
  const portable = !!process.env.PORTABLE_EXECUTABLE_DIR;
  const enabled = app.isPackaged && !portable;
  const report = (s) => {
    state = { ...state, ...s };
    send('app-update', state);
  };

  ipcMain.handle('app-info', () => ({ version: app.getVersion(), autoUpdates: enabled, update: state }));
  if (!enabled) {
    ipcMain.handle('check-app-update', () => ({ state: 'unavailable' }));
    ipcMain.handle('download-app-update', () => {});
    ipcMain.handle('restart-to-update', () => {});
    return;
  }

  const { autoUpdater } = require('electron-updater');
  autoUpdater.autoDownload = false; // ask first
  autoUpdater.allowPrerelease = true; // early-access versions are GitHub pre-releases
  autoUpdater.autoInstallOnAppQuit = true;
  autoUpdater.on('download-progress', (p) => report({ state: 'downloading', pct: p.percent / 100 }));
  autoUpdater.on('update-downloaded', (info) => report({ state: 'ready', version: info.version }));
  autoUpdater.on('error', () => {
    // A failed download goes back to "available" so it can be tried again.
    if (state.state === 'downloading') report({ state: 'available', error: 'The download failed. Try again later.' });
  });

  // Returns { state: 'available', version } | { state: 'none' } | { state: 'error', message }.
  async function check() {
    if (state.state === 'downloading' || state.state === 'ready') return state;
    try {
      const result = await autoUpdater.checkForUpdates();
      const latest = result?.updateInfo?.version;
      const newer = result?.isUpdateAvailable ?? (latest && latest !== app.getVersion());
      if (newer && latest) {
        report({ state: 'available', version: latest, current: app.getVersion(), error: null });
      } else {
        report({ state: 'none' });
      }
    } catch (e) {
      return { state: 'error', message: "Couldn't check for app updates. Check your internet connection and try again." };
    }
    return state;
  }

  ipcMain.handle('check-app-update', () => check());
  ipcMain.handle('download-app-update', () => {
    if (state.state !== 'available') return;
    report({ state: 'downloading', pct: 0, error: null });
    autoUpdater.downloadUpdate().catch(() => {}); // failures arrive as 'error' above
  });
  ipcMain.handle('restart-to-update', () => {
    // Silently, with the same install folder and settings, then the editor opens again.
    if (state.state === 'ready') autoUpdater.quitAndInstall(true, true);
  });

  // Check once the app has started; the window asks if there's an update.
  setTimeout(check, 3000);
}

module.exports = { setupSelfUpdate };
