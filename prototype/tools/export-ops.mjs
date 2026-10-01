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

// Interior: stairs, lifts and the wall graph (docs/EDITOR.md slice 6.2). A case is a list of steps, as a tool would
// make them; the last step's return value is recorded, and the building after (queries record none). Steps that are
// handlers in the prototype (dragging a core or a joint, the angle field, Align to wall, the floor range fields)
// are replayed here line for line from index.html, and named after the handler.
const icases = [];
for (const corpus of ['demo', 'variants']) {
  const doc = fs.readFileSync(path.join(fixtures, corpus + '.json'), 'utf8');
  const got = await page.evaluate(([doc, corpus]) => {
    __sb.state.buildings.length = 0; __sb.state.buildings.push(...JSON.parse(doc).buildings); __sb.outline.refresh();
    __sb.ops.reindex();
    const O = __sb.ops, B = __sb.state.buildings, ui = __sb.ui, clone = o => JSON.parse(JSON.stringify(o)), r = [];
    const pristine = B.map(clone);
    const reset = i => { for (const key of Object.keys(B[i])) delete B[i][key]; Object.assign(B[i], clone(pristine[i])); };
    const cm = v => Math.round(v * 100) / 100, P = (x, z) => ({ x, z }), pt = q => [q.x, q.z];
    let seed = 1;
    const rnd = () => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; };
    // the steps a case can take; each returns what the prototype's operation returned, made plain
    const step = {
      snapPoint: (b, [k, p, o]) => { const q = O.snapPoint(b, k, P(...p), { from: o.from && P(...o.from), anchors: (o.anchors || []).map(a => P(...a)), ex: new Set(o.ex || []), exW: new Set(o.exW || []), free: !!o.free });
        return { x: q.x, z: q.z, kind: q.kind, wi: q.wi ?? null, guides: q.guides.map(pt) }; },
      placementAt: (b, [k, p, type, placeRot]) => { ui.tool = type; ui.placeRot = placeRot; const { it, ok } = O.placementAt(b, k, P(...p)); ui.tool = 'select'; ui.placeRot = 0; return { x: it.x, z: it.z, rot: it.rot, ok }; },
      doorAt: (b, [k, p, extOnly]) => { const d = O.doorAt(b, k, P(...p), extOnly); if (!d) return null; const { w, ...rest } = d; return rest; },
      hoverTarget: (b, [k, p]) => { const t = O.hoverTarget(b, k, P(...p)); if (!t) return null; return t.kind === 'shaft' ? { kind: 'shaft', si: b.shafts.indexOf(t.s) } : t; },
      wallAngle: (b, [k, p]) => O.wallAngle(O.fpAt(b, k), ...p),
      snapCore: (b, [k, type, p, rot]) => { const q = O.snapCore(O.fpAt(b, k), { type, x: p[0], z: p[1], rot }, p[0], p[1], rot); return [q.x, q.z]; },
      itemsOverlap: (b, [si, sj, gap]) => O.itemsOverlap(b.shafts[si], b.shafts[sj], gap),
      addWall: (b, [k, s, q]) => { O.addWall(b, k, P(...s), P(...q)); },
      removeJoint: (b, [k, key]) => O.removeJoint(b, k, key) ?? null,
      // the overlay's joint drag: move every wall end at the joint, then connect and clean (buildWallOverlay)
      dragJoint: (b, [k, key, p, free]) => { const f = b.floors[k], n = O.wallNodes(f.walls).find(x => O.ptKey(f.walls[x.refs[0][0]][x.refs[0][1]]) === key); if (!n) return false;
        const refs = n.refs.map(([wi, end]) => [f.walls[wi], end]), anchors = refs.map(([w, end]) => w[end === 'a' ? 'b' : 'a']);
        const ex = new Set(), exW = new Set(); for (const [w, end] of refs) { const wi = f.walls.indexOf(w); ex.add(wi + ':' + end); exW.add(wi); }
        const q = O.snapPoint(b, k, P(...p), { ex, exW, anchors, free }); for (const [w, end] of refs) { w[end].x = q.x; w[end].z = q.z; }
        const at = refs[0][0][refs[0][1]]; O.connectAt(f, at, new Set(refs.map(x => x[0]))); O.cleanWalls(f); return true; },
      // the overlay's "+" handle: split wall wi at its middle and drag the new joint
      splitDrag: (b, [k, wi, p, free]) => { const f = b.floors[k], x = f.walls[wi], mid = P((x.a.x + x.b.x) / 2, (x.a.z + x.b.z) / 2);
        O.splitWall(f, wi, mid); const w1 = f.walls[wi], w2 = f.walls[wi + 1], refs = [[w1, 'b'], [w2, 'a']], anchors = [w1.a, w2.b];
        const ex = new Set(), exW = new Set(); for (const [w, end] of refs) { const j = f.walls.indexOf(w); ex.add(j + ':' + end); exW.add(j); }
        const q = O.snapPoint(b, k, P(...p), { ex, exW, anchors, free }); for (const [w, end] of refs) { w[end].x = q.x; w[end].z = q.z; }
        O.connectAt(f, refs[0][0][refs[0][1]], new Set(refs.map(y => y[0]))); O.cleanWalls(f); },
      // the Door tool's click (toolDown)
      toggleDoor: (b, [k, p]) => { const d = O.doorAt(b, k, P(...p)); if (!d) return false;
        if (d.kind === 'int') { const w = b.floors[k].walls[d.wi]; if (d.di >= 0) w.doors.splice(d.di, 1); else w.doors.push({ t: d.t }); }
        else { if (d.ei >= 0) b.entrances.splice(d.ei, 1); else b.entrances.push(d.k ? { edge: d.edge, t: d.t, k: d.k } : { edge: d.edge, t: d.t }); } return true; },
      // the Erase tool's click
      erase: (b, [k, p]) => { const t = O.hoverTarget(b, k, P(...p)); if (!t) return false; O.eraseTarget(b, k, t); return true; },
      // the Stairs / Lift tool's click (toolUp), with the new core's id given
      placeCore: (b, [k, p, type, placeRot, id]) => { ui.tool = type; ui.placeRot = placeRot; const { it, ok } = O.placementAt(b, k, P(...p)); ui.tool = 'select'; ui.placeRot = 0;
        if (!ok) return false; b.shafts.push({ id, type: it.type, x: it.x, z: it.z, rot: it.rot, bottom: k, top: -1, roof: it.type === 'stairs' }); return true; },
      // the Select tool's core drag (toolMove), one move
      dragCore: (b, [si, p0, p]) => { const it = b.shafts[si], off = P(it.x - p0[0], it.z - p0[1]); let nx = cm(p[0] + off.x), nz = cm(p[1] + off.z);
        { const sn = O.snapCore(O.fpAt(b, it.bottom), it, nx, nz, it.rot); if (O.fitsLevels(b, it, it.bottom, __sb.ops.shaftTopOf(b, it), sn.x, sn.z) && !b.shafts.some(s => s !== it && O.itemsOverlap(s, it, 0.3, sn.x, sn.z))) { nx = sn.x; nz = sn.z; } }
        if (O.fitsLevels(b, it, it.bottom, __sb.ops.shaftTopOf(b, it), nx, nz) && !b.shafts.some(s => s !== it && O.itemsOverlap(s, it, 0.3, nx, nz))) { if (nx !== it.x || nz !== it.z) { it.x = nx; it.z = nz; return true; } } return false; },
      // setCoreAngle, the angle field
      setAngle: (b, [si, deg]) => { const sh = b.shafts[si], rr = Math.round((((deg % 360) + 360) % 360) * 10) / 10; if (!O.shaftFits(b, sh, sh.x, sh.z, rr) || b.shafts.some(x => x !== sh && O.itemsOverlap(x, { ...sh, rot: rr }))) return false; sh.rot = rr; return true; },
      // Align to wall (shAlign), through setCoreAngle
      alignCore: (b, [si]) => { const sh = b.shafts[si], wa = O.wallAngle(O.fpAt(b, sh.bottom), sh.x, sh.z); return step.setAngle(b, [si, wa + Math.round(((sh.rot || 0) - wa) / 90) * 90]); },
      // the floor range fields (data-sh)
      setRange: (b, [si, field, v]) => { const sh = b.shafts[si]; if (field === 'bottom') { sh.bottom = v; if (sh.top >= 0 && sh.top < v) sh.top = v; } else { sh.top = v; if (v >= 0 && v < sh.bottom) sh.bottom = v; } },
    };
    const QUERIES = new Set(['snapPoint', 'placementAt', 'doorAt', 'hoverTarget', 'wallAngle', 'snapCore', 'itemsOverlap']);
    const run = (i, name, steps) => {
      reset(i); let ret = null;
      for (const [op, args] of steps) { ret = step[op](B[i], args); if (ret === undefined) ret = null; }
      const last = steps[steps.length - 1][0];
      const same = JSON.stringify(B[i]) === JSON.stringify(pristine[i]);   // most refused edits change nothing: say so instead of storing the building
      r.push({ corpus, building: i, name, steps: steps.map(([op, args]) => ({ op, args })), ret: clone(ret), after: QUERIES.has(last) ? null : same ? 'unchanged' : clone(B[i]) });
      reset(i);
    };
    pristine.forEach((b0, i) => {
      if (corpus === 'variants' && i % 3 !== 0) return;
      seed = 7 + i * 131 + (corpus === 'demo' ? 0 : 50000);
      const N = b0.floors.length, ks = [...new Set([0, Math.floor(N / 2), N - 1])];
      for (const k of ks) {
        const fp = O.fpAt(b0, k), xs = fp.map(q => q.x), zs = fp.map(q => q.z), x0 = Math.min(...xs) - 1, x1 = Math.max(...xs) + 1, z0 = Math.min(...zs) - 1, z1 = Math.max(...zs) + 1;
        const walls = b0.floors[k].walls, pts = [];
        for (let j = 0; j < 10; j++) pts.push([cm(x0 + rnd() * (x1 - x0)), cm(z0 + rnd() * (z1 - z0))]);
        for (const w of walls.slice(0, 6)) { pts.push([cm(w.a.x + 0.2), cm(w.a.z - 0.1)]); pts.push([cm((w.a.x + w.b.x) / 2 + 0.1), cm((w.a.z + w.b.z) / 2 + 0.15)]); }
        for (const v of fp.slice(0, 4)) pts.push([cm(v.x + 0.25), cm(v.z + 0.3)]);
        pts.forEach((p, j) => {
          run(i, 'snap', [['snapPoint', [k, p, {}]]]);
          if (j % 2 === 0) run(i, 'snap-from', [['snapPoint', [k, p, { from: pts[(j + 1) % pts.length] }]]]);
          if (j % 3 === 0) run(i, 'snap-anchors', [['snapPoint', [k, p, { anchors: [pts[(j + 2) % pts.length], pts[(j + 3) % pts.length]] }]]]);
          if (j % 4 === 0 && walls.length) run(i, 'snap-excluding', [['snapPoint', [k, p, { ex: ['0:a'], exW: [0] }]]]);
          if (j % 5 === 0) run(i, 'snap-free', [['snapPoint', [k, p, { free: true }]]]);
          run(i, 'place-stairs', [['placementAt', [k, p, 'stairs', j % 2 ? 90 : 0]]]);
          run(i, 'place-lift', [['placementAt', [k, p, 'lift', j % 3 ? 0 : 270]]]);
          run(i, 'door-at', [['doorAt', [k, p, false]]]);
          if (j % 2 === 0) run(i, 'door-at-ext', [['doorAt', [k, p, true]]]);
          run(i, 'hover', [['hoverTarget', [k, p]]]);
          if (j % 2 === 1) run(i, 'wall-angle', [['wallAngle', [k, p]]]);
          if (j % 3 === 1) run(i, 'snap-core', [['snapCore', [k, j % 2 ? 'lift' : 'stairs', p, [0, 30, 90, 180][j % 4]]]]);
          run(i, 'toggle-door', [['toggleDoor', [k, p]]]);
          if (j % 2 === 0) run(i, 'erase', [['erase', [k, p]]]);
          if (j % 3 === 0) run(i, 'place-core', [['placeCore', [k, p, j % 2 ? 'lift' : 'stairs', j % 4 === 0 ? 0 : 90, 'zz' + j]]]);
          if (j < pts.length - 1) {
            // draw a wall the way the Wall tool does: both ends snapped, the second from the first
            const s = step.snapPoint(b0, [k, p, {}]), q = step.snapPoint(b0, [k, pts[j + 1], { from: [s.x, s.z] }]);
            if (Math.hypot(q.x - s.x, q.z - s.z) > 0.5) run(i, 'add-wall', [['addWall', [k, [s.x, s.z], [q.x, q.z]]]]);
          }
        });
        // a chain of walls, then the middle joint removed or dragged
        if (pts.length >= 4) {
          const c = [pts[0], pts[1], pts[2], pts[3]];
          const chain = [['addWall', [k, c[0], c[1]]], ['addWall', [k, c[1], c[2]]], ['addWall', [k, c[2], c[3]]]];
          const key = Math.round(c[1][0] * 100) + ',' + Math.round(c[1][1] * 100);
          run(i, 'chain', chain);
          run(i, 'chain-remove-joint', [...chain, ['removeJoint', [k, key]]]);
          run(i, 'chain-drag-joint', [...chain, ['dragJoint', [k, key, [c[1][0] + 0.7, c[1][1] - 0.4], false]]]);
          run(i, 'chain-straight-join', [['addWall', [k, c[0], [(c[0][0] + c[1][0]) / 2, (c[0][1] + c[1][1]) / 2]]], ['addWall', [k, [(c[0][0] + c[1][0]) / 2, (c[0][1] + c[1][1]) / 2], c[1]]], ['removeJoint', [k, Math.round((c[0][0] + c[1][0]) / 2 * 100) + ',' + Math.round((c[0][1] + c[1][1]) / 2 * 100)]]]);
        }
        const nodes = O.wallNodes(walls).slice(0, 5);
        for (const n of nodes) {
          const key = O.ptKey(walls[n.refs[0][0]][n.refs[0][1]]), at = walls[n.refs[0][0]][n.refs[0][1]];
          run(i, 'remove-joint', [['removeJoint', [k, key]]]);
          run(i, 'drag-joint', [['dragJoint', [k, key, [at.x + 0.62, at.z - 0.37], false]]]);
          run(i, 'drag-joint-free', [['dragJoint', [k, key, [at.x + 0.62, at.z - 0.37], true]]]);
        }
        walls.slice(0, 5).forEach((w, wi) => run(i, 'split-drag', [['splitDrag', [k, wi, [cm((w.a.x + w.b.x) / 2 + 0.9), cm((w.a.z + w.b.z) / 2 - 0.6)], false]]]));
      }
      b0.shafts.forEach((s, si) => {
        for (const [dx, dz] of [[0.3, 0.2], [-1.1, 0.4], [0.05, -0.08]]) run(i, 'drag-core', [['dragCore', [si, [s.x, s.z], [s.x + dx, s.z + dz]]]]);
        for (const deg of [90, 45, -30.25, 180]) run(i, 'set-angle', [['setAngle', [si, deg]]]);
        run(i, 'align-core', [['setAngle', [si, 33]], ['alignCore', [si]]]);
        for (let v = 0; v < N; v += Math.max(1, Math.floor(N / 3))) { run(i, 'range-bottom', [['setRange', [si, 'bottom', v]]]); run(i, 'range-top', [['setRange', [si, 'top', v]]]); }
        run(i, 'range-top-follow', [['setRange', [si, 'top', -1]]]);
        b0.shafts.forEach((_, sj) => { if (sj !== si) run(i, 'overlap', [['itemsOverlap', [si, sj, 0.3]]]); });
      });
    });
    return r;
  }, [doc, corpus]);
  icases.push(...got);
}

// Facade: details by hand, entrances, blank walls, style presets and setback styles, terrace or roof (slice 6.7, 6.4),
// and where new buildings go. A facade point is given as the tool's pick result would give it: edge i of tier k0,
// t along it, height y, storey k.
const fcases = [];
for (const corpus of ['demo', 'variants']) {
  const doc = fs.readFileSync(path.join(fixtures, corpus + '.json'), 'utf8');
  const got = await page.evaluate(([doc, corpus]) => {
    __sb.state.buildings.length = 0; __sb.state.buildings.push(...JSON.parse(doc).buildings); __sb.outline.refresh();
    __sb.ops.reindex();
    const O = __sb.ops, B = __sb.state.buildings, clone = o => JSON.parse(JSON.stringify(o)), r = [];
    const pristine = B.map(clone);
    const reset = i => { for (const key of Object.keys(B[i])) delete B[i][key]; Object.assign(B[i], clone(pristine[i])); };
    const QUERIES = new Set(['detailAt', 'entranceAt', 'drivesRoof']);
    const step = {
      detailAt: (b, [ed, kind]) => { const x = O.detailPlace(b, ed, kind); return { add: x.d ?? null, remove: x.ei ?? -1, error: x.err ?? null, box: x.box ?? null }; },
      toggleDetail: (b, [ed, kind]) => { const x = O.detailPlace(b, ed, kind); if (x.err) return x.err; if (x.ei >= 0) b.details.splice(x.ei, 1); else (b.details ??= []).push(x.d); return null; },
      entranceAt: (b, [ed]) => { const x = O.facadeDoor(b, ed); if (x.err) return { error: x.err }; const { w, ...d } = x.d; return { door: d }; },
      toggleEntrance: (b, [ed]) => { const x = O.facadeDoor(b, ed); if (x.err) return x.err; const d = x.d; if (d.ei >= 0) b.entrances.splice(d.ei, 1); else b.entrances.push(d.k ? { edge: d.edge, t: d.t, k: d.k } : { edge: d.edge, t: d.t }); return null; },
      toggleBlank: (b, [k, i]) => { const bl = O.blankAt(b, k), j = bl.indexOf(i); if (j >= 0) bl.splice(j, 1); else bl.push(i); },
      // the preset buttons (case 'style'); the style edited is the setback's own if it has one, else the building's
      applyPreset: (b, [k0, key]) => { const sp = k0 && b.floors[k0].style ? 'floors.' + k0 + '.style' : 'style', cur = sp === 'style' ? b.style : b.floors[k0].style, keep = { roofType: cur.roofType, pitch: cur.pitch, eave: cur.eave };
        const ns = { ...clone(O.STYLES[key]), preset: key, ...keep }; if (sp === 'style') b.style = ns; else b.floors[k0].style = ns; },
      giveOwnStyle: (b, [k0]) => { b.floors[k0].style = clone(O.styleAt(b, k0 - 1)); },
      matchBelow: (b, [k0]) => { delete b.floors[k0].style; },
      setTerraceRoof: (b, [k0, roof]) => { if (!!b.floors[k0].terraceRoof === roof) return; if (roof) b.floors[k0].terraceRoof = { pitch: 30 }; else delete b.floors[k0].terraceRoof; },
      // renderPanel's drivesRoof
      drivesRoof: (b, [k0]) => { let j0 = O.tierStart(b, b.floors.length); while (j0 && !b.floors[j0].style) j0 = O.tierStart(b, j0 - 1); const own = k0 && b.floors[k0].style; return k0 === j0 || (!own && !!k0 && j0 === 0); },
    };
    const run = (i, name, steps) => {
      reset(i); let ret = null;
      for (const [op, args] of steps) { ret = step[op](B[i], args); if (ret === undefined) ret = null; }
      const last = steps[steps.length - 1][0], same = JSON.stringify(B[i]) === JSON.stringify(pristine[i]);
      r.push({ corpus, building: i, name, steps: steps.map(([op, args]) => ({ op, args })), ret: clone(ret), after: QUERIES.has(last) ? null : same ? 'unchanged' : clone(B[i]) });
      reset(i);
    };
    const KINDS = ['ac', 'vent', 'dish', 'escape', 'awning'];
    pristine.forEach((b0, i) => {
      if (corpus === 'variants' && i % 3 !== 0) return;
      for (const t of O.tiers(b0)) {
        const fp = O.fpAt(b0, t.k0), ks = [...new Set([t.k0, t.k1 - 1])];
        for (const k of ks) for (let e = 0; e < Math.min(fp.length, corpus === 'demo' ? 8 : 3); e++) {
          const a = fp[e], c = fp[(e + 1) % fp.length], L = Math.hypot(c.x - a.x, c.z - a.z) || 1;
          for (const tt of [0.18, 0.5, 0.83]) for (const yo of [0.45, 1.5]) {
            const ed = { i: e, t: tt, L, y: O.floorBase(b0, k) + yo, k0: t.k0, k, d: 0 };
            for (const kind of KINDS) run(i, 'detail-' + kind, [['detailAt', [ed, kind]]]);
            if (yo === 0.45) {
              run(i, 'entrance-at', [['entranceAt', [ed]]]);
              run(i, 'toggle-entrance', [['toggleEntrance', [ed]]]);
              run(i, 'toggle-detail', [['toggleDetail', [ed, KINDS[(e + Math.round(tt * 10)) % 5]]]]);
              run(i, 'toggle-detail-twice', [['toggleDetail', [ed, 'ac']], ['toggleDetail', [ed, 'ac']]]);
            }
          }
          run(i, 'toggle-blank', [['toggleBlank', [k, e]]]);
        }
        run(i, 'drives-roof', [['drivesRoof', [t.k0]]]);
        for (const key of Object.keys(O.STYLES)) run(i, 'apply-preset', [['applyPreset', [t.k0, key]]]);
        if (t.k0) {
          run(i, 'give-own-style', [['giveOwnStyle', [t.k0]]]);
          run(i, 'own-style-preset', [['giveOwnStyle', [t.k0]], ['applyPreset', [t.k0, 'glass']]]);
          run(i, 'own-style-drives-roof', [['giveOwnStyle', [t.k0]], ['drivesRoof', [t.k0]]]);
          run(i, 'match-below', [['matchBelow', [t.k0]]]);
          run(i, 'terrace-roof', [['setTerraceRoof', [t.k0, true]]]);
          run(i, 'terrace-again', [['setTerraceRoof', [t.k0, false]]]);
        }
      }
    });
    // where new buildings go: free spots around a few points, and the building a preset makes there
    const spots = [];
    for (const [x, z] of [[0, 0], [20, -5], [-30, 4], [200, 200]]) for (const [w, d] of [[12, 9], [18, 14], [6, 8]]) spots.push({ near: [x, z], w, d, at: [O.freeSpot.length, 0] });
    const freeSpots = spots.map(s => { O.camEdit.target.set(s.near[0], 0, s.near[1]); const p = O.freeSpot(s.w, s.d); return { near: s.near, w: s.w, d: s.d, spot: [p.x, p.z] }; });
    const seq0 = __sb.state.seq, made = [];
    for (const shape of Object.keys(O.FOOTPRINTS)) for (const st of Object.keys(O.STYLES)) made.push({ shape, style: st, seq: __sb.state.seq, building: clone(O.makeBuilding(shape, { x: 3, z: 4 }, st)) });
    __sb.state.seq = seq0;
    return { cases: r, freeSpots, made, names: O.nextName() };
  }, [doc, corpus]);
  fcases.push(...got.cases.map(c => c));
  if (corpus === 'demo') { fcases.freeSpots = got.freeSpots; fcases.made = got.made; fcases.nextName = got.names; }
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

const iout = path.join(path.dirname(out), 'ops-interior.json');
const icounts = {}; for (const c of icases) icounts[c.name] = (icounts[c.name] || 0) + 1;
fs.writeFileSync(iout, JSON.stringify({
  schema: 1,
  source: 'prototype/index.html: stairs, lifts, the wall graph, doors and erase (window.__sb.ops), and the tool handlers replayed in export-ops.mjs',
  note: 'Each case starts from the corpus building as stored in demo.json or variants.json, with every other building in place. after is null for queries and "unchanged" when the building is as it started.',
  counts: icounts,
  cases: icases,
}) + '\n');
console.log('wrote', iout, icases.length, 'cases', icounts);

const fout = path.join(path.dirname(out), 'ops-facade.json');
const fcounts = {}; for (const c of fcases) fcounts[c.name] = (fcounts[c.name] || 0) + 1;
fs.writeFileSync(fout, JSON.stringify({
  schema: 1,
  source: 'prototype/index.html: detailPlace, facadeDoor, blankAt, styleAt, freeSpot, makeBuilding (window.__sb.ops), and the Facade tab handlers replayed in export-ops.mjs',
  note: 'Cases as in ops-interior.json. freeSpots and made are from the demo street: where freeSpot puts a w x d footprint near a point, and what makeBuilding builds (seq is the document counter before).',
  counts: fcounts,
  freeSpots: fcases.freeSpots,
  made: fcases.made,
  nextName: fcases.nextName,
  cases: fcases,
}) + '\n');
console.log('wrote', fout, fcases.length, 'cases', fcounts);
