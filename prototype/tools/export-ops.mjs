// Reference answers for the Unity port's edit operations (docs/EDITOR.md §1), emitted by the prototype.
//
// Loads the prototype headlessly with the demo street and the variant set from unity/Fixtures, runs every
// floor, outline and setback operation on every building (each case on a fresh copy, neighbours in place for
// the snaps), and writes ops.json: per case the corpus, the building, the operation and its arguments, what it
// returned, and the building afterwards.
//
//   npm run ops            -> ../../unity/Fixtures/ops.json
import { chromium } from 'playwright-core';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const fixtures = path.resolve(here, '../../unity/Fixtures');
const out = path.resolve(process.argv[2] || path.join(fixtures, 'ops.json'));
const html = fs.readFileSync(path.join(here, '../index.html'), 'utf8');
const three = fs.readFileSync(path.join(here, 'node_modules/three/build/three.module.js'), 'utf8');
const clipping = fs.readFileSync(path.join(here, 'node_modules/polygon-clipping/dist/polygon-clipping.umd.min.js'), 'utf8');

const exe = process.env.CHROMIUM || undefined;
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

const cases = [];
for (const corpus of ['demo', 'variants']) {
  const doc = fs.readFileSync(path.join(fixtures, corpus + '.json'), 'utf8');
  const got = await page.evaluate(([doc, corpus]) => {
    __sb.state.buildings.length = 0; __sb.state.buildings.push(...JSON.parse(doc).buildings); __sb.outline.refresh();
    __sb.ops.reindex();   // the id lookup is cached and only rebuilt when the count changes, which the demo's does not
    const O = __sb.ops, B = __sb.state.buildings, clone = o => JSON.parse(JSON.stringify(o)), r = [];
    // runs fn on building i, records what it returned and the building after, then puts the building back
    const pristine = B.map(clone);
    // restored in place: the prototype looks buildings up by id, and a replaced object would leave the lookup on the old one
    const reset = i => { for (const key of Object.keys(B[i])) delete B[i][key]; Object.assign(B[i], clone(pristine[i])); };
    const run = (i, op, args, fn) => {
      reset(i);
      let ret = fn(B[i]);
      if (ret === undefined) ret = null;
      r.push({ corpus, building: i, op, args, ret: clone(ret), after: clone(B[i]) });
      reset(i);
    };
    const pt = q => [q.x, q.z];
    pristine.forEach((b0, i) => {
      if (corpus === 'variants' && i % 3 !== 0) return;   // a third of the variant set: every preset, setbacks, cores, details and roofs still occur
      const N = b0.floors.length;
      // floors
      for (const n of [1, N - 1, N + 3]) if (n >= 1) run(i, 'setFloorCount', [n], b => O.setFloorCount(b, n));
      run(i, 'addFloorTop', [], b => O.addFloorTop(b));
      for (let k = 0; k < N; k++) {
        run(i, 'deleteFloor', [k], b => O.deleteFloor(b, k));
        run(i, 'insertAbove', [k], b => O.insertAbove(b, k));
        run(i, 'copyLayoutUp', [k], b => { for (let j = k + 1; j < b.floors.length; j++) b.floors[j].walls = clone(b.floors[k].walls); });   // the floor card's handler
      }
      // outlines, per tier
      for (const t of O.tiers(b0)) {
        const k0 = t.k0, fp = O.tierFp(b0, k0), n = fp.length;
        for (let e = 0; e < n; e++) {
          run(i, 'insertVertex', [e, k0], b => O.insertVertex(b, e, k0));
          run(i, 'insertVertexSimplify', [e, k0], b => { O.insertVertex(b, e, k0); return O.simplifyTier(b, k0); });   // the midpoint merges back: mergeVertex
          run(i, 'removeVertex', [e, k0], b => O.removeVertex(b, e, k0));
          for (const d of [-1.3, 0.6, 3, -6]) run(i, 'pushEdge', [e, k0, d], b => { const nf = O.pushEdge(O.tierFp(b, k0), e, d); return { fp: nf.map(pt), issue: O.outlineIssue(b, k0, nf) }; });
          const v = fp[e];
          for (const [dx, dz] of [[0.3, -0.2], [-0.15, 0.38], [1.1, 0.7]]) run(i, 'outlineSnap', [k0, e, v.x + dx, v.z + dz], b => pt(O.outlineSnap(b, k0, O.tierFp(b, k0), e, v.x + dx, v.z + dz)));
        }
        run(i, 'insetPoly', [k0, 1.5], b => { const nf = O.insetPoly(O.tierFp(b, k0), 1.5); return { fp: nf.map(pt), issue: O.outlineIssue(b, k0, nf) }; });
        run(i, 'addSetback', [k0], b => O.addSetback(b, k0));
        if (k0) {
          run(i, 'removeSetback', [k0], b => O.removeSetback(b, k0));
          for (let k = 1; k < N; k++) if (k !== k0 && !b0.floors[k].shape) run(i, 'moveSetback', [k0, k], b => O.moveSetback(b, k0, k));
        }
      }
      for (const key of Object.keys(O.FOOTPRINTS)) run(i, 'applyShape', [key], b => O.applyShape(b, key));
      for (const [dx, dz] of [[0.3, 0], [-0.4, 0.2], [0, -0.45], [2, 2]]) run(i, 'snapMove', [b0.pos.x + dx, b0.pos.z + dz], b => O.snapMove(b, b0.pos.x + dx, b0.pos.z + dz));
    });
    return r;
  }, [doc, corpus]);
  cases.push(...got);
}
await browser.close();

const counts = {}; for (const c of cases) counts[c.op] = (counts[c.op] || 0) + 1;
fs.writeFileSync(out, JSON.stringify({
  schema: 1,
  source: 'prototype/index.html: the floor, outline and setback operations (window.__sb.ops); copyLayoutUp is the floor card handler',
  note: 'Each case starts from the corpus building as stored in demo.json or variants.json, with every other building in place.',
  counts,
  cases,
}) + '\n');
console.log('wrote', out, cases.length, 'cases', counts);
