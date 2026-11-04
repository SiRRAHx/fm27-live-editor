// "What's new" after an update: the changelog lines for the versions since the one last opened.
const fs = require('fs');
const path = require('path');
const { app } = require('electron');
const store = require('./store');
const CHANGELOG = require('./changelog.json');

// Editors before 0.3.6 didn't keep the version last opened. The engine log they left behind shows
// the editor has run here before, so an update from one of them still gets the window and a fresh
// install doesn't. Checked when the app loads, before this run's engine writes the log.
const ranBefore = fs.existsSync(path.join(process.env.LOCALAPPDATA || '', 'FM27 Live Editor', 'engine.log'));

const parts = (v) => v.split('.').map(Number);
function newerThan(a, b) {
  const x = parts(a), y = parts(b);
  for (let i = 0; i < 3; i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) > (y[i] || 0);
  return false;
}

function whatsNew() {
  const current = app.getVersion();
  const seen = store.getSettings().lastSeenVersion;
  if (seen === current) return null;
  store.saveSettings({ lastSeenVersion: current });
  if (seen && !newerThan(current, seen)) return null;
  if (!seen && !ranBefore) return null;
  const versions = seen
    ? Object.keys(CHANGELOG).filter((v) => newerThan(v, seen) && !newerThan(v, current)).sort((a, b) => (newerThan(a, b) ? -1 : 1))
    : CHANGELOG[current] ? [current] : [];
  return versions.length ? { version: current, from: seen, changes: versions.map((v) => ({ version: v, items: CHANGELOG[v] })) } : null;
}

module.exports = { whatsNew };
