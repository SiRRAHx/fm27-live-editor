// "What's new" after an update: the changelog lines for the versions since the one last opened.
const fs = require('fs');
const path = require('path');
const { app } = require('electron');
const store = require('./store');
const CHANGELOG = require('./changelog.json');

// Editors before 0.3.6 didn't keep the version last opened. The engine log they left behind notes
// the version each time it started ("engine 0.3.1 starting"), so an update from one of them gets
// everything since and a fresh install gets nothing. Read when the app loads, before this run's
// engine writes to the log.
function lastLogged() {
  try {
    const file = path.join(process.env.LOCALAPPDATA || '', 'FM27 Live Editor', 'engine.log');
    const size = fs.statSync(file).size, len = Math.min(size, 1 << 20), buf = Buffer.alloc(len);
    const fd = fs.openSync(file, 'r');
    try { fs.readSync(fd, buf, 0, len, size - len); } finally { fs.closeSync(fd); }
    const runs = [...buf.toString('utf8').matchAll(/engine (\d+\.\d+\.\d+) starting/g)];
    return runs.length ? runs[runs.length - 1][1] : null;
  } catch { return null; }
}
const logged = lastLogged();

const parts = (v) => v.split('.').map(Number);
function newerThan(a, b) {
  const x = parts(a), y = parts(b);
  for (let i = 0; i < 3; i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) > (y[i] || 0);
  return false;
}

function whatsNew() {
  const current = app.getVersion();
  const stored = store.getSettings().lastSeenVersion;
  if (stored === current) return null;
  store.saveSettings({ lastSeenVersion: current });
  const seen = stored || logged;
  if (!seen || !newerThan(current, seen)) return null;
  // Every version since, newest first.
  const versions = Object.keys(CHANGELOG).filter((v) => newerThan(v, seen) && !newerThan(v, current)).sort((a, b) => (newerThan(a, b) ? -1 : 1));
  return versions.length ? { version: current, from: seen, changes: versions.map((v) => ({ version: v, items: CHANGELOG[v] })) } : null;
}

module.exports = { whatsNew };
