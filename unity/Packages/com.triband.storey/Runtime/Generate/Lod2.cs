#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>One row of the LOD2 parameter table: what the facade shader reads for a run of storeys.</summary>
    public sealed class ParamRow
    {
        /// <summary>The tier's colours as swatches: wall, trim, glass (darkened, as LOD1's opaque panes) and roof.</summary>
        public Swatch wall, trim, glass, roof;
        /// <summary>Window spec: gap for full-width styles (&lt;0: fixed width), width, sill, head.</summary>
        public double[] spec = { -1, 0, 1, 0 };
        /// <summary>Storey height, storeys with a band, run height (1e9 = not the top), parapet 0 none / 1 wall / 2 curb.</summary>
        public double[] run = { 3, 0, 1e9, 0 };
        /// <summary>The 24 floats as the table stores them (six texels: wall, trim, glass, roof, spec, run), colours resolved to linear RGB.</summary>
        public double[] Texels(ColorResolver colors)
        {
            Rgb w = colors.Resolve(wall), t = colors.Resolve(trim), g = colors.Resolve(glass), r = colors.Resolve(roof);
            return new[] { w.r, w.g, w.b, 1, t.r, t.g, t.b, 1, g.r, g.g, g.b, 1, r.r, r.g, r.b, 1, spec[0], spec[1], spec[2], spec[3], run[0], run[1], run[2], run[3] };
        }

        /// <summary>
        /// The 24 floats the GPU table stores (docs/COLOURS.md §3.5): texel 0 is (colour row, wall, trim and glass
        /// shades), texel 1 (roof shade), texels 2 and 3 are unused, then spec and run as in <see cref="Texels"/>.
        /// The facade shader reads wall, trim, glass and roof from <paramref name="colorRow"/>, the row of the swatches' style.
        /// </summary>
        public double[] GpuTexels(int colorRow)
        {
            if (wall.slot != ColorSlot.Wall || trim.slot != ColorSlot.Trim || glass.slot != ColorSlot.Glass || roof.slot != ColorSlot.Roof
                || !trim.style.Equals(wall.style) || !glass.style.Equals(wall.style) || !roof.style.Equals(wall.style))
                throw new InvalidOperationException("a parameter row's colours must be the wall, trim, glass and roof of one style");
            return new[] { colorRow, wall.tone, trim.tone, glass.tone, roof.tone, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, spec[0], spec[1], spec[2], spec[3], run[0], run[1], run[2], run[3] };
        }
    }

    /// <summary>The LOD2 massing mesh: quads with facade parameters instead of colours.</summary>
    public sealed class Lod2Mesh
    {
        public readonly List<P3> P = new List<P3>();
        /// <summary>Normals quantised to Int8 (× 127), as the mesh stores them.</summary>
        public readonly List<P3> N = new List<P3>();
        /// <summary>Per vertex: u along the wall, height above the run's base, window span start and end.</summary>
        public readonly List<double> Fac = new List<double>();
        /// <summary>Per vertex: bay width, kind (1 facade, 2 roof, 3 trim, 4 plain wall).</summary>
        public readonly List<double> Fac2 = new List<double>();
        /// <summary>Per vertex: index into <see cref="Rows"/>.</summary>
        public readonly List<int> Slot = new List<int>();
        public readonly List<int> I = new List<int>();
        public readonly List<ParamRow> Rows = new List<ParamRow>();
        public int Tag;
        public int Tris => I.Count / 3;
        public int Verts => P.Count;
    }

    /// <summary>
    /// LOD2: massing. One quad per footprint edge for the height of each setback tier's storey runs,
    /// the parapets, the roof and terraces. Windows come from the facade shader, driven by the same
    /// numbers the geometry LODs use, so the LOD1 to LOD2 switch keeps every window in place.
    /// </summary>
    public static class Lod2
    {
        public static double[] SpecOf(Facade.WinSpec? ws) => ws != null ? new[] { ws.full ?? -1, ws.w, ws.y0, ws.y1 } : new[] { -1.0, 0, 1, 0 };

        public static Lod2Mesh Build(Site site, BuildingData b)
        {
            double T = Dim.T_EXT; int N = b.floors.Count; double ox = b.pos.x, oz = b.pos.z, top = Derived.RoofY(b);
            var stTop = Derived.StyleAt(b, N); bool pitched = Roofs.IsPitched(b); double yTop = pitched ? top : top + (stTop.parapet ? 1.1 : 0.3);
            var M = new Lod2Mesh { Tag = site.IndexOf(b) + 2 * Lod1.LOD_TAG };
            int slot = 0;
            var Z = new double[] { 0, 0, 0, 0 };
            site.partyMemo.Remove(b.id);
            var L0 = new Lod0.Shared(site, b);

            void Vtx(P3 p, P3 nr, double[] fac, double bay, double kind)
            {
                // JavaScript's Math.round (ties toward +∞), which the prototype's Int8 normals were made with
                static double Q(double v) => Math.Floor(v * 127 + 0.5);
                M.P.Add(p); M.N.Add(new P3(Q(nr.x), Q(nr.y), Q(nr.z)));
                M.Fac.AddRange(fac); M.Fac2.Add(bay); M.Fac2.Add(kind); M.Slot.Add(slot);
            }
            void Quad(P3[] q, P3 nr, double[][] fac, double bay, double kind)
            {
                bool flip = P3.Dot(P3.Cross(q[1] - q[0], q[2] - q[0]), nr) < 0; int bse = M.P.Count;
                for (int j = 0; j < 4; j++) Vtx(q[j], nr, fac[j], bay, kind);
                if (flip) M.I.AddRange(new[] { bse, bse + 2, bse + 1, bse, bse + 3, bse + 2 }); else M.I.AddRange(new[] { bse, bse + 1, bse + 2, bse, bse + 2, bse + 3 });
            }
            var ZZ = new[] { Z, Z, Z, Z };
            void Flat(List<List<Vec2>> rings, double y, double kind, bool down)
            {
                var v2 = new List<List<Vec2>>(); foreach (var r in rings) { var l = new List<Vec2>(r.Count); foreach (var q in r) l.Add(new Vec2(q.x + ox, q.z + oz)); v2.Add(l); }
                var tris = Triangulate.Shape(v2[0], v2.GetRange(1, v2.Count - 1));
                var all = new List<Vec2>(); foreach (var r in v2) all.AddRange(r);
                int bse = M.P.Count; var nr = new P3(0, down ? -1 : 1, 0);
                foreach (var p in all) Vtx(new P3(p.x, y, p.z), nr, Z, 0, kind);
                for (int t = 0; t < tris.Count; t += 3)
                {
                    int x = tris[t], yy = tris[t + 1], z = tris[t + 2]; var A = all[x]; var B = all[yy]; var D = all[z];
                    if ((((B.z - A.z) * (D.x - A.x) - (B.x - A.x) * (D.z - A.z)) < 0) != down) { int q = yy; yy = z; z = q; }
                    M.I.Add(bse + x); M.I.Add(bse + yy); M.I.Add(bse + z);
                }
            }
            void RoofPieces(List<RoofPart> parts)
            {
                foreach (var p in parts)
                {
                    if (p.Dormer) continue;   // massing: the roof's shape only
                    double K = p.Kind == RoofKind.Roof ? 2 : p.Kind == RoofKind.Wall ? 4 : 3;
                    var all = new List<P3>(p.Pts); foreach (var h in p.Holes) all.AddRange(h);
                    int bse = M.P.Count; foreach (var q in all) Vtx(q, p.N, Z, 0, K);
                    var tris = Triangulate.Planar(p.Pts, p.Holes, p.N);
                    for (int t = 0; t < tris.Count; t += 3)
                    {
                        int a = tris[t], c = tris[t + 1], d = tris[t + 2];
                        if (P3.Dot(P3.Cross(all[c] - all[a], all[d] - all[a]), p.N) < 0) { int x = c; c = d; d = x; }
                        M.I.Add(bse + a); M.I.Add(bse + c); M.I.Add(bse + d);
                    }
                }
            }
            int Row(Palette C, double[]? spec = null, double[]? run = null)
            {
                M.Rows.Add(new ParamRow { wall = C.wall, trim = C.trim, glass = C.glassDark, roof = C.roof, spec = spec ?? new[] { -1.0, 0, 1, 0 }, run = run ?? new[] { 3, 0, 1e9, 0 } });
                return M.Rows.Count - 1;
            }

            var tiers = Party.Tiers(b);
            for (int ti = 0; ti < tiers.Count; ti++)
            {
                var t = tiers[ti];
                var fp = Derived.OutlineAt(b, t.k0); var st = Derived.StyleAt(b, t.k0); var blank = t.k0 > 0 ? b.floors[t.k0].blank : b.blank;
                var g = L0.At(t.k0); int n = fp.Count; bool last = t.k1 == N; var C = g.C;
                int tierSlot = Row(C); slot = tierSlot;
                if (t.k0 > 0)
                {
                    double y0 = Derived.FloorBase(b, t.k0), yb = y0 - Dim.SLAB;
                    foreach (var poly in Geo.TerracePolys(fp, Derived.OutlineAt(b, t.k0 - 1))) Flat(poly, yb, 4, true);
                    foreach (var r in L0.OverhangRuns(t.k0))
                    {
                        var F = new Frame(r.a, r.u, r.w); double s0 = r.S.s, e0 = r.E.s, s1 = r.S.At(T), e1 = r.E.At(T);
                        Quad(new[] { F.At(s1, yb, T), F.At(e1, yb, T), F.At(e1, y0, T), F.At(s1, y0, T) }, new P3(r.w.x, 0, r.w.z), ZZ, 0, 4);
                        Quad(new[] { F.At(s0, yb, 0), F.At(e0, yb, 0), F.At(e1, yb, T), F.At(s1, yb, T) }, new P3(0, -1, 0), ZZ, 0, 4);
                    }
                }
                var runs = new List<(int k0, int k1, double h, bool g, int slot, bool top)>();
                for (int k = t.k0; k < t.k1; k++)
                {
                    double h = Derived.FloorH(b, k); bool gnd = k == 0;
                    if (runs.Count > 0 && !runs[runs.Count - 1].g && !gnd && Math.Abs(runs[runs.Count - 1].h - h) < 1e-6) { var r = runs[runs.Count - 1]; r.k1 = k + 1; runs[runs.Count - 1] = r; }
                    else runs.Add((k, k + 1, h, gnd, 0, false));
                }
                for (int ri = 0; ri < runs.Count; ri++)
                {
                    var r = runs[ri]; double H = Derived.FloorBase(b, r.k1) - Derived.FloorBase(b, r.k0); bool topRun = last && ri == runs.Count - 1;
                    var ws = r.g ? Facade.GroundSpec(st, r.h) : Facade.WindowSpec(st.windows, r.h, st);
                    r.slot = Row(C, SpecOf(ws), new[] { r.h, st.bands ? Math.Max(0, Math.Min(r.k1, N - 1) - r.k0) : 0, topRun && !pitched ? H : 1e9, topRun ? (st.parapet ? 1 : 2) : 0 });
                    r.top = topRun && !pitched; runs[ri] = r;
                }
                for (int i = 0; i < n; i++)
                {
                    var e = Geo.EdgeInfo(g.Wp, i, g.ccw); var F = e.F; var m = Geo.MiterOf(g.cor, i, e.L); var (uS, uE) = Facade.Span(m, e.L);
                    double bay = blank.Contains(i) ? 0 : Facade.BayLayout(st, uS, uE).bay;
                    double yc = Derived.FloorBase(b, t.k0);
                    var c0 = Party.CornerCut(site, b, t.k0, i, false, yc); var c1 = Party.CornerCut(site, b, t.k0, i, true, yc);
                    if (c0 != null) m.S = c0.Value; if (c1 != null) m.E = c1.Value;
                    double s1 = m.S.At(T), e1 = m.E.At(T), s0 = m.S.At(0), e0 = m.E.At(0); var outN = new P3(e.w.x, 0, e.w.z);
                    foreach (var r in runs)
                    {
                        double y0 = Derived.FloorBase(b, r.k0), y1 = r.top ? yTop : Derived.FloorBase(b, r.k1), H = y1 - y0; slot = r.slot;
                        Quad(new[] { F.At(s1, y0, T), F.At(e1, y0, T), F.At(e1, y1, T), F.At(s1, y1, T) }, outN,
                            new[] { new[] { s1, 0, uS, uE }, new[] { e1, 0, uS, uE }, new[] { e1, H, uS, uE }, new[] { s1, H, uS, uE } }, bay, 1);
                    }
                    slot = tierSlot;
                    if (last && !pitched)
                    {
                        Quad(new[] { F.At(s0, top, 0), F.At(e0, top, 0), F.At(e0, yTop, 0), F.At(s0, yTop, 0) }, new P3(-e.w.x, 0, -e.w.z), ZZ, 0, st.parapet ? 4 : 3);
                        Quad(new[] { F.At(s0, yTop, 0), F.At(e0, yTop, 0), F.At(e1, yTop, T), F.At(s1, yTop, T) }, new P3(0, 1, 0), ZZ, 0, 3);
                    }
                }
                if (b.details.Exists(d => d.kind == DetailKind.Escape))
                    for (int k = t.k0; k < t.k1; k++)
                        foreach (var d in Details.At(site, b, k))
                        {
                            if (d.kind != DetailKind.Escape || d.ladder) continue;
                            var e = Geo.EdgeInfo(g.Wp, d.i, g.ccw); var F = e.F; double yk = Derived.FloorBase(b, k), u0 = d.u - 1.3, u1 = d.u + 1.3, y0 = yk + 0.16, y1 = yk + 0.24, w0 = T, w1 = T + 1.1;
                            Quad(new[] { F.At(u0, y1, w0), F.At(u1, y1, w0), F.At(u1, y1, w1), F.At(u0, y1, w1) }, new P3(0, 1, 0), ZZ, 0, 3);
                            Quad(new[] { F.At(u0, y0, w0), F.At(u1, y0, w0), F.At(u1, y0, w1), F.At(u0, y0, w1) }, new P3(0, -1, 0), ZZ, 0, 3);
                            Quad(new[] { F.At(u0, y0, w1), F.At(u1, y0, w1), F.At(u1, y1, w1), F.At(u0, y1, w1) }, new P3(e.w.x, 0, e.w.z), ZZ, 0, 3);
                            Quad(new[] { F.At(u0, y0, w0), F.At(u0, y0, w1), F.At(u0, y1, w1), F.At(u0, y1, w0) }, new P3(-e.u.x, 0, -e.u.z), ZZ, 0, 3);
                            Quad(new[] { F.At(u1, y0, w0), F.At(u1, y0, w1), F.At(u1, y1, w1), F.At(u1, y1, w0) }, new P3(e.u.x, 0, e.u.z), ZZ, 0, 3);
                        }
                if (last && !pitched)
                {
                    var rings = new List<List<Vec2>> { fp };
                    foreach (var v in b.voids) if (Courtyards.HoleAt(b, v, N)) rings.Add(v.shape);
                    Flat(rings, top, 2, false);
                }
                else if (last) RoofPieces(Roofs.Parts(site, b)!.Parts);
                else
                {
                    var tr = Roofs.TerraceRoof(site, b, t.k1);
                    if (tr != null) RoofPieces(tr.Parts);
                    else
                    {
                        double y1 = Derived.FloorBase(b, t.k1);
                        foreach (var poly in Geo.TerracePolys(fp, Derived.OutlineAt(b, t.k1))) Flat(poly, y1, 2, false);
                        foreach (var r in L0.TerraceRuns(t.k1))
                        {
                            var F = new Frame(r.a, r.u, r.w); double s0 = r.S.s, e0 = r.E.s, s1 = r.S.At(T), e1 = r.E.At(T), yp = y1 + 1.1;
                            Quad(new[] { F.At(s1, y1, T), F.At(e1, y1, T), F.At(e1, yp, T), F.At(s1, yp, T) }, new P3(r.w.x, 0, r.w.z), ZZ, 0, 4);
                            Quad(new[] { F.At(s0, y1, 0), F.At(e0, y1, 0), F.At(e0, yp, 0), F.At(s0, yp, 0) }, new P3(-r.w.x, 0, -r.w.z), ZZ, 0, 4);
                            Quad(new[] { F.At(s0, yp, 0), F.At(e0, yp, 0), F.At(e1, yp, T), F.At(s1, yp, T) }, new P3(0, 1, 0), ZZ, 0, 3);
                        }
                    }
                }
            }
            // courtyards: plain walls facing into each, from its paving up to the roof's top, and the paving
            int courtSlot = -1;
            foreach (var v in b.voids)
            {
                if (v.kind != VoidKind.Courtyard || !Courtyards.Live(b, v)) continue;
                if (courtSlot < 0) courtSlot = Row(L0.At(N).C);
                slot = courtSlot;
                double y0 = Derived.FloorBase(b, v.bottom), y1 = pitched ? top : yTop;
                foreach (var ring in Courtyards.Offset(v.shape, -T))
                {
                    bool ccw = Geo.Area2(ring) > 0; int n = ring.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var a = ring[i]; var c = ring[(i + 1) % n]; double L = Geo.Hypot(c.x - a.x, c.z - a.z); if (L < 1e-4) continue;
                        double ux = (c.x - a.x) / L, uz = (c.z - a.z) / L; var nIn = ccw ? new P3(-uz, 0, ux) : new P3(uz, 0, -ux);
                        Quad(new[] { new P3(a.x + ox, y0, a.z + oz), new P3(c.x + ox, y0, c.z + oz), new P3(c.x + ox, y1, c.z + oz), new P3(a.x + ox, y1, a.z + oz) }, nIn, ZZ, 0, 4);
                    }
                    Flat(new List<List<Vec2>> { ring }, y0, 2, false);
                }
            }
            // bridges this building builds: a plain box from the deck's underside to the roof (or the rails' top)
            foreach (var br in b.bridges)
            {
                if (!(Bridges.Span(site, b, br) is BridgeSpan s)) continue;
                if (courtSlot < 0) courtSlot = Row(L0.At(N).C);
                slot = courtSlot;
                double hw = s.W / 2, y0 = -Bridges.Deck, y1 = br.open ? 1.02 : Bridges.Inside + Bridges.Roof; var F = s.F;
                P3 At(double a, double o, double dy) => F.At(a, s.Y(a) + dy, o);
                var up = new P3(-s.Slope * s.u.x, 1, -s.Slope * s.u.z).Normalized; var lft = new P3(s.w.x, 0, s.w.z);
                Quad(new[] { At(0, -hw, y1), At(s.sLeft, -hw, y1), At(s.sRight, hw, y1), At(0, hw, y1) }, up, ZZ, 0, 2);
                Quad(new[] { At(0, -hw, y0), At(0, hw, y0), At(s.sRight, hw, y0), At(s.sLeft, -hw, y0) }, up * -1, ZZ, 0, 4);
                Quad(new[] { At(0, hw, y0), At(0, hw, y1), At(s.sRight, hw, y1), At(s.sRight, hw, y0) }, lft, ZZ, 0, 4);
                Quad(new[] { At(0, -hw, y0), At(s.sLeft, -hw, y0), At(s.sLeft, -hw, y1), At(0, -hw, y1) }, lft * -1, ZZ, 0, 4);
            }
            return M;
        }
    }
}
