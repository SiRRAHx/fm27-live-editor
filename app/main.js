const { app, BrowserWindow, ipcMain, shell, protocol, net, dialog } = require('electron');
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const graphics = require('./src/graphics');
const store = require('./src/store');
const addon = require('./src/addon');
const { GROUPS, POSITIONS, SQUAD_STATUSES, label } = require('./src/attributes');

// --demo uses made-up players, so the window can be tried without FM27.
const demo = process.argv.includes('--demo');
const game = demo ? require('./src/demo') : require('./src/game');

let win;

// Our own taskbar identity, so Windows shows our icon rather than Electron's.
const AppId = 'com.local.fm27liveeditor';
app.setAppUserModelId(AppId);

function createWindow() {
  win = new BrowserWindow({
    width: 1180,
    height: 800,
    minWidth: 900,
    minHeight: 560,
    backgroundColor: '#061410',
    title: 'FM27 Live Editor',
    icon: path.join(__dirname, 'build', 'icon.ico'),
    autoHideMenuBar: true,
    // Our own title bar area blends into the app; Windows draws the buttons.
    titleBarStyle: 'hidden',
    titleBarOverlay: { color: '#00000000', symbolColor: '#a7bdb2', height: 38 },
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });
  win.loadFile(path.join(__dirname, 'renderer', 'index.html'));
}

ipcMain.handle('info', () => ({
  version: app.getVersion(),
  demo,
  groups: GROUPS.map((g) => ({ ...g, labels: Object.fromEntries(g.keys.map((k) => [k, label(k)])) })),
  positions: POSITIONS,
  squadStatuses: SQUAD_STATUSES,
}));
ipcMain.handle('settings', () => store.getSettings());
ipcMain.handle('save-settings', (_e, patch) => store.saveSettings(patch));
ipcMain.handle('status', () => game.status());
ipcMain.handle('search', (_e, q) => game.search(q));
ipcMain.handle('player', (_e, id) => game.player(id));
ipcMain.handle('save-player', (_e, id, changes) => game.savePlayer(id, changes));
ipcMain.handle('search-clubs', (_e, q) => game.searchClubs(q));
ipcMain.handle('club', (_e, id) => game.club(id));
ipcMain.handle('save-club', (_e, id, changes) => game.saveClub(id, changes));
ipcMain.handle('save-squad', (_e, id, values) => game.saveSquad(id, values));
ipcMain.handle('transfer-status', () => game.transferStatus());
ipcMain.handle('transfer', (_e, action, req) => game.transfer(action, req));
ipcMain.handle('open-url', (_e, url) => /^https:\/\//.test(url) && shell.openExternal(url));
ipcMain.handle('addon-status', () => addon.status());
ipcMain.handle('addon-install', async (_e, folder) => {
  if (!folder) {
    const pick = await dialog.showOpenDialog(win, { title: 'Find your FM27 folder (the one with fm.exe)', properties: ['openDirectory'] });
    if (pick.canceled || !pick.filePaths[0]) return addon.status();
    folder = pick.filePaths[0];
  }
  return addon.install(folder);
});
ipcMain.handle('addon-on', (_e, on) => addon.setOn(on));
ipcMain.handle('addon-remove', () => addon.remove());

// The in-game "Live Editor" button opens a shortcut to this copy of the app in
// Documents\FM27 Live Editor. (Game Pass FM runs sandboxed and can't see links
// like fm27editor://, but it can read Documents.) Keep to one window: a second
// launch just brings the open window to the front.
function writeLaunchShortcut() {
  const dir = path.join(app.getPath('documents'), 'FM27 Live Editor');
  const args = process.defaultApp ? `"${path.resolve(process.argv[1] || '.')}"` : '';
  try {
    fs.mkdirSync(dir, { recursive: true });
    shell.writeShortcutLink(path.join(dir, 'Open FM27 Live Editor.lnk'), {
      target: process.execPath, args, cwd: path.dirname(process.execPath),
      icon: process.defaultApp ? path.join(__dirname, 'build', 'icon.ico') : process.execPath, iconIndex: 0,
      appUserModelId: AppId,
      description: 'Open FM27 Live Editor',
    });
  } catch (e) { console.error('Couldn\'t write the launch shortcut:', e); }
}
if (!app.requestSingleInstanceLock()) app.quit();
else if (!demo) writeLaunchShortcut();
app.on('second-instance', () => {
  if (!win) return;
  if (win.isMinimized()) win.restore();
  win.show();
  win.focus();
});

// Faces and badges: fmimg://person/<uid> and fmimg://club/<uid>, using FM's
// unique IDs. They come from the player's graphics packs; without one, a
// plain silhouette or shield, like FM shows.
protocol.registerSchemesAsPrivileged([{ scheme: 'fmimg', privileges: { standard: true, secure: true } }]);
const PLACEHOLDER = {
  person: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><circle cx="50" cy="36" r="19" fill="#3d5a4f"/><path d="M14 100c0-22 16-36 36-36s36 14 36 36z" fill="#3d5a4f"/></svg>',
  club: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><path d="M50 8 86 20v28c0 22-15 37-36 44C29 85 14 70 14 48V20z" fill="none" stroke="#3d5a4f" stroke-width="7"/></svg>',
};
async function image(req) {
  const { hostname: kind, pathname, searchParams } = new URL(req.url);
  const uid = pathname.slice(1);
  if (!(kind in PLACEHOLDER) || !/^\d+$/.test(uid)) return new Response('', { status: 404 });
  // ?alt= is a second ID to try, e.g. the men's club for a women's club badge.
  const alt = /^\d+$/.test(searchParams.get('alt') || '') ? searchParams.get('alt') : null;
  const file = demo ? null : (await graphics.find(kind, uid)) || (alt && await graphics.find(kind, alt));
  if (file) return net.fetch(pathToFileURL(file).toString());
  return new Response(PLACEHOLDER[kind], { headers: { 'content-type': 'image/svg+xml', 'cache-control': 'no-store' } });
}

// The engine does the editing: it attaches to FM27 from outside and answers on 127.0.0.1.
// It's started with the app and stops when the app closes (it watches our process id).
let engine;
function startEngine() {
  const exe = app.isPackaged
    ? path.join(process.resourcesPath, 'engine', 'FM27LiveEditor.Engine.exe')
    : path.join(__dirname, '..', 'engine', 'bin', 'out', 'FM27LiveEditor.Engine.exe');
  if (!fs.existsSync(exe)) { console.error('The editor engine is missing:', exe); return; }
  engine = spawn(exe, ['--parent', String(process.pid)], { windowsHide: true, stdio: 'ignore' });
  engine.on('exit', (code) => { if (code) console.error('The editor engine stopped with code', code); engine = null; });
}
app.on('will-quit', () => { if (engine) engine.kill(); });

app.whenReady().then(() => {
  protocol.handle('fmimg', image);
  if (!demo) startEngine();
  if (!demo) graphics.load();
  createWindow();
});
app.on('window-all-closed', () => app.quit());
