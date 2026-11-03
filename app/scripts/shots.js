// Screenshots of the demo window for checking the layout: electron scripts/shots.js <outDir>
// Opens the Contract tab of the first demo player, then a club on the Clubs page.
const { app, BrowserWindow } = require('electron');
const fs = require('fs');
const path = require('path');

const out = path.resolve(process.argv[2] || '.');
process.argv.push('--demo');
require('../main.js');

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
app.on('browser-window-created', (_e, win) => {
  win.webContents.once('did-finish-load', async () => {
    const run = (js) => win.webContents.executeJavaScript(js);
    const shot = async (name) => fs.writeFileSync(path.join(out, name), (await win.webContents.capturePage()).toPNG());
    try {
      await wait(1500);
      await run(`document.querySelector('.result').click()`);
      await wait(400);
      await run(`document.querySelector('[data-pane=contract]').click()`);
      await wait(300);
      await shot('contract.png');
      await run(`document.querySelector('[data-pane=status]').click()`);
      await wait(300);
      await shot('status.png');
      await run(`document.querySelector('[data-tab=clubs]').click()`);
      await wait(300);
      await run(`document.querySelector('#club-results .result').click()`);
      await wait(400);
      await shot('club.png');
      await run(`document.querySelector('[data-tab=transfers]').click()`);
      const type = async (sel, text) => {
        await run(`(() => { const i = document.querySelector('${sel} input'); i.value = '${text}'; i.dispatchEvent(new Event('input')); })()`);
        await wait(600);
        await run(`document.querySelector('${sel} .pick-results .result').click()`);
        await wait(200);
      };
      await type('#my-club-pick', 'River');
      await type('#t-player', 'Striker');
      await type('#t-club', 'River');
      await shot('transfers.png');
      console.log('saved to', out);
    } catch (e) { console.error(e); }
    app.exit(0);
  });
});
