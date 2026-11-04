// Creates the GitHub release for the current version before electron-builder
// uploads to it. electron-builder uploads several files at once and each tries
// to create a missing release, so the second one fails and the rest never upload.
// Run via `npm run release` with GH_TOKEN set.
const { version, build } = require('../package.json');

const { owner, repo } = build.publish[0];
const token = process.env.GH_TOKEN;
const tag = `v${version}`;
const api = `https://api.github.com/repos/${owner}/${repo}`;
const headers = {
  Authorization: `Bearer ${token}`,
  Accept: 'application/vnd.github+json',
  'User-Agent': 'fm27-live-editor-release',
};

// The release page text: this version's "What's new" lines and how to install.
function releaseNotes(v = version) {
  const lines = require('../src/changelog.json')[v] || [];
  return [
    '**Early access.** Please back up your saves and report anything odd in Issues.',
    '',
    ...(lines.length ? ["## What's new", ...lines.map((l) => `- ${l}`), ''] : []),
    '## Install',
    `Download **FM27-Live-Editor-Setup-${v}.exe** below and run it. If Windows shows "Windows protected your PC", click **More info → Run anyway** (the app isn't code-signed).`,
    'The installer asks where FM27 is (the folder with fm.exe). Then start FM27 and load your career; the editor connects by itself.',
    '',
    'Already have 0.3.1 or later? The editor offers this update itself when it starts.',
    '',
    '## Supported',
    '- Game Pass / Microsoft Store: tested.',
    "- Steam and Epic: supported, not yet confirmed by a tester. If the editor says your FM27 build isn't supported, please open an Issue and attach `%LOCALAPPDATA%\\FM27 Live Editor\\engine.log`.",
    '',
    'Edits are kept only if you save in FM.',
  ].join('\n');
}

async function main() {
  if (!token) throw new Error('Set GH_TOKEN first: $env:GH_TOKEN = "<your token>"');
  const existing = await fetch(`${api}/releases/tags/${tag}`, { headers });
  if (existing.ok) {
    console.log(`Release ${tag} already exists; uploading to it.`);
    return;
  }
  if (existing.status !== 404) throw new Error(`GitHub said ${existing.status} when looking for ${tag}.`);
  const res = await fetch(`${api}/releases`, {
    method: 'POST',
    headers,
    body: JSON.stringify({ tag_name: tag, name: `FM27 Live Editor ${version} (early access)`, body: releaseNotes(), draft: false, prerelease: true }),
  });
  if (!res.ok) throw new Error(`Couldn't create release ${tag}: ${res.status} ${await res.text()}`);
  console.log(`Created release ${tag}.`);
}

if (require.main !== module) module.exports = { releaseNotes };
else main().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
