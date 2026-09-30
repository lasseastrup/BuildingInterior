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

// Face census of the LOD0 opaque mesh, everything included. Triangles are bucketed by plane (normal to 1e-3, offset to 1 mm) and colour
// (linear, to 1e-3); each bucket records its area and triangle count. Triangulation-independent, so the C# generator
// is judged on what surface it produces where, not on how it splits it.
// LOD0 and LOD1 buckets carry the colour. LOD2 has no colours in the mesh: its quads carry a bay width, a kind and a
// parameter row (colours, window spec, storey height, bands, run height, parapet) the facade shader reads, so its
// buckets carry those instead, and the window span (fac.zw) of the quad.
const census = async (lod) => {
  const n = await page.evaluate(() => __sb.nb()); const out = [];
  for (let i = 0; i < n; i++) out.push(await page.evaluate(([i, lod]) => {
    const b = __sb.state.buildings[i];
    const g = __sb.geo(i, lod)[0];
    const B = new Map(); let tris = 0;
    for (let t = 0; t < g.i.length; t += 3) {
      const ids = [g.i[t], g.i[t + 1], g.i[t + 2]], P = ids.map(j => [g.p[3 * j], g.p[3 * j + 1], g.p[3 * j + 2]]), v0 = ids[0];
      const nn = [g.n[3 * v0], g.n[3 * v0 + 1], g.n[3 * v0 + 2]];
      const ux = P[1][0] - P[0][0], uy = P[1][1] - P[0][1], uz = P[1][2] - P[0][2], vx = P[2][0] - P[0][0], vy = P[2][1] - P[0][1], vz = P[2][2] - P[0][2];
      const cr = [uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx], cl = Math.hypot(...cr) || 1, area = cl / 2;
      // the plane offset from the triangle's own (exact) normal, not the stored one: LOD2 stores Int8 normals, and with
      // a normal that is not quite perpendicular the offset would depend on which vertex the triangulation put first
      let gn = cr.map(v => v / cl); if (gn[0] * nn[0] + gn[1] * nn[1] + gn[2] * nn[2] < 0) gn = gn.map(v => -v);
      const d = gn[0] * P[0][0] + gn[1] * P[0][1] + gn[2] * P[0][2];
      let key, extra;
      if (lod < 2) { const c = [g.c[3 * v0], g.c[3 * v0 + 1], g.c[3 * v0 + 2]]; key = [nn.map(v => v.toFixed(3)).join(','), d.toFixed(3), c.map(v => v.toFixed(3)).join(',')].join('|'); extra = { c: c.map(v => +v.toFixed(3)) }; }
      else { const f2 = [g.fac2[2 * v0], g.fac2[2 * v0 + 1]], span = [g.fac[4 * v0 + 2], g.fac[4 * v0 + 3]], pr = g.params[g.slot[v0]].map(v => +v.toFixed(3));
        extra = { kind: f2[1], bay: +f2[0].toFixed(3), span: span.map(v => +v.toFixed(3)), params: pr }; key = [nn.map(v => v.toFixed(3)).join(','), d.toFixed(3), JSON.stringify(extra)].join('|'); }
      const e = B.get(key) || B.set(key, { n: nn.map(v => +v.toFixed(3)), d: +d.toFixed(3), ...extra, area: 0, tris: 0 }).get(key);
      e.area += area; e.tris++; tris++;
    }
    return { id: b.id, tris, verts: g.p.length / 3, buckets: [...B.values()].map(e => ({ ...e, area: +e.area.toFixed(5) })).sort((p, q) => p.d - q.d || p.area - q.area) };
  }, [i, lod]));
  return out;
};
const censusDemo = [await census(0), await census(1), await census(2)];
await page.evaluate(d => { __sb.state.buildings.length = 0; __sb.state.buildings.push(...JSON.parse(d).buildings); __sb.outline.refresh(); }, variants);
const censusVariants = [await census(0), await census(1), await census(2)];
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
for (const lod of [0, 1, 2]) write(`census${lod}.json`, {
  schema: 2, lod,
  source: `prototype/index.html: buildLOD${lod}` + (lod === 0 ? ' (opaque mesh)' : ''),
  tolerance: { area: 0.002, areaRelative: 0.001 },
  note: lod < 2
    ? 'Per building: triangles by plane and colour with the area they cover. A bucket missing, extra, or off by more than the tolerance is a face the generator put somewhere else.'
    : 'Per building: triangles by plane, quad kind, bay width, window span and the parameter row the facade shader reads (wall, trim, glass, roof colours; window spec; storey height, banded storeys, run height, parapet). Normals are the mesh\'s Int8 ones.',
  demo: censusDemo[lod],
  variants: censusVariants[lod],
});
