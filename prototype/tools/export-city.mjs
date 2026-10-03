// The stress-test city as the prototype generates it, for the Unity port's TestCity (docs/CITY.md §1).
//
// Loads the prototype headlessly with its demo street, generates the 3,000-building test city, and writes a compact
// summary of every generated building: name, position, footprint, floors, heights, style, roof, entrance, blank wall
// and core. The C# test generates the same city from the same demo street and compares field by field.
//
//   npm run city           -> ../../unity/Fixtures/city.json
//   npm run fixtures       -> ../../unity/Fixtures
import { chromium } from 'playwright-core';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.resolve(process.argv[2] || path.join(here, "../../unity/Fixtures/city.json"));
const html = fs.readFileSync(path.join(here, '../index.html'), 'utf8');
const three = fs.readFileSync(path.join(here, 'node_modules/three/build/three.module.js'), 'utf8');
const clipping = fs.readFileSync(path.join(here, 'node_modules/polygon-clipping/dist/polygon-clipping.umd.min.js'), 'utf8');

const exe = process.env.CHROMIUM || undefined;   // Playwright finds its own browser when unset
const browser = await chromium.launch({ executablePath: exe, args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ viewport: { width: 800, height: 600 } });
page.on('pageerror', e => console.error('PAGEERROR: ' + e.message));
await page.route('**/*', r => {
  const u = r.request().url();
  if (u.includes('three.module.js')) return r.fulfill({ body: three, contentType: 'application/javascript' });
  if (u.includes('polygon-clipping')) return r.fulfill({ body: clipping, contentType: 'application/javascript' });
  if (u.startsWith('http://local/')) return r.fulfill({ body: '<!doctype html><html><head><meta charset="utf-8"></head><body>' + html + '</body></html>', contentType: 'text/html' });
  return r.abort();
});
await page.goto('http://local/');
await page.waitForFunction(() => window.__sb && __sb.nb() > 0);

const city = await page.evaluate(() => {
  __sb.city(3000);
  const r = v => Math.round(v * 1000) / 1000;
  return __sb.state.buildings.filter(b => b.gen).map(b => ({
    name: b.name, pos: [r(b.pos.x), r(b.pos.z)], fp: b.footprint.map(p => [r(p.x), r(p.z)]), floors: b.floors.length,
    gh: r(b.groundHeight), fh: r(b.floorHeight), style: b.style.preset, roof: b.style.roofType || 'flat', pitch: b.style.pitch ?? null,
    interior: b.interior !== false, shafts: b.shafts.length, door: r(b.entrances[0].t), blank: b.blank }));
});
fs.writeFileSync(out, JSON.stringify(city));
console.log('wrote ' + out + ' (' + city.length + ' buildings)');
await browser.close();
