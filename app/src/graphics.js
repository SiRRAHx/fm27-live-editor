// Finds player faces and club badges in FM's graphics folder (facepacks and
// logo packs such as Cutout Megapack and FMG Standard Logos). Each pack has a
// config.xml that maps its files to FM's unique IDs, e.g.
//   <record from="face_67296654" to="graphics/pictures/person/67296654/portrait"/>
// so we read those maps rather than guessing file names.
const fs = require('fs');
const path = require('path');
const os = require('os');

// FM keeps its data folder (saves, graphics) where the player chose, recorded in
// Preferences\version N\user_data_location.xml. The default is in Documents.
function dataFolder() {
  const prefs = path.join(process.env.LOCALAPPDATA || '', 'Sports Interactive', 'Football Manager 27', 'Preferences');
  try {
    for (const v of fs.readdirSync(prefs).filter((d) => d.startsWith('version')).sort().reverse()) {
      const xml = fs.readFileSync(path.join(prefs, v, 'user_data_location.xml'), 'utf8');
      const m = xml.match(/value="([^"]+)"/);
      if (m && fs.existsSync(m[1])) return m[1];
    }
  } catch { /* no preferences yet */ }
  return path.join(os.homedir(), 'Documents', 'Sports Interactive', 'Football Manager 27');
}

// Logo packs ship other styles beside "Normal"; FM only uses them when chosen in
// its settings. Backup copies of packs shouldn't win over the real one either.
const SKIP = /^(alternatives|fantasy|retro)$|\.backup/i;
const MAP = /from="([^"]+)"\s+to="graphics\/pictures\/(person|club)\/(\d+)\/(portrait|logo)"/g;

const files = { person: new Map(), club: new Map() };
let ready = null;

async function indexFolder(dir) {
  let entries;
  try { entries = await fs.promises.readdir(dir, { withFileTypes: true }); } catch { return; }
  for (const e of entries) {
    if (e.isDirectory() && !SKIP.test(e.name)) await indexFolder(path.join(dir, e.name));
  }
  if (!entries.some((e) => e.name === 'config.xml')) return;
  let xml;
  try { xml = await fs.promises.readFile(path.join(dir, 'config.xml'), 'utf8'); } catch { return; }
  for (const [, from, kind, uid] of xml.matchAll(MAP)) {
    if (!files[kind].has(uid)) files[kind].set(uid, path.join(dir, from));
  }
}

function load() {
  if (!ready) {
    const root = path.join(dataFolder(), 'graphics');
    const started = Date.now();
    ready = indexFolder(root).then(() => {
      console.log(`graphics: ${files.person.size} faces, ${files.club.size} badges in ${root} (${Date.now() - started} ms)`);
    });
  }
  return ready;
}

// The image file for a person's face or a club's badge, or null.
async function find(kind, uid) {
  await load();
  const base = files[kind]?.get(String(uid));
  if (!base) return null;
  for (const ext of ['.png', '.jpg', '.jpeg', '.tga', '.bmp']) {
    if (fs.existsSync(base + ext)) return base + ext;
  }
  return null;
}

module.exports = { load, find, dataFolder };
