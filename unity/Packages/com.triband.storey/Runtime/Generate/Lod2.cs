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
        /// <summary>
        /// The band: height (0 = the prototype's 0.22 m), a shade of the wall (1) or the trim (0), that shade, and its own
        /// colour's slot (<see cref="ColorSlot.Band"/>; 0 = none). GPU texel 2. Storey's own.
        /// </summary>
        public double[] band = { 0, 0, 0, 0 };
        /// <summary>
        /// The slot the run's walls take their colour from: 0 for the walls', <see cref="ColorSlot.GroundWall"/> for a ground
        /// storey with its own (docs/EDITOR.md §6.7). GPU texel 1's y. Storey's own.
        /// </summary>
        public int wallSlot;
        /// <summary>
        /// A cornice round the top of the run's storeys (a tier's top storey, a run of its own): its height (0 = none) and its
        /// colour's slot (the trim's or <see cref="ColorSlot.Cornice"/>). GPU texel 1's z and w. Storey's own.
        /// </summary>
        public double corniceH; public int corniceSlot;
        /// <summary>
        /// A raised roof cap on the top run's parapet zone (<see cref="RoofCap.raised"/>, run's fourth number 3, or 4 with a
        /// parapet on it): its height and colour's slot (the trim's or <see cref="ColorSlot.Cap"/>). GPU texel 3. Storey's own.
        /// </summary>
        public double[] cap = { 0, 0, 0, 0 };
        /// <summary>The 24 floats as the table stores them (six texels: wall, trim, glass, roof, spec, run), colours resolved to linear RGB.</summary>
        public double[] Texels(ColorResolver colors)
        {
            Rgb w = colors.Resolve(wall), t = colors.Resolve(trim), g = colors.Resolve(glass), r = colors.Resolve(roof);
            return new[] { w.r, w.g, w.b, 1, t.r, t.g, t.b, 1, g.r, g.g, g.b, 1, r.r, r.g, r.b, 1, spec[0], spec[1], spec[2], spec[3], run[0], run[1], run[2], run[3] };
        }

        /// <summary>
        /// The 24 floats the GPU table stores (docs/COLOURS.md §3.5): texel 0 is (colour row, wall, trim and glass
        /// shades), texel 1 (roof shade, the walls' slot, the cornice's height and slot), texel 2 the band, texel 3 the roof cap,
        /// then spec and run as in <see cref="Texels"/>.
        /// The facade shader reads wall, trim, glass and roof from <paramref name="colorRow"/>, the row of the swatches' style.
        /// </summary>
        public double[] GpuTexels(int colorRow)
        {
            Check();
            return new[] { colorRow, wall.tone, trim.tone, glass.tone, roof.tone, wallSlot, corniceH, corniceSlot, band[0], band[1], band[2], band[3], cap[0], cap[1], cap[2], cap[3], spec[0], spec[1], spec[2], spec[3], run[0], run[1], run[2], run[3] };
        }

        /// <summary>
        /// <see cref="GpuTexels(int)"/> as the table stores them, floats, into <paramref name="into"/> (24 of them): the table
        /// writes a row without an array of its own (docs/PERFORMANCE.md §6.8).
        /// </summary>
        public void GpuTexels(int colorRow, Span<float> into)
        {
            Check();
            into[0] = colorRow; into[1] = (float)wall.tone; into[2] = (float)trim.tone; into[3] = (float)glass.tone;
            into[4] = (float)roof.tone; into[5] = wallSlot; into[6] = (float)corniceH; into[7] = corniceSlot;
            for (int i = 0; i < 4; i++) { into[8 + i] = (float)band[i]; into[12 + i] = (float)cap[i]; into[16 + i] = (float)spec[i]; into[20 + i] = (float)run[i]; }
        }

        void Check()
        {
            if (wall.slot != ColorSlot.Wall || trim.slot != ColorSlot.Trim || glass.slot != ColorSlot.Glass || roof.slot != ColorSlot.Roof
                || !trim.style.Equals(wall.style) || !glass.style.Equals(wall.style) || !roof.style.Equals(wall.style))
                throw new InvalidOperationException("a parameter row's colours must be the wall, trim, glass and roof of one style");
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
        /// <summary>
        /// The faces only Unity draws, as ranges of triangles: the backs of the roof's parts (<see cref="MeshBuilder.BackingTris"/>),
        /// which the prototype draws two-sided, and what closes the massing where it is open (a corner's slit, the lid under
        /// a pitched roof). Its census leaves them out.
        /// </summary>
        public readonly List<(int start, int end)> BackingTris = new List<(int, int)>();
        public int Tag;
        /// <summary>
        /// Per vertex, when several buildings share the mesh (a merged cell, <see cref="Lod.Cells"/>): each vertex's
        /// building tag, in place of <see cref="Tag"/>. Its <see cref="Slot"/>s are then the table's rows already.
        /// </summary>
        public List<int>? Tags;
        public int Tris => I.Count / 3;
        public int Verts => P.Count;

        // ---- reuse (docs/CITY.md §7) ----

        /// <summary>The massings kept for the next <see cref="Make"/>, their lists empty, on any thread (a lock each way).</summary>
        const int Spares = 32;
        static readonly Stack<Lod2Mesh> spares = new Stack<Lod2Mesh>();

        /// <summary>
        /// A massing as the constructor makes it: one let go with <see cref="Recycle"/> when there is one, its lists keeping
        /// their room, else a new one. Building a massing's lists from nothing made most of a layout load's garbage: 0.06 MB
        /// a building, 186 MB for the 3,000-building test city.
        /// </summary>
        public static Lod2Mesh Make()
        {
            lock (spares) if (spares.Count > 0) return spares.Pop();
            return new Lod2Mesh();
        }

        /// <summary>
        /// Done with this massing (its cell packed, or it uploaded): its lists are emptied and it is kept for the next
        /// <see cref="Make"/>, on any thread. Nothing may use it after.
        /// </summary>
        public void Recycle()
        {
            P.Clear(); N.Clear(); Fac.Clear(); Fac2.Clear(); Slot.Clear(); I.Clear(); Rows.Clear(); BackingTris.Clear(); Tag = 0; Tags = null;
            lock (spares) if (spares.Count < Spares && !spares.Contains(this)) spares.Push(this);
        }

        /// <summary>Let go of the massings kept (the tests' allocation counts start from none).</summary>
        public static void ForgetSpares() { lock (spares) spares.Clear(); }

        /// <summary>
        /// Merge vertices the upload can't tell apart (every attribute as a float, as <see cref="MeshBuilder.Weld"/> has it),
        /// keeping the first of each, and rewrite the indices; the triangles are unchanged. Where a parapet's top or a lid's
        /// rim turns a corner, the two walls' quads meet: a tenth to a seventh of a massing's vertices (the converted scenes',
        /// the test city's). Returns how many went. Storey's own.
        /// </summary>
        public int Weld()
        {
            int n = P.Count;
            if (n == 0 || Tags != null) return 0;
            // each vertex's first equal, through a table of vertex indices (open addressing, at least twice as many slots as
            // vertices; 0 is empty, else the index + 1), lent for the weld: a massing waiting for its cell holds none
            int size = 4; while (size < n * 2) size <<= 1;
            var sc = welds.Take();
            try { return Merge(n, size, sc.Slots(size), sc.Map(n)); }
            finally { welds.Give(sc); }
        }

        // Weld's work, in tables lent for it
        int Merge(int n, int size, int[] slots, int[] map)
        {
            int kept = 0;
            for (int i = 0; i < n; i++)
            {
                var q = P[i];
                int hash = unchecked(BitConverter.SingleToInt32Bits((float)q.x + 0f) * 73856093 ^ BitConverter.SingleToInt32Bits((float)q.y + 0f) * 19349663 ^ BitConverter.SingleToInt32Bits((float)q.z + 0f) * 83492791);
                for (int h = (hash ^ hash >> 15) & (size - 1); ; h = (h + 1) & (size - 1))
                {
                    int sl = slots[h];
                    if (sl == 0) { slots[h] = i + 1; map[i] = kept++; break; }
                    if (Same(i, sl - 1)) { map[i] = map[sl - 1]; break; }
                }
            }
            int gone = n - kept;
            if (gone == 0) return 0;
            // in place: the kept vertices move down, in order
            for (int i = 0, at = 0; i < n; i++)
            {
                if (map[i] != at) continue;
                if (at != i)
                {
                    P[at] = P[i]; N[at] = N[i]; Slot[at] = Slot[i];
                    for (int c = 0; c < 4; c++) Fac[4 * at + c] = Fac[4 * i + c];
                    Fac2[2 * at] = Fac2[2 * i]; Fac2[2 * at + 1] = Fac2[2 * i + 1];
                }
                at++;
            }
            P.RemoveRange(kept, n - kept); N.RemoveRange(kept, n - kept); Slot.RemoveRange(kept, n - kept);
            Fac.RemoveRange(4 * kept, 4 * (n - kept)); Fac2.RemoveRange(2 * kept, 2 * (n - kept));
            for (int t = 0; t < I.Count; t++) I[t] = map[I[t]];
            return gone;
        }

        // what Weld works in, lent out for each
        static readonly Lender<WeldScratch> welds = new Lender<WeldScratch>(8);

        sealed class WeldScratch
        {
            int[] slots = Array.Empty<int>(), map = Array.Empty<int>();
            /// <summary>At least <paramref name="size"/> slots, the first so many empty.</summary>
            public int[] Slots(int size) { if (slots.Length < size) slots = new int[size]; else Array.Clear(slots, 0, size); return slots; }
            public int[] Map(int n) { if (map.Length < n) map = new int[Math.Max(n, 2 * map.Length)]; return map; }
        }

        // the same as uploaded: every attribute as a float
        bool Same(int i, int j)
        {
            static bool F(double a, double b) => (float)a == (float)b;
            P3 a = P[i], b = P[j], m = N[i], o = N[j];
            if (!F(a.x, b.x) || !F(a.y, b.y) || !F(a.z, b.z) || !F(m.x, o.x) || !F(m.y, o.y) || !F(m.z, o.z) || Slot[i] != Slot[j]) return false;
            for (int c = 0; c < 4; c++) if (!F(Fac[4 * i + c], Fac[4 * j + c])) return false;
            return F(Fac2[2 * i], Fac2[2 * j]) && F(Fac2[2 * i + 1], Fac2[2 * j + 1]);
        }
    }

    /// <summary>
    /// LOD2: massing. One quad per footprint edge for the height of each setback tier's storey runs,
    /// the parapets, the roof and terraces. Windows come from the facade shader, driven by the same
    /// numbers the geometry LODs use, so the LOD1 to LOD2 switch keeps every window in place.
    /// </summary>
    public static class Lod2
    {
        public static double[] SpecOf(Facade.WinSpec? ws) => ws != null ? new[] { ws.full ?? -1, ws.w, ws.y0, ws.y1 } : new[] { -1.0, 0, 1, 0 };

        /// <summary>
        /// <see cref="SpecOf(Facade.WinSpec?)"/> for a run of style <paramref name="st"/> (<paramref name="ground"/>: the ground
        /// storey's): with fixed bays (<see cref="FacadeStyle.bayFit"/>) an artist's window that keeps its size keeps it however
        /// narrow the bay, as <see cref="Facade.Windows"/> lays it out, so it is painted as a full-width window is, the bay less
        /// a gap (the shader would take a window of a fixed width to the bay less 50 cm). Storey's own.
        /// </summary>
        public static double[] SpecOf(Facade.WinSpec? ws, FacadeStyle st, bool ground)
        {
            var sp = SpecOf(ws);
            if (ws != null && ws.full == null && st.bayFit == BayFit.Fixed && OpeningSets.Representative(st, ground, out var k) && k != null && !k.stretch)
                sp[0] = Math.Max(0, st.bay - ws.w);
            return sp;
        }

        // whether the roof over the terrace below storey k's tier comes up to the window painted in bay j of edge i's wall
        // (Roofs.TerraceRoofAt), its foot at `foot`: Facade.Ops leaves that window out
        static bool UnderTerraceRoof(Site site, BuildingData b, int k, int i, double uS, double bay, double ww, int j, double foot)
        {
            double c = uS + (j + 0.5) * bay;
            return Roofs.TerraceRoofAt(site, b, k, i, c - ww / 2, c + ww / 2) > foot;
        }

        public static Lod2Mesh Build(Site site, BuildingData b)
        {
            double T = Dim.T_EXT; int N = b.floors.Count; double ox = b.pos.x, oz = b.pos.z, top = Derived.RoofY(b);
            var stTop = Derived.StyleAt(b, N); bool pitched = Roofs.IsPitched(b);
            // a roof cap (but a fascia) takes the curb's place, and a raised one lifts the parapet (RoofCap); its overhang is too
            // small to show here
            var cap = pitched ? null : RoofCap.Of(stTop); double lift = cap?.Lift ?? 0;
            double yTop = pitched ? top : top + lift + (stTop.parapet ? 1.1 : cap != null && !cap.Value.fascia ? 0 : 0.3);
            // under a pitched roof, the massing's lid: LOD1's roof slab, just below the roof where it sits on the walls. It
            // shows only where the roof stops short of the walls (pulled back behind a neighbour's wall along an edge mostly
            // shared, past the neighbour's end), which a back face culled would leave open
            double yLid = top - 0.05;
            var M = Lod2Mesh.Make(); M.Tag = site.IndexOf(b) + 2 * Lod1.LOD_TAG;
            int slot = 0;
            var L0 = new Lod0.Shared(site, b);

            void Vtx(P3 p, P3 nr, double u, double h, double s0, double s1, double bay, double kind)
            {
                // JavaScript's Math.round (ties toward +∞), which the prototype's Int8 normals were made with
                static double Q(double v) => Math.Floor(v * 127 + 0.5);
                M.P.Add(p); M.N.Add(new P3(Q(nr.x), Q(nr.y), Q(nr.z)));
                M.Fac.Add(u); M.Fac.Add(h); M.Fac.Add(s0); M.Fac.Add(s1); M.Fac2.Add(bay); M.Fac2.Add(kind); M.Slot.Add(slot);
            }
            // a quad's corners in order; a facade's carry u along the wall (s at the first and last corner, e at the other
            // two), the height up the run (0, then h at the last two) and the window span (uS to uE); the rest carry none.
            // No arrays: a massing's quads made most of its garbage once its lists were reused
            void Quad(P3 q0, P3 q1, P3 q2, P3 q3, P3 nr, double bay, double kind, double s = 0, double e = 0, double h = 0, double uS = 0, double uE = 0)
            {
                bool flip = P3.Dot(P3.Cross(q1 - q0, q2 - q0), nr) < 0; int bse = M.P.Count;
                Vtx(q0, nr, s, 0, uS, uE, bay, kind); Vtx(q1, nr, e, 0, uS, uE, bay, kind); Vtx(q2, nr, e, h, uS, uE, bay, kind); Vtx(q3, nr, s, h, uS, uE, bay, kind);
                var I = M.I;
                if (flip) { I.Add(bse); I.Add(bse + 2); I.Add(bse + 1); I.Add(bse); I.Add(bse + 3); I.Add(bse + 2); }
                else { I.Add(bse); I.Add(bse + 1); I.Add(bse + 2); I.Add(bse); I.Add(bse + 2); I.Add(bse + 3); }
            }
            // a strip's half of a clipped corner (Cut.Clipped, docs/EDITOR.md §6.1): beside a wall's quad w out, between heights ya
            // and yb, painted as the wall is but with no windows (h: the run's height, for its bands); and on a top or underside
            // between wa and wb out, a triangle from the footprint line
            void ClipWall(Frame F, Cut x, double w, double ya, double yb, double kind, double h)
            {
                if (!x.Clipped || w < 1e-6 || yb - ya < 1e-6) return;
                var (tu, tw) = x.Tip(w); double eu = x.At(w);
                P3 a = F.At(eu, ya, w), q = F.At(tu, ya, tw);
                Quad(a, q, F.At(tu, yb, tw), F.At(eu, yb, w), Geo.Bisector(F, x), 0, kind, 0, Geo.Hypot(q.x - a.x, q.z - a.z), h);
            }
            void ClipFlat(Frame F, Cut x, double wa, double wb, double y, double kind, bool down = false)
            {
                if (!x.Clipped) return;
                wa = Math.Max(wa, 0); if (wb - wa < 1e-6) return;
                var nr = new P3(0, down ? -1 : 1, 0); var (u1, w1) = x.Tip(wb);
                if (wa < 1e-6)
                {
                    P3 p0 = F.At(x.s, y, 0), p1 = F.At(x.At(wb), y, wb), p2 = F.At(u1, y, w1);
                    if (P3.Dot(P3.Cross(p1 - p0, p2 - p0), nr) < 0) { var t = p1; p1 = p2; p2 = t; }
                    int bse = M.P.Count;
                    Vtx(p0, nr, 0, 0, 0, 0, 0, kind); Vtx(p1, nr, 0, 0, 0, 0, 0, kind); Vtx(p2, nr, 0, 0, 0, 0, 0, kind);
                    M.I.Add(bse); M.I.Add(bse + 1); M.I.Add(bse + 2);
                    return;
                }
                var (u0, w0) = x.Tip(wa);
                Quad(F.At(x.At(wa), y, wa), F.At(x.At(wb), y, wb), F.At(u1, y, w1), F.At(u0, y, w0), nr, 0, kind);
            }
            void Flat(List<List<Vec2>> rings, double y, double kind, bool down)
            {
                var v2 = new List<List<Vec2>>(); foreach (var r in rings) { var l = new List<Vec2>(r.Count); foreach (var q in r) l.Add(new Vec2(q.x + ox, q.z + oz)); v2.Add(l); }
                var tris = Triangulate.Shape(v2[0], v2.GetRange(1, v2.Count - 1));
                var all = new List<Vec2>(); foreach (var r in v2) all.AddRange(r);
                int bse = M.P.Count; var nr = new P3(0, down ? -1 : 1, 0);
                foreach (var p in all) Vtx(new P3(p.x, y, p.z), nr, 0, 0, 0, 0, 0, kind);
                for (int t = 0; t < tris.Count; t += 3)
                {
                    int x = tris[t], yy = tris[t + 1], z = tris[t + 2]; var A = all[x]; var B = all[yy]; var D = all[z];
                    if ((((B.z - A.z) * (D.x - A.x) - (B.x - A.x) * (D.z - A.z)) < 0) != down) { int q = yy; yy = z; z = q; }
                    M.I.Add(bse + x); M.I.Add(bse + yy); M.I.Add(bse + z);
                }
            }
            // the triangles made since from: only Unity draws them
            void Backed(int from)
            {
                if (M.Tris <= from) return;
                int nb = M.BackingTris.Count;
                if (nb > 0 && M.BackingTris[nb - 1].end == from) M.BackingTris[nb - 1] = (M.BackingTris[nb - 1].start, M.Tris);
                else M.BackingTris.Add((from, M.Tris));
            }
            void RoofPieces(List<RoofPart> parts)
            {
                foreach (var p in parts)
                {
                    if (p.Dormer) continue;   // massing: the roof's shape only
                    double K = p.Kind == RoofKind.Roof ? 2 : p.Kind == RoofKind.Wall ? 4 : 3;
                    var all = new List<P3>(p.Pts); foreach (var h in p.Holes) all.AddRange(h);
                    int bse = M.P.Count; foreach (var q in all) Vtx(q, p.N, 0, 0, 0, 0, 0, K);
                    var tris = Triangulate.Planar(p.Pts, p.Holes, p.N);
                    for (int t = 0; t < tris.Count; t += 3)
                    {
                        int a = tris[t], c = tris[t + 1], d = tris[t + 2];
                        if (P3.Dot(P3.Cross(all[c] - all[a], all[d] - all[a]), p.N) < 0) { int x = c; c = d; d = x; }
                        M.I.Add(bse + a); M.I.Add(bse + c); M.I.Add(bse + d);
                    }
                    // its back, just behind it, as LOD0's and LOD1's (Roofs.Draw): only Unity draws it
                    int back = M.P.Count, from = M.Tris; var off = p.N * -Dim.BACK;
                    foreach (var q in all) Vtx(q + off, p.N * -1, 0, 0, 0, 0, 0, K);
                    for (int t = 0; t < tris.Count; t += 3)
                    {
                        int a = tris[t], c = tris[t + 1], d = tris[t + 2];
                        if (P3.Dot(P3.Cross(all[c] - all[a], all[d] - all[a]), p.N) > 0) { int x = c; c = d; d = x; }
                        M.I.Add(back + a); M.I.Add(back + c); M.I.Add(back + d);
                    }
                    Backed(from);
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
                var fp = Derived.OutlineAt(b, t.k0); var st = Derived.StyleAt(b, t.k0);
                var g = L0.At(t.k0); int n = fp.Count; bool last = t.k1 == N; var C = g.C;
                int tierSlot = Row(C); slot = tierSlot;
                if (t.k0 > 0)
                {
                    double y0 = Derived.FloorBase(b, t.k0), yb = y0 - Dim.SLAB;
                    foreach (var poly in Geo.TerracePolys(fp, Derived.OutlineAt(b, t.k0 - 1))) Flat(poly, yb, 4, true);
                    foreach (var r in L0.OverhangRuns(t.k0))
                    {
                        var F = new Frame(r.a, r.u, r.w); double s0 = r.S.s, e0 = r.E.s, s1 = r.S.At(T), e1 = r.E.At(T);
                        Quad(F.At(s1, yb, T), F.At(e1, yb, T), F.At(e1, y0, T), F.At(s1, y0, T), new P3(r.w.x, 0, r.w.z), 0, 4);
                        Quad(F.At(s0, yb, 0), F.At(e0, yb, 0), F.At(e1, yb, T), F.At(s1, yb, T), new P3(0, -1, 0), 0, 4);
                        ClipWall(F, r.S, T, yb, y0, 4, 0); ClipWall(F, r.E, T, yb, y0, 4, 0);
                        ClipFlat(F, r.S, 0, T, yb, 4, true); ClipFlat(F, r.E, 0, T, yb, 4, true);
                    }
                }
                var runs = new List<(int k0, int k1, double h, bool g, int slot, bool top)>();
                int[]? bareSlots = null;   // each run's row for a bare wall (docs/EDITOR.md §6.7): no windows and no band, made when one is first met
                // each storey the roof over the terrace below can reach (Roofs.TerraceRoofReach) is a run of its own, below `alone`:
                // its walls leave out the windows that roof comes up to, bay by bay, as Facade.Ops does
                double reach = t.k0 > 0 ? Roofs.TerraceRoofReach(site, b, t.k0) : double.NegativeInfinity; int alone = t.k0;
                while (alone < t.k1 && Derived.FloorBase(b, alone) - Roofs.TerraceRoofClear < reach) alone++;
                for (int k = t.k0; k < t.k1; k++)
                {
                    double h = Derived.FloorH(b, k); bool gnd = k == 0;
                    // a tier's top storey under a cornice is a run of its own: its windows stay under the cornice, which it paints
                    if (runs.Count > 0 && k > alone && !runs[runs.Count - 1].g && !gnd && Math.Abs(runs[runs.Count - 1].h - h) < 1e-6 && !Facade.CorniceAt(b, k)) { var r = runs[runs.Count - 1]; r.k1 = k + 1; runs[runs.Count - 1] = r; }
                    else runs.Add((k, k + 1, h, gnd, 0, false));
                }
                for (int ri = 0; ri < runs.Count; ri++)
                {
                    var r = runs[ri]; double H = Derived.FloorBase(b, r.k1) - Derived.FloorBase(b, r.k0); bool topRun = last && ri == runs.Count - 1;
                    double? under = Facade.CorniceUnder(b, r.k1 - 1); bool cn = under.HasValue;
                    var ws = r.g ? Facade.GroundSpec(st, r.h, under) : Facade.WindowSpec(st.windows, r.h, st, cornice: under);
                    // the storeys with a band, from the run's first (Facade.BandAt: all but the building's top, or the ground's
                    // alone); a cornice's storey has none
                    int banded = 0; for (int k = r.k0; k < r.k1 && Facade.BandAt(b, k) && !(cn && k == r.k1 - 1); k++) banded++;
                    r.slot = Row(C, SpecOf(ws, st, r.g), new[] { r.h, banded, topRun && !pitched ? H : 1e9, topRun ? (lift > 0 ? (st.parapet ? 4 : 3) : st.parapet ? 1 : 2) : 0 });
                    var row = M.Rows[r.slot];
                    if (topRun && lift > 0) row.cap = new[] { lift, (double)(int)C.CapOf(st).slot, 0, 0 };
                    if (st.bandH.HasValue || st.bandWall || st.band != null) row.band = new[] { Lod0.Shared.Band(st, C).h, st.bandWall ? 1 : 0, C.wallBand.tone, st.band != null ? (int)ColorSlot.Band : 0 };
                    if (r.g && st.groundWall != null) row.wallSlot = (int)ColorSlot.GroundWall;
                    // painted from its foot to the storey's top: under a pitched roof the eave's soffit covers the rest
                    if (cn) { row.corniceH = under!.Value; row.corniceSlot = (int)C.CorniceOf(st).slot; }
                    r.top = topRun && !pitched; runs[ri] = r;
                }
                for (int i = 0; i < n; i++)
                {
                    var e = Geo.EdgeInfo(g.Wp, i, g.ccw); var F = e.F; var m = Geo.MiterOf(g.cor, i, e.L); var (sS, sE) = Facade.Span(m, e.L);
                    double yc = Derived.FloorBase(b, t.k0);
                    var c0 = Party.CornerCut(site, b, t.k0, i, false, yc, false, out double c0Top); var c1 = Party.CornerCut(site, b, t.k0, i, true, yc, false, out double c1Top);
                    double ms1 = m.S.At(T), me1 = m.E.At(T), ms0 = m.S.At(0), me0 = m.E.At(0);   // the corners mitred, as above a neighbour's top they are
                    var mS = m.S; var mE = m.E;   // the corners' own cuts, clipped where they are too sharp to mitre (Cut.Clipped)
                    if (c0 != null) m.S = c0.Value; if (c1 != null) m.E = c1.Value;
                    double s1 = m.S.At(T), e1 = m.E.At(T), s0 = m.S.At(0), e0 = m.E.At(0); var outN = new P3(e.w.x, 0, e.w.z);
                    for (int ri = 0; ri < runs.Count; ri++)
                    {
                        var r = runs[ri];
                        double y0 = Derived.FloorBase(b, r.k0), y1 = r.top ? yTop : Derived.FloorBase(b, r.k1), H = y1 - y0; slot = r.slot;
                        // the windows' span, as Facade.Ops lays them out (narrowed by the dressing of the artist's windows the run can pick)
                        // (fixed bays: the whole bays on the wall's outer face, as Facade.Ops works it out from the outline)
                        var (uS, uE) = Facade.WindowSpan(st, r.g, sS, sE);
                        var es = Derived.EdgeStateAt(b, t.k0, i);
                        double bay = 0;
                        if (es == EdgeState.Open)
                        {
                            var (oS, oE) = st.bayFit == BayFit.Fixed ? Facade.OuterFace(fp, g.ccw, i) : (uS, uE);
                            var (nb, wb, bS) = Facade.Bays(st, uS, uE, oS, oE);
                            bay = nb > 0 ? wb : 0;
                            if (st.bayFit == BayFit.Fixed) { uS = bS; uE = bS + nb * wb; }
                        }
                        if (es == EdgeState.Bare && M.Rows[r.slot].run[1] > 0)
                        {
                            bareSlots ??= new int[runs.Count];   // a row's index + 1; 0 = none yet
                            if (bareSlots[ri] == 0) { var src = M.Rows[r.slot]; bareSlots[ri] = 1 + Row(C, src.spec, new[] { src.run[0], 0, src.run[2], src.run[3] }); M.Rows[bareSlots[ri] - 1].band = src.band; }
                            slot = bareSlots[ri] - 1;
                        }
                        // a storey the roof over the terrace below can reach: the bays whose painted window it comes up to are painted
                        // without one (bay 0), the wall in pieces where that changes
                        var sp = M.Rows[r.slot].spec; double ww = sp[0] >= 0 ? bay - sp[0] : Math.Min(sp[1], bay - 0.5);
                        int bays = bay > 0 ? (int)Math.Round((uE - uS) / bay) : 0;
                        if (r.k0 < alone && bays > 0 && ww >= 0.45 && sp[3] > sp[2] + 0.4)
                        {
                            double foot = y0 + sp[2] - Roofs.TerraceRoofClear, ua = s1;
                            bool prev = UnderTerraceRoof(site, b, r.k0, i, uS, bay, ww, 0, foot);
                            for (int j = 1; j <= bays; j++)
                            {
                                bool cur = j < bays && UnderTerraceRoof(site, b, r.k0, i, uS, bay, ww, j, foot);
                                if (j < bays && cur == prev) continue;
                                double ub = j < bays ? uS + j * bay : e1;
                                Quad(F.At(ua, y0, T), F.At(ub, y0, T), F.At(ub, y1, T), F.At(ua, y1, T), outN, prev ? 0 : bay, 1, ua, ub, H, uS, uE);
                                ua = ub; prev = cur;
                            }
                        }
                        else Quad(F.At(s1, y0, T), F.At(e1, y0, T), F.At(e1, y1, T), F.At(s1, y1, T), outN, bay, 1, s1, e1, H, uS, uE);
                        ClipWall(F, m.S, T, y0, y1, 1, H); ClipWall(F, m.E, T, y0, y1, 1, H);   // a neighbour's corner cut has no clip
                        // the tier's corner is cut for a neighbour lower than the run's top: above the neighbour the cut leaves a slit
                        // to the corner, closed by Unity only (the prototype's massing has it too)
                        foreach (var (cut, upTo, a, z) in new[] { (c0, c0Top, ms1, s1), (c1, c1Top, e1, me1) })
                        {
                            if (cut == null || y1 <= upTo + 1e-3 || z - a < 1e-3) continue;
                            int from = M.Tris; double ya = Math.Max(y0, upTo);
                            Quad(F.At(a, ya, T), F.At(z, ya, T), F.At(z, y1, T), F.At(a, y1, T), outN, 0, 4);
                            Backed(from);
                        }
                    }
                    slot = tierSlot;
                    if (last && !pitched)
                    {
                        Quad(F.At(s0, top, 0), F.At(e0, top, 0), F.At(e0, yTop, 0), F.At(s0, yTop, 0), new P3(-e.w.x, 0, -e.w.z), 0, st.parapet ? 4 : 3);
                        Quad(F.At(s0, yTop, 0), F.At(e0, yTop, 0), F.At(e1, yTop, T), F.At(s1, yTop, T), new P3(0, 1, 0), 0, 3);
                        ClipFlat(F, m.S, 0, T, yTop, 3); ClipFlat(F, m.E, 0, T, yTop, 3);
                    }
                    else if (last)
                    {
                        // the lid's rim, over the wall (mitred: a corner cut for a neighbour is inside it or below the lid)
                        int from = M.Tris;
                        Quad(F.At(ms0, yLid, 0), F.At(me0, yLid, 0), F.At(me1, yLid, T), F.At(ms1, yLid, T), new P3(0, 1, 0), 0, 2);
                        ClipFlat(F, mS, 0, T, yLid, 2); ClipFlat(F, mE, 0, T, yLid, 2);
                        Backed(from);
                    }
                }
                if (b.details.Exists(d => d.kind == DetailKinds.Escape))
                    for (int k = t.k0; k < t.k1; k++)
                        foreach (var d in Details.At(site, b, k))
                        {
                            if (d.kind.id != DetailKinds.Escape || d.ladder) continue;
                            var e = Geo.EdgeInfo(g.Wp, d.i, g.ccw); var F = e.F; double yk = Derived.FloorBase(b, k), u0 = d.u - 1.3, u1 = d.u + 1.3, y0 = yk + 0.16, y1 = yk + 0.24, w0 = T, w1 = T + 1.1;
                            Quad(F.At(u0, y1, w0), F.At(u1, y1, w0), F.At(u1, y1, w1), F.At(u0, y1, w1), new P3(0, 1, 0), 0, 3);
                            Quad(F.At(u0, y0, w0), F.At(u1, y0, w0), F.At(u1, y0, w1), F.At(u0, y0, w1), new P3(0, -1, 0), 0, 3);
                            Quad(F.At(u0, y0, w1), F.At(u1, y0, w1), F.At(u1, y1, w1), F.At(u0, y1, w1), new P3(e.w.x, 0, e.w.z), 0, 3);
                            Quad(F.At(u0, y0, w0), F.At(u0, y0, w1), F.At(u0, y1, w1), F.At(u0, y1, w0), new P3(-e.u.x, 0, -e.u.z), 0, 3);
                            Quad(F.At(u1, y0, w0), F.At(u1, y0, w1), F.At(u1, y1, w1), F.At(u1, y1, w0), new P3(e.u.x, 0, e.u.z), 0, 3);
                        }
                if (last && !pitched)
                {
                    var rings = new List<List<Vec2>> { fp };
                    foreach (var v in b.voids) if (Courtyards.HoleAt(b, v, N)) rings.Add(v.shape);
                    Flat(rings, top, 2, false);
                    // an inset cap: its sides in the trim's colour, its top the trim's with a strip, else the roof's; on a roof nothing
                    // comes up through, a solid block (RoofCap.Whole) with its top the roof's
                    if (cap is RoofCap ci && ci.inset)
                    {
                        double y1 = top + ci.h; bool block = RoofCap.Whole(b);
                        foreach (var (ring, holes) in L0.InsetCapRegion(ci, block))
                        {
                            List<Vec2> Local(List<Vec2> r) { var l = new List<Vec2>(r.Count); foreach (var q in r) l.Add(new Vec2(q.x - ox, q.z - oz)); return l; }
                            var loc = new List<List<Vec2>> { Local(ring) }; foreach (var hl in holes) loc.Add(Local(hl));
                            Flat(loc, y1, !block && stTop.capStrip > 0 ? 3 : 2, false);
                            foreach (var (a, z, nx, nz) in Lod0.Shared.InsetCapSides(ring, holes))
                                Quad(new P3(a.x, top, a.z), new P3(z.x, top, z.z), new P3(z.x, y1, z.z), new P3(a.x, y1, a.z), new P3(nx, 0, nz), 0, 3);
                        }
                    }
                }
                else if (last)
                {
                    var rp = Roofs.Parts(site, b)!;
                    site.riseMemo[b.id] = rp.Rise;   // the occlusion's ray test needs it (Roofs.Rise)
                    RoofPieces(rp.Parts);
                    // the lid, open where a courtyard opens through the roof (its walls' faces)
                    int from = M.Tris; var rings = new List<List<Vec2>> { fp };
                    foreach (var v in b.voids) if (v.kind == VoidKind.Courtyard && Courtyards.HoleAt(b, v, N)) rings.AddRange(Courtyards.Offset(v.shape, -T));
                    Flat(rings, yLid, 2, false);
                    Backed(from);
                }
                else
                {
                    var tr = Roofs.TerraceRoof(site, b, t.k1);
                    if (tr != null)
                    {
                        RoofPieces(tr.Parts);
                        // the lid under it, as under the top's: over the terrace and the walls it sits on
                        double yl = Derived.FloorBase(b, t.k1) - 0.05; int from = M.Tris;
                        foreach (var poly in Geo.TerracePolys(fp, Derived.OutlineAt(b, t.k1))) Flat(poly, yl, 2, false);
                        foreach (var r in L0.TerraceRuns(t.k1))
                        {
                            var F = new Frame(r.a, r.u, r.w);
                            Quad(F.At(r.S.s, yl, 0), F.At(r.E.s, yl, 0), F.At(r.E.At(T), yl, T), F.At(r.S.At(T), yl, T), new P3(0, 1, 0), 0, 2);
                            ClipFlat(F, r.S, 0, T, yl, 2); ClipFlat(F, r.E, 0, T, yl, 2);
                        }
                        Backed(from);
                    }
                    else
                    {
                        double y1 = Derived.FloorBase(b, t.k1);
                        foreach (var poly in Geo.TerracePolys(fp, Derived.OutlineAt(b, t.k1))) Flat(poly, y1, 2, false);
                        foreach (var r in L0.TerraceRuns(t.k1))
                        {
                            var F = new Frame(r.a, r.u, r.w); double s0 = r.S.s, e0 = r.E.s, s1 = r.S.At(T), e1 = r.E.At(T), yp = y1 + 1.1;
                            Quad(F.At(s1, y1, T), F.At(e1, y1, T), F.At(e1, yp, T), F.At(s1, yp, T), new P3(r.w.x, 0, r.w.z), 0, 4);
                            Quad(F.At(s0, y1, 0), F.At(e0, y1, 0), F.At(e0, yp, 0), F.At(s0, yp, 0), new P3(-r.w.x, 0, -r.w.z), 0, 4);
                            Quad(F.At(s0, yp, 0), F.At(e0, yp, 0), F.At(e1, yp, T), F.At(s1, yp, T), new P3(0, 1, 0), 0, 3);
                            ClipWall(F, r.S, T, y1, yp, 4, 0); ClipWall(F, r.E, T, y1, yp, 4, 0);
                            ClipFlat(F, r.S, 0, T, yp, 3); ClipFlat(F, r.E, 0, T, yp, 3);
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
                        Quad(new P3(a.x + ox, y0, a.z + oz), new P3(c.x + ox, y0, c.z + oz), new P3(c.x + ox, y1, c.z + oz), new P3(a.x + ox, y1, a.z + oz), nIn, 0, 4);
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
                Quad(At(0, -hw, y1), At(s.sLeft, -hw, y1), At(s.sRight, hw, y1), At(0, hw, y1), up, 0, 2);
                Quad(At(0, -hw, y0), At(0, hw, y0), At(s.sRight, hw, y0), At(s.sLeft, -hw, y0), up * -1, 0, 4);
                Quad(At(0, hw, y0), At(0, hw, y1), At(s.sRight, hw, y1), At(s.sRight, hw, y0), lft, 0, 4);
                Quad(At(0, -hw, y0), At(s.sLeft, -hw, y0), At(s.sLeft, -hw, y1), At(0, -hw, y1), lft * -1, 0, 4);
            }
            M.Weld();
            return M;
        }
    }
}
