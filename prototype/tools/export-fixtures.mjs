// Reference answers for the Unity port, emitted by the implementation that works.
//
// Loads the prototype headlessly, builds the demo street and the 85-building variant
// set (the same set the coplanar-face checker runs over), and writes into the given
// folder:
//
//   demo.json      the demo street as the prototype saves it (state v2)
//   variants.json  the variant set: every footprint preset × window style, setbacks,
//                  overhangs, roofed terraces, pitched roofs, flush cores, details
//   derived.json   per building: floor bases, roof height, tier starts, outline per
//                  floor, style resolution per floor, shaft tops, triangles per LOD
//
// The C# tests read these; nothing in them is written by hand.
//
//   npm install            (once, in prototype/tools)
//   npm run fixtures       -> ../../unity/Fixtures
import { chromium } from 'playwright-core';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.resolve(process.argv[2] || path.join(here, '../../unity/Fixtures'));
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

const demo = await page.evaluate(() => JSON.stringify(__sb.state, null, 1));

// The variant set. Kept identical to the coplanar checker's so the two suites judge one corpus.
await page.evaluate(() => {
  const src = __sb.state.buildings[0];
  const FP = { rect: [[0, 0], [12, 0], [12, 9], [0, 9]], L: [[0, 0], [16, 0], [16, 8], [8, 8], [8, 16], [0, 16]], U: [[0, 0], [18, 0], [18, 14], [12, 14], [12, 6], [6, 6], [6, 14], [0, 14]], T: [[0, 0], [18, 0], [18, 7], [12, 7], [12, 15], [6, 15], [6, 7], [0, 7]], oct: [[3.5, 0], [8.5, 0], [12, 3.5], [12, 8.5], [8.5, 12], [3.5, 12], [0, 8.5], [0, 3.5]], skew: [[0, 0], [14, 2], [11, 10], [3, 12]] };
  let x = 60; const wins = ['punched', 'tall', 'ribbon', 'curtain', 'none']; const grounds = ['storefront', 'match', 'solid']; let j = 0;
  __sb.state.buildings.length = 0;
  for (const [name, fp] of Object.entries(FP)) for (const win of wins) {
    const b = JSON.parse(JSON.stringify(src)); b.id = 't' + (j++); b.name = name + '/' + win; b.pos = { x: x += 30, z: 60 }; b.footprint = fp.map(([a, c]) => ({ x: a, z: c }));
    b.floors = b.floors.slice(0, 3).map(() => ({ walls: [] })); b.shafts = []; b.style.windows = win; b.style.ground = grounds[j % 3]; b.style.parapet = j % 2 === 0; b.style.bands = true;
    b.entrances = fp.map((_, e) => ({ edge: e, t: 0.3 })); b.details = []; b.interior = j % 4 !== 0;
    if (name === 'rect') b.shafts = [{ id: 'zs' + j, type: 'stairs', x: 1.3, z: 6.4, rot: 0, bottom: 0, top: -1, roof: true }, { id: 'zl' + j, type: 'lift', x: 10.8, z: 7.8, rot: 0, bottom: 0, top: -1, roof: false }];
    __sb.state.buildings.push(b);
  }
  const O = __sb.outline;
  const T = () => __sb.state.buildings.filter(b => String(b.id).startsWith('t'));
  for (const t of T().filter((_, i) => i % 5 === 0)) for (const two of [false, true]) {
    const b = JSON.parse(JSON.stringify(t)); b.id = 's' + (j++); b.name = t.name + (two ? '/setback2' : '/setback'); b.shafts = []; b.pos = { x: b.pos.x, z: b.pos.z + (two ? 90 : 45) };
    b.floors = [0, 1, 2, 3, 4, 5].map(() => ({ walls: [] })); b.style.windows = ['punched', 'tall', 'ribbon', 'curtain', 'none'][j % 5];
    b.floors[2].shape = two ? O.insetPoly(b.footprint, 1.6) : O.pushEdge(b.footprint, 2, -1.6); b.floors[2].blank = []; b.entrances.push({ edge: 1, t: 0.5, k: 2 });
    if (!two && Math.hypot(b.footprint[1].x - b.footprint[0].x, b.footprint[1].z - b.footprint[0].z) >= 10) b.details = [{ kind: 'escape', k: 0, edge: 0, t: 0.75 }];
    if (two) { const sh = O.insetPoly(b.floors[2].shape, 1.4); b.floors[4].shape = sh; b.floors[4].blank = []; if (O.outlineIssue(b, 4, sh)) { delete b.floors[4].shape; delete b.floors[4].blank; } }
    __sb.state.buildings.push(b);
  }
  for (const t of T().filter((_, i) => i % 5 === 0)) {
    const b = JSON.parse(JSON.stringify(t)); b.id = 'o' + (j++); b.name = t.name + '/overhang'; b.pos = { x: b.pos.x, z: b.pos.z + 135 };
    b.floors = [0, 1, 2, 3, 4].map(() => ({ walls: [] })); b.floors[1].h = 4.2; b.floors[2].shape = O.pushEdge(b.footprint, 0, 2); b.floors[2].blank = []; b.floors[2].style = { ...b.style, windows: 'curtain', wall: '#C3CCD0', bands: true };
    if (O.outlineIssue(b, 2, b.floors[2].shape)) { delete b.floors[2].shape; delete b.floors[2].style; } __sb.state.buildings.push(b);
  }
  for (const b of T().filter((_, i) => i % 5 === 2)) b.details = [{ kind: 'escape', k: 0, edge: 0, t: 0.7 }, { kind: 'awning', k: 0, edge: 0, t: 0.2 }, { kind: 'awning', k: 0, edge: 0, t: 0.8 }, { kind: 'dish', k: 1, edge: 1, t: 0.5, y: 1.9 }, { kind: 'vent', k: 2, edge: 2, t: 0.3, y: 0.4 }, { kind: 'ac', k: 1, edge: 2, t: 0.7, y: 0.3 }];
  let qj = 0; for (const t of __sb.state.buildings.filter(b => String(b.id).startsWith('s'))) {
    const b = JSON.parse(JSON.stringify(t)); b.id = 'q' + (j++); b.name = t.name + '/roofed'; b.pos = { x: b.pos.x, z: b.pos.z + 300 };
    for (const k of [2, 4]) if (b.floors[k].shape) b.floors[k].terraceRoof = { pitch: [20, 30, 45][qj++ % 3] }; __sb.state.buildings.push(b);
  }
  let rj = 0; for (const t of T().filter((_, i) => i % 5 === 1)) for (const rt of ['hip', 'gable', 'shed']) {
    const b = JSON.parse(JSON.stringify(t)); b.id = 'r' + (j++); b.name = t.name + '/' + rt; b.pos = { x: b.pos.x, z: b.pos.z + 180 + ['hip', 'gable', 'shed'].indexOf(rt) * 40 };
    b.style.roofType = rt; b.style.pitch = rt === 'shed' ? 14 : [25, 35, 45][rj % 3]; b.style.eave = [0.35, 0, 0.8][rj % 3]; b.interior = rj % 2 === 0; rj++; __sb.state.buildings.push(b);
  }
  __sb.outline.refresh();
});
const variants = await page.evaluate(() => JSON.stringify(__sb.state, null, 1));

// Derived values for both corpora, computed by the prototype's own functions.
const derive = async () => { const n = await page.evaluate(() => __sb.nb()); const r = []; for (let i = 0; i < n; i++) r.push(await page.evaluate(i => __sb.derive(i), i)); return r; };
const derivedVariants = await derive();
await page.evaluate(d => { __sb.state.buildings.length = 0; __sb.state.buildings.push(...JSON.parse(d).buildings); __sb.outline.refresh(); }, demo);
const derivedDemo = await derive();
await browser.close();

fs.mkdirSync(out, { recursive: true });
const write = (name, obj) => { fs.writeFileSync(path.join(out, name), typeof obj === 'string' ? obj : JSON.stringify(obj, null, 1) + '\n'); console.log('wrote', path.join(out, name)); };
write('demo.json', demo);
write('variants.json', variants);
write('derived.json', {
  schema: 1,
  source: 'prototype/index.html: floorBase, roofY, tierStart, fpAt, styleAt, shaftTop, and the LOD builders',
  tolerance: { absolute: 1e-6 },
  note: 'Identity, counts and tier starts are compared exactly; heights and outline coordinates to the tolerance. Triangle counts per LOD are targets for the generator (workstream 2), not part of the data-model tests.',
  demo: derivedDemo,
  variants: derivedVariants,
});
