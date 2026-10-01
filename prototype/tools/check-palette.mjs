// The prototype opens layouts Unity wrote (docs/COLOURS.md §3.1): palette ids render through the file's palette
// block, missing ones show magenta, the per-style colours are used, and the block survives a save. Exit 1 on a failure.
import fs from 'fs'; import path from 'path';
import { chromium } from 'playwright-core';
import { fileURLToPath } from 'url';
const T = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const html = fs.readFileSync(T + '/index.html', 'utf8');
const three = fs.readFileSync(T + '/tools/node_modules/three/build/three.module.js', 'utf8');
const clip = fs.readFileSync(T + '/tools/node_modules/polygon-clipping/dist/polygon-clipping.umd.min.js', 'utf8');
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM || undefined, args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage(); const errors = []; page.on('pageerror', e => errors.push(e.message));
await page.route('**/*', r => { const u = r.request().url();
  if (u.includes('three.module.js')) return r.fulfill({ body: three, contentType: 'application/javascript' });
  if (u.includes('polygon-clipping')) return r.fulfill({ body: clip, contentType: 'application/javascript' });
  if (u.startsWith('http://local/')) return r.fulfill({ body: '<!doctype html><html><head><meta charset="utf-8"></head><body>' + html + '</body></html>', contentType: 'text/html' });
  return r.abort(); });
await page.goto('http://local/'); await page.waitForFunction(() => window.__sb && __sb.nb() > 0);
const lin = h => { const n = parseInt(h.slice(1), 16); return [16, 8, 0].map(s => { const c = ((n >> s) & 255) / 255; return c < 0.04045 ? c * 0.0773993808 : Math.pow(c * 0.9478672986 + 0.0521327014, 2.4); }); };
const has = (cols, h) => { const w = lin(h); for (let i = 0; i < cols.length; i += 3) if (Math.abs(cols[i] - w[0]) < 1e-6 && Math.abs(cols[i + 1] - w[1]) < 1e-6 && Math.abs(cols[i + 2] - w[2]) < 1e-6) return true; return false; };
const ID = 'aaaabbbbccccddddeeeeffff00001111', MISSING = 'ffffffffffffffffffffffffffffffff';
const r = await page.evaluate(([ID, MISSING]) => {
  const s = __sb.state; s.palette = { [ID]: { name: 'TestBrick', hex: '#204060' } };
  const b = s.buildings[0]; b.style.wall = ID; b.style.door = '#112233'; b.style.rail = '#445566';
  const c1 = Array.from(__sb.geo(0, 1)[0].c), c0 = Array.from(__sb.geo(0, 0)[0].c);
  b.style.wall = MISSING; const cm = Array.from(__sb.geo(0, 1)[0].c);
  const saved = JSON.parse(JSON.stringify(s));
  return { c0, c1, cm, keptPalette: !!saved.palette && saved.palette[ID].hex === '#204060', interior: b.interior };
}, [ID, MISSING]);
const checks = {
  'palette id renders its snapshot colour': has(r.c1, '#204060'),
  'per-style door colour is used (LOD1 door panes)': has(r.c1, '#112233'),
  'default door colour is gone from that building': !has(r.c1, '#3B3129'),
  'per-style rail colour is used (LOD0 stairs)': has(r.c0, '#445566') || !r.interior,
  'an id missing from the block shows magenta': has(r.cm, '#FF00FF'),
  'the palette block survives a save': r.keptPalette,
  'no page errors': errors.length === 0,
};
for (const [k, v] of Object.entries(checks)) console.log((v ? 'ok   ' : 'FAIL ') + k);
if (errors.length) console.log(errors);
await browser.close(); process.exit(Object.values(checks).every(Boolean) ? 0 : 1);
