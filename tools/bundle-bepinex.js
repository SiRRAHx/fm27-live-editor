// Copies BepInEx (the mod loader the in-game button needs) out of a working FM27 install into
// vendor/bepinex, ready for the installer. Only BepInEx's own files are taken: what it generates
// on first run (interop, unity-libs, cache, config), logs and other mods are left behind.
// Usage: node tools/bundle-bepinex.js "E:\Games\Football Manager 27\Content"
const fs = require('fs');
const path = require('path');

const from = process.argv[2];
if (!from || !fs.existsSync(path.join(from, 'winhttp.dll'))) {
  console.error('Give the FM27 Content folder that has BepInEx installed (it contains winhttp.dll).');
  process.exit(1);
}
const to = path.join(__dirname, '..', 'vendor', 'bepinex');
fs.rmSync(to, { recursive: true, force: true });
for (const item of ['winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt', 'dotnet', path.join('BepInEx', 'core')]) {
  fs.cpSync(path.join(from, item), path.join(to, item), { recursive: true });
}
for (const empty of ['plugins', 'patchers']) fs.mkdirSync(path.join(to, 'BepInEx', empty), { recursive: true });
fs.copyFileSync(path.join(__dirname, '..', 'vendor', 'BepInEx-LICENSE.txt'), path.join(to, 'BepInEx', 'LICENSE.txt'));
console.log('BepInEx copied to', to);
