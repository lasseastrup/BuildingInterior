// Reference answers for the Unity port's play model and occlusion (docs/PLAY.md), emitted by the prototype.
//
// Loads the prototype headlessly with the demo street and the variant set from unity/Fixtures and writes play.json:
//   segs     every building's collision segments per storey (LOD0's)
//   surface  surfaceAt(x, z, y) at points over and around every building, at heights around every floor line
//   collide  collide(x, z, y) at points near the walls of every storey
//   loc      playerLoc() at the same points
//   hits     segHitsBuilding(b, p, q) for camera-to-player segments through and past each building
//   walks    play mode stepped at a fixed time step with held keys, from spots around the street: per frame the
//            player, the camera, the view uniforms, the occluders (slot, amounts, row) and the sliding walls
//
//   npm run play           -> ../../unity/Fixtures/play.json
import { chromium } from 'playwright-core';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const fixtures = path.resolve(here, '../../unity/Fixtures');
const out = path.resolve(process.argv[2] || path.join(fixtures, 'play.json'));
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

const result = { corpora: {} };
for (const corpus of ['demo', 'variants']) {
  const doc = fs.readFileSync(path.join(fixtures, corpus + '.json'), 'utf8');
  await page.evaluate(doc => { __sb.state.buildings.length = 0; __sb.state.buildings.push(...JSON.parse(doc).buildings); __sb.outline.refresh(); __sb.ops.reindex(); }, doc);
  await page.waitForFunction(() => __sb.play.ready());
  const got = await page.evaluate(corpus => {
    const S = __sb.play, O = __sb.ops, B = __sb.state.buildings;
    let seed = corpus === 'demo' ? 7 : 11;
    const rnd = () => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; };
    const r4 = v => Math.round(v * 1e4) / 1e4;   // sample points on a 0.1 mm grid, so both sides start from the same doubles
    const only = (i) => corpus === 'demo' || i % 3 === 0;
    const segs = [], surface = [], collide = [], loc = [], hits = [];
    B.forEach((b, i) => {
      if (!only(i)) return;
      segs.push({ building: i, segs: S.segs(i).map(st => st.map(s => [s.ax, s.az, s.bx, s.bz, s.r])) });
      const N = b.floors.length, xs = [], zs = [];
      for (const t of O.tiers(b)) for (const p of O.fpAt(b, t.k0)) { xs.push(p.x + b.pos.x); zs.push(p.z + b.pos.z); }
      const x0 = Math.min(...xs) - 1.5, x1 = Math.max(...xs) + 1.5, z0 = Math.min(...zs) - 1.5, z1 = Math.max(...zs) + 1.5;
      const heights = [];
      for (let k = 0; k <= N; k++) { const y = O.floorBase(b, k); heights.push(y, y + 0.35, y + 0.59, y - 0.5); if (k < N) heights.push(y + O.floorH(b, k) / 2 + 0.2); }
      const n = corpus === 'demo' ? 260 : 90;
      for (let j = 0; j < n; j++) {
        const x = r4(x0 + (x1 - x0) * rnd()), z = r4(z0 + (z1 - z0) * rnd()), y = r4(heights[Math.floor(rnd() * heights.length)]);
        surface.push([x, z, y, S.surfaceAt(x, z, y)]);
        const l = S.loc(x, y, z); loc.push([x, y, z, l ? l.id : null, l ? l.floor : null]);
      }
      // near walls: points within 0.6 m of a segment's line on that storey
      S.segs(i).forEach((st, k) => {
        if (!st.length) return;
        const y = r4(k < N ? O.floorBase(b, k) + 0.1 : O.floorBase(b, N) + 0.1);
        for (let j = 0; j < (corpus === 'demo' ? 16 : 5); j++) {
          const s = st[Math.floor(rnd() * st.length)], t = rnd(), ox = (rnd() - 0.5) * 1.2, oz = (rnd() - 0.5) * 1.2;
          const x = r4(s.ax + (s.bx - s.ax) * t + ox), z = r4(s.az + (s.bz - s.az) * t + oz);
          collide.push([x, z, y, ...S.collide(x, z, y)]);
        }
      });
      // camera-to-player segments: from 20 m out at camera height to points around and inside the building
      for (let j = 0; j < (corpus === 'demo' ? 40 : 12); j++) {
        const a = rnd() * Math.PI * 2, d = 14 + rnd() * 12, cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        const p = { x: r4(cx + Math.cos(a) * d), y: r4(4 + rnd() * 14), z: r4(cz + Math.sin(a) * d) };
        const q = { x: r4(x0 + (x1 - x0) * rnd()), y: r4(heights[Math.floor(rnd() * heights.length)] + 1), z: r4(z0 + (z1 - z0) * rnd()) };
        hits.push([i, p.x, p.y, p.z, q.x, q.y, q.z, S.segHits(i, p, q)]);
      }
    });
    return { segs, surface, collide, loc, hits };
  }, corpus);
  result.corpora[corpus] = got;
}

// walks, on the demo street only: from outside every walk-in building's street door, in through it and on, then a
// circle around the outside so the neighbours come in the way, under each buildings-in-the-way mode
{
  const doc = fs.readFileSync(path.join(fixtures, 'demo.json'), 'utf8');
  await page.evaluate(doc => { __sb.state.buildings.length = 0; __sb.state.buildings.push(...JSON.parse(doc).buildings); __sb.outline.refresh(); __sb.ops.reindex(); }, doc);
  await page.waitForFunction(() => __sb.play.ready());
  result.walks = await page.evaluate(() => {
    const S = __sb.play, O = __sb.ops, B = __sb.state.buildings, dt = 1 / 30, walks = [];
    S.hold(true);
    B.forEach((b, i) => {
      if (b.interior === false) return;
      const e = b.entrances.find(x => !x.k); if (!e) return;
      const fp = O.fpAt(b, 0), a = fp[e.edge], c = fp[(e.edge + 1) % fp.length];
      const L = Math.hypot(c.x - a.x, c.z - a.z), ux = (c.x - a.x) / L, uz = (c.z - a.z) / L;
      let nx = uz, nz = -ux;   // outward normal, whichever way the outline winds
      const px = a.x + (c.x - a.x) * e.t + b.pos.x, pz = a.z + (c.z - a.z) * e.t + b.pos.z;
      const cen = fp.reduce((s, p) => [s[0] + p.x / fp.length, s[1] + p.z / fp.length], [0, 0]);
      if ((cen[0] + b.pos.x - px) * nx + (cen[1] + b.pos.z - pz) * nz > 0) { nx = -nx; nz = -nz; }
      const sx = px + nx * 4, sz = pz + nz * 4;
      // the play camera looks along -(sin yaw, cos yaw): facing the door means yaw along the outward normal
      const yaw = Math.atan2(nx, nz);
      for (const mode of i < 2 ? ['sink', 'slice', 'cutout', 'fade'] : ['sink']) {
        S.enter(sx, 0, sz, yaw + 0.35, 0.78, 13, { outside: mode, cut: true, cutH: 1.0, baseH: 1.0, holeR: 2.4, assist: true });
        const frames = [];
        for (let f = 0; f < 150; f++) {
          const input = f < 70 ? { KeyW: true } : f < 110 ? { KeyD: true, yaw: yaw + 0.35 + (f - 70) * 0.05 } : { KeyS: true, ShiftLeft: true, yaw: yaw + 2.35 };
          frames.push({ input, ...S.step(dt, input) });
        }
        walks.push({ building: i, mode, start: [sx, 0, sz], cam: [yaw + 0.35, 0.78, 13], occ: { outside: mode, cut: true, cutH: 1.0, baseH: 1.0, holeR: 2.4, assist: true }, frames });
        S.exit();
      }
    });
    // climbs: from the foot of every switchback stair, up the left flight, across the mid landing, up the right flight,
    // and round again, so the player changes storey (the floor clip, the cutaway and the slab holes all move with them)
    B.forEach((b, i) => {
      if (b.interior === false) return;
      b.shafts.forEach((s, si) => {
        if (s.type !== 'stairs') return;
        const a = s.rot * Math.PI / 180, u = [Math.cos(a), Math.sin(a)], w = [-Math.sin(a), Math.cos(a)];
        const y0 = O.floorBase(b, s.bottom);
        const sx = b.pos.x + s.x + u[0] * -0.65 + w[0] * -2.1, sz = b.pos.z + s.z + u[1] * -0.65 + w[1] * -2.1;
        const yaw = Math.atan2(-w[0], -w[1]);   // looking up the flight
        S.enter(sx, y0, sz, yaw, 0.9, 11, { outside: 'sink', cut: true, cutH: 1.0, baseH: 1.0, holeR: 2.4, assist: false });
        const frames = [];
        for (let lap = 0; lap < 2; lap++) {
          for (let f = 0; f < 30; f++) { const input = { KeyW: true, yaw }; frames.push({ input, ...S.step(1 / 30, input) }); }   // up the left flight to the mid landing
          for (let f = 0; f < 9; f++) { const input = { KeyA: true, yaw }; frames.push({ input, ...S.step(1 / 30, input) }); }    // across to the right lane (+u: the camera's left here)
          for (let f = 0; f < 30; f++) { const input = { KeyS: true, yaw }; frames.push({ input, ...S.step(1 / 30, input) }); }   // up the right flight to the next floor
          for (let f = 0; f < 9; f++) { const input = { KeyD: true, yaw }; frames.push({ input, ...S.step(1 / 30, input) }); }    // back to the left lane
        }
        walks.push({ building: i, shaft: si, mode: 'sink', start: [sx, y0, sz], cam: [yaw, 0.9, 11], occ: { outside: 'sink', cut: true, cutH: 1.0, baseH: 1.0, holeR: 2.4, assist: false }, frames });
        S.exit();
      });
    });
    S.hold(false);
    return walks;
  });
}
await browser.close();
fs.writeFileSync(out, JSON.stringify(result, (k, v) => typeof v === 'number' && !Number.isInteger(v) ? Math.round(v * 1e7) / 1e7 : v));
const c = result.corpora;
console.log(`${out}: ${Object.values(c).reduce((n, x) => n + x.surface.length, 0)} surface, ${Object.values(c).reduce((n, x) => n + x.collide.length, 0)} collide, ${Object.values(c).reduce((n, x) => n + x.hits.length, 0)} hits, ${result.walks.length} walks of ${result.walks[0]?.frames.length} frames`);
