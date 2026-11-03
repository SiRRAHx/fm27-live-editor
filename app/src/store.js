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
    // The clubs you manage: players are never moved out of them.
    myClubs: Array.isArray(s.myClubs) ? s.myClubs : [],
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
