// Small JSON-file persistence for settings.
const fs = require('fs');
const path = require('path');
const { app } = require('electron');

const file = () => path.join(app.getPath('userData'), 'settings.json');

function getSettings() {
  let s = {};
  try { s = JSON.parse(fs.readFileSync(file(), 'utf8')); } catch {}
  return {
    background: s.background || 'carbon',
    theme: s.theme || 'emerald',
    // Where FM27 is installed, once the in-game button has been set up.
    gameFolder: typeof s.gameFolder === 'string' ? s.gameFolder : null,
    // The version last opened, for "What's new" after an update.
    lastSeenVersion: typeof s.lastSeenVersion === 'string' ? s.lastSeenVersion : null,
  };
}

function saveSettings(patch) {
  const next = { ...getSettings(), ...patch };
  fs.mkdirSync(path.dirname(file()), { recursive: true });
  fs.writeFileSync(file() + '.tmp', JSON.stringify(next, null, 2));
  fs.renameSync(file() + '.tmp', file());
  return next;
}

module.exports = { getSettings, saveSettings };
