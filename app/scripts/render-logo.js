// Renders build/logo.svg to PNG files for the app icon and the in-game button.
// Usage: electron scripts/render-logo.js <svg> <out.png@size | out.ico> ...
const { app, BrowserWindow } = require('electron');
const fs = require('fs');
const path = require('path');
const [svg, ...outs] = process.argv.slice(2).filter((a) => !a.startsWith('--') && a !== path.resolve(__filename) && !a.endsWith('render-logo.js'));
const log = (m) => console.log(m);
log('start ' + JSON.stringify(process.argv));
process.on('uncaughtException', (e) => { log(e.stack); app.exit(1); });
process.on('unhandledRejection', (e) => { log(String(e && e.stack)); app.exit(1); });
// A Windows .ico holding PNG images at the sizes Windows asks for.
function ico(png) {
  const sizes = [16, 24, 32, 48, 64, 128, 256];
  const images = sizes.map(png);
  const head = Buffer.alloc(6 + 16 * sizes.length);
  head.writeUInt16LE(1, 2);
  head.writeUInt16LE(sizes.length, 4);
  let offset = head.length;
  sizes.forEach((n, i) => {
    const e = 6 + 16 * i;
    head.writeUInt8(n % 256, e);
    head.writeUInt8(n % 256, e + 1);
    head.writeUInt16LE(1, e + 4);
    head.writeUInt16LE(32, e + 6);
    head.writeUInt32LE(images[i].length, e + 8);
    head.writeUInt32LE(offset, e + 12);
    offset += images[i].length;
  });
  return Buffer.concat([head, ...images]);
}

app.whenReady().then(async () => {
  const data = 'data:image/svg+xml;base64,' + fs.readFileSync(svg).toString('base64');
  // Draw it once, large, then scale down for each file.
  const w = new BrowserWindow({ width: 512, height: 512, show: false, frame: false, transparent: true, useContentSize: true });
  await w.loadURL('data:text/html,' + encodeURIComponent(`<body style="margin:0;background:transparent;overflow:hidden"><img src="${data}" width="512" height="512" style="display:block">`));
  await new Promise((r) => setTimeout(r, 500));
  const img = await w.webContents.capturePage();
  const png = (n) => img.resize({ width: n, height: n, quality: 'best' }).toPNG();
  for (const o of outs) {
    const [file, size] = o.split('@');
    fs.writeFileSync(file, file.endsWith('.ico') ? ico(png) : png(+size));
    log('wrote ' + file);
  }
  app.quit();
});
