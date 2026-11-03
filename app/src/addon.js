// The optional in-game button: BepInEx (the mod loader) plus our small add-on, copied into the
// FM27 folder. The editor works without it; it only adds the Live Editor button inside FM.
// "Turn off" renames BepInEx's winhttp.dll so FM starts without any mods (e.g. if FM's
// anti-tamper check ever objects), and "Turn on" puts it back.
const { app } = require('electron');
const { execFile } = require('child_process');
const fs = require('fs');
const path = require('path');
const store = require('./store');

const resources = () => (app.isPackaged ? process.resourcesPath : path.join(__dirname, '..', '..'));
const bundle = () => path.join(resources(), app.isPackaged ? 'bepinex' : path.join('vendor', 'bepinex'));
const addonDll = () => path.join(resources(), app.isPackaged ? path.join('addon', 'FM27LiveEditor.dll') : path.join('plugin', 'bin', 'out', 'FM27LiveEditor.dll'));

const isGame = (dir) => !!dir && fs.existsSync(path.join(dir, 'fm.exe')) && fs.existsSync(path.join(dir, 'fm_Data'));

// The path of the running fm.exe, or null.
function runningFm() {
  return new Promise((resolve) => {
    execFile('powershell.exe', ['-NoProfile', '-Command', '(Get-Process fm -ErrorAction SilentlyContinue | Select-Object -First 1).Path'],
      { windowsHide: true, timeout: 8000 }, (err, out) => resolve(!err && out.trim() ? out.trim() : null));
  });
}

// Where FM27 is: the folder picked before, the running game, or the usual Game Pass and Steam places.
async function findGame() {
  const saved = store.getSettings().gameFolder;
  if (isGame(saved)) return saved;
  const running = await runningFm();
  if (running && isGame(path.dirname(running))) {
    // Game Pass runs FM from a link under WindowsApps; the real (writable) folder is where it points.
    try { return fs.realpathSync.native(path.dirname(running)); } catch { return path.dirname(running); }
  }
  for (const drive of 'CDEFGHIJ') {
    for (const dir of ['XboxGames\\Football Manager 27\\Content', 'Games\\Football Manager 27\\Content',
      'Program Files (x86)\\Steam\\steamapps\\common\\Football Manager 27', 'SteamLibrary\\steamapps\\common\\Football Manager 27']) {
      const full = `${drive}:\\${dir}`;
      if (isGame(full)) return full;
    }
  }
  return null;
}

async function status() {
  const folder = await findGame();
  const fmRunning = !!(await runningFm());
  if (!folder) return { folder: null, fmRunning };
  const plugin = path.join(folder, 'BepInEx', 'plugins', 'FM27LiveEditor.dll');
  return {
    folder,
    fmRunning,
    installed: fs.existsSync(path.join(folder, 'BepInEx', 'core')) && fs.existsSync(plugin),
    on: fs.existsSync(path.join(folder, 'winhttp.dll')),
    off: fs.existsSync(path.join(folder, 'winhttp.dll.off')),
    available: fs.existsSync(bundle()) && fs.existsSync(addonDll()),
  };
}

async function notWhileRunning() {
  if (await runningFm()) throw new Error('Close FM27 first, then try again.');
}

async function install(folder) {
  if (!isGame(folder)) throw new Error("That folder doesn't have FM27 in it (fm.exe).");
  await notWhileRunning();
  if (!fs.existsSync(bundle()) || !fs.existsSync(addonDll())) throw new Error('This copy of the editor came without the in-game button files.');
  try {
    // BepInEx's own files; a BepInEx that's already there (maybe with other mods) is kept, only updated.
    fs.cpSync(bundle(), folder, { recursive: true, force: true });
    if (fs.existsSync(path.join(folder, 'winhttp.dll.off'))) fs.rmSync(path.join(folder, 'winhttp.dll.off'));
    fs.copyFileSync(addonDll(), path.join(folder, 'BepInEx', 'plugins', 'FM27LiveEditor.dll'));
  } catch (e) {
    throw new Error(e.code === 'EPERM' || e.code === 'EACCES'
      ? "Windows didn't let the editor write to the FM27 folder. On Game Pass, turn on mods for FM27 in the Xbox app (Manage → Advanced → enable mods), then try again."
      : `Couldn't copy the files (${e.message}).`);
  }
  store.saveSettings({ gameFolder: folder });
  return status();
}

async function setOn(on) {
  const folder = await findGame();
  if (!folder) throw new Error("Couldn't find FM27.");
  await notWhileRunning();
  const live = path.join(folder, 'winhttp.dll'), parked = live + '.off';
  if (on && fs.existsSync(parked)) fs.renameSync(parked, live);
  if (!on && fs.existsSync(live)) fs.renameSync(live, parked);
  return status();
}

// Takes out our add-on only; BepInEx stays, as other mods may use it.
async function remove() {
  const folder = await findGame();
  if (!folder) throw new Error("Couldn't find FM27.");
  await notWhileRunning();
  fs.rmSync(path.join(folder, 'BepInEx', 'plugins', 'FM27LiveEditor.dll'), { force: true });
  return status();
}

module.exports = { status, install, setOn, remove, isGame };
