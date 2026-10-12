#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>A solid the geometry stands for, kept for the coplanar-face checker (a face buried in one is not visible).</summary>
    public sealed class Solid
    {
        public Frame F;
        public bool Mitred;
        public double u0, u1, sA, kA, eB, kB, y0, y1, w0, w1;
        public List<Vec2>? Prism;

        public bool Contains(P3 p)
        {
            if (Prism != null) return p.y > y0 + 1e-4 && p.y < y1 - 1e-4 && Geo.Pip(Prism, p.x, p.z);
            if (p.y <= y0 + 1e-4 || p.y >= y1 - 1e-4) return false;
            double dx = p.x - F.O.x, dz = p.z - F.O.z, a = dx * F.u.x + dz * F.u.z, b = dx * F.w.x + dz * F.w.z;
            if (Mitred) return b > w0 + 1e-4 && b < w1 - 1e-4 && a > sA + kA * b + 1e-4 && a < eB + kB * b - 1e-4;
            return a > u0 + 1e-4 && a < u1 - 1e-4 && b > w0 + 1e-4 && b < w1 - 1e-4;
        }
    }

    /// <summary>
    /// Geometry builder: boxes and polygons with per-vertex colour, building tag and occlusion
    /// data (the prototype's <c>GB</c>). Positions are doubles here; the Unity layer packs them.
    /// The lists are made of <see cref="Chunks"/>, given back when the builder is recycled.
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly ChunkList<P3> P = new ChunkList<P3>();
        /// <summary>Normal per vertex, an index into the builder's table of its normals (a building has few), each as made.</summary>
        public readonly IndexedList<P3> N = new IndexedList<P3>(P3Bits.Instance);
        /// <summary>
        /// Colour per vertex, by reference (docs/COLOURS.md §3.2); <see cref="ColorResolver"/> gives the RGB. An index into the
        /// builder's table of its swatches.
        /// </summary>
        public readonly IndexedList<Swatch> C = new IndexedList<Swatch>(EqualityComparer<Swatch>.Default);
        /// <summary>Kind per vertex: 0 slab/roof, 1 wall, 2 tall part; + 8 × (neighbour index + 1) for party walls.</summary>
        public readonly ChunkList<int> K = new ChunkList<int>();
        /// <summary>Wall id per vertex, 1-based within the building; 0 = none.</summary>
        public readonly ChunkList<int> WI = new ChunkList<int>();
        public readonly ChunkList<int> I = new ChunkList<int>();
        /// <summary>The walls given ids, in id order.</summary>
        public List<(double[] W, int K)> Walls = new List<(double[], int)>();
        /// <summary>The floor height of the storey each wall in <see cref="Walls"/> was made on (the editor slides only the active storey's).</summary>
        public List<double> WallBase = new List<double>();
        /// <summary>The floor height of the storey being generated, recorded with each wall.</summary>
        public double StoreyBase;
        public List<Solid>? Solids;
        /// <summary>
        /// Where on its window pane a vertex is, 0 to 1 across and up (vertex index → u, v): the window shader maps a
        /// room behind the pane by it (docs/EDITOR.md §6.8). Only pane vertices have one.
        /// </summary>
        public readonly Dictionary<int, (double u, double v, byte id)> PaneUV = new Dictionary<int, (double u, double v, byte id)>();
        /// <summary>Building index + 65536 × LOD, written into every vertex at upload.</summary>
        public int Tag { get; private set; }
        public readonly bool Lean;
        /// <summary>
        /// Keeps no geometry: the walls are numbered as always (<see cref="Ctx"/>), every triangle is dropped. What
        /// <see cref="Lod0.BuildShape"/> builds with, for the segments and the wall list alone.
        /// </summary>
        public readonly bool ShapeOnly;

        /// <summary>Change the tag before upload: the generator tags with the site index, a table with its own.</summary>
        public void RetagForUpload(int tag) => Tag = tag;

        double[] curW = Zero4;
        int curK, curI;
        public static readonly double[] Zero4 = { 0, 0, 0, 0 };

        public MeshBuilder(int tag = 0, bool lean = false, bool shapeOnly = false) { Tag = tag; Lean = lean; ShapeOnly = shapeOnly; }

        // ---- reuse (docs/CITY.md §7) ----

        /// <summary>The builders kept for the next <see cref="Make"/>, their lists empty, on any thread (a lock each way).</summary>
        const int Spares = 16;
        static readonly List<MeshBuilder> spares = new List<MeshBuilder>();

        /// <summary>
        /// A builder as the constructor makes it: one let go with <see cref="Recycle"/> when there is one, else a new one.
        /// Either way its lists take their memory from <see cref="Chunks"/>, a chunk at a time.
        /// </summary>
        public static MeshBuilder Make(int tag = 0, bool lean = false, bool shapeOnly = false)
        {
            if (!shapeOnly)
                lock (spares)
                    for (int k = spares.Count - 1; k >= 0; k--)
                        if (spares[k].Lean == lean) { var m = spares[k]; spares.RemoveAt(k); m.Tag = tag; return m; }
            return new MeshBuilder(tag, lean, shapeOnly);
        }

        /// <summary>
        /// Done with this builder (its meshes packed): its lists give their chunks back for the next build
        /// (<see cref="Chunks"/>), and the builder is kept for the next <see cref="Make"/>, on any thread. Nothing may use
        /// it after. What it handed out stays as it was: the wall lists (a LOD0's shape holds them) are replaced, not emptied.
        /// </summary>
        public void Recycle()
        {
            if (ShapeOnly) return;
            lock (spares) if (spares.Contains(this)) return;
            P.Clear(); N.Clear(); C.Clear(); K.Clear(); WI.Clear(); I.Clear(); PaneUV.Clear();
            Walls = new List<(double[], int)>(); WallBase = new List<double>(); Solids = null;
            StoreyBase = 0; curW = Zero4; curK = curI = 0; Welded = false;
            lock (spares) if (spares.Count < Spares) spares.Add(this);
        }

        /// <summary>Let go of the builders kept (the tests' allocation counts start from none).</summary>
        public static void ForgetSpares() { lock (spares) spares.Clear(); }

        /// <summary>What <see cref="Facade.WallPanel"/> works in, kept with the builder for its next panel.</summary>
        internal Facade.PanelScratch? Panel;

        public int Tris => I.Count / 3;
        public int Verts => P.Count;

        /// <summary>
        /// Merge vertices that are the same in every attribute (position, normal, colour, and the cutaway's wall, kind and
        /// id), keeping the first of each, and rewrite the indices. Faces are flat-shaded, so only vertices of coplanar
        /// faces of one colour ever meet; the generator already shares those within a polygon, this catches the rest.
        /// Returns how many went. The triangles are unchanged.
        /// </summary>
        public int Weld()
        {
            Welded = true;
            int n = P.Count;
            if (n == 0) return 0;
            // each vertex's first equal, through a table of vertex indices (open addressing, at least twice as many slots as
            // vertices; 0 is empty, else the index + 1): no keys copied, a few bytes a vertex, in chunks given back after
            int size = 4; while (size < n * 2) size <<= 1;
            var slots = weldSlots ??= new ChunkList<int>(); var map = weldMap ??= new ChunkList<int>();   // the builder's own: empty between welds
            try
            {
                slots.Resize(size); for (int c = 0; c < slots.ChunkCount; c++) Array.Clear(slots.Chunk(c), 0, Chunks.Size);
                map.Resize(n);
                int kept = 0;
                for (int i = 0; i < n; i++)
                    for (int h = Hash(i) & (size - 1); ; h = (h + 1) & (size - 1))
                    {
                        int s = slots[h];
                        if (s == 0) { slots[h] = i + 1; map[i] = kept++; break; }
                        if (Same(i, s - 1)) { map[i] = map[s - 1]; break; }
                    }
                int gone = n - kept;
                if (gone == 0) return 0;
                // in place: the kept vertices move down, in order (a later copy of one already kept maps below the next place)
                int at = 0;
                for (int i = 0; i < n; i++)
                {
                    if (map[i] != at) continue;
                    if (at != i) { P[at] = P[i]; N.Index[at] = N.Index[i]; C.Index[at] = C.Index[i]; if (!Lean) { K[at] = K[i]; WI[at] = WI[i]; } }
                    at++;
                }
                P.Truncate(kept); N.Truncate(kept); C.Truncate(kept);
                if (!Lean) { K.Truncate(kept); WI.Truncate(kept); }
                for (int t = 0; t < I.Count; t++) I[t] = map[I[t]];
                if (PaneUV.Count > 0)
                {
                    // each pane vertex's place moves with it, the first of those welded into one kept
                    var panes = weldPanes ??= new List<KeyValuePair<int, (double, double, byte)>>();
                    foreach (var kv in PaneUV) panes.Add(kv);
                    PaneUV.Clear();
                    foreach (var kv in panes) { int to = map[kv.Key]; if (!PaneUV.ContainsKey(to)) PaneUV[to] = kv.Value; }
                    panes.Clear();
                }
                return gone;
            }
            finally { slots.Clear(); map.Clear(); }
        }

        ChunkList<int>? weldSlots, weldMap;
        List<KeyValuePair<int, (double u, double v, byte id)>>? weldPanes;

        /// <summary>Set by <see cref="Weld"/> (a build thread welds before the upload; the upload then skips it). Add nothing after.</summary>
        public bool Welded { get; private set; }

        // a vertex's identity for Weld: position, normal, colour, and the cutaway's kind and wall id (the id stands for
        // the wall's data: one id, one wall). Exact: the same doubles, as the generator made them (0 and −0 alike, as == has
        // them); a colour by its entry in the builder's table, which holds each swatch once, equal as Swatch.Equals has it
        int Hash(int i)
        {
            var p = P[i]; var q = N[i];
            return HashCode.Combine(p.x, p.y, p.z, q.x, q.y, q.z, C.Index[i], Lean ? 0 : HashCode.Combine(K[i], WI[i]));
        }

        bool Same(int i, int j)
        {
            P3 a = P[i], b = P[j];
            if (a.x != b.x || a.y != b.y || a.z != b.z) return false;
            if (C.Index[i] != C.Index[j] || (!Lean && (K[i] != K[j] || WI[i] != WI[j]))) return false;
            if (N.Index[i] == N.Index[j]) return true;
            a = N[i]; b = N[j];
            return a.x == b.x && a.y == b.y && a.z == b.z;
        }

        /// <summary>Set the cutaway context for what follows. A wall (kind 1) gets an id so it can slide.</summary>
        public MeshBuilder Ctx(double[]? w, int k)
        {
            curW = w ?? Zero4; curK = k;
            if (!Lean && curK % 8 == 1) { Walls.Add(((double[])curW.Clone(), curK)); WallBase.Add(StoreyBase); curI = Walls.Count; }
            else curI = 0;
            return this;
        }

        void V(P3 q, P3 nr, Swatch c)
        {
            P.Add(q); N.Add(nr); C.Add(c);
            if (!Lean) { K.Add(curK); WI.Add(curI); }
        }

        /// <summary>
        /// Triangles as they come (an artist's mesh: <see cref="OpeningKinds"/>): one vertex per point with its own normal,
        /// the winding as given (front faces wind as the generator's own). Returns the first new vertex.
        /// </summary>
        public int Mesh(IReadOnlyList<P3> pts, IReadOnlyList<P3> nrm, IReadOnlyList<int> tris, Swatch c)
        {
            int bse = P.Count;
            if (ShapeOnly) return bse;
            for (int i = 0; i < pts.Count; i++) V(pts[i], nrm[i], c);
            foreach (int t in tris) I.Add(bse + t);
            return bse;
        }

        /// <summary>A convex planar polygon, fanned; winding fixed to face along <paramref name="nr"/>.</summary>
        public void Poly(P3[] pts, P3 nr, Swatch c)
        {
            if (ShapeOnly) return;
            P3 a = pts[0], b = pts[1], d = pts[2];
            var cr = P3.Cross(b - a, d - a);
            if (P3.Dot(cr, nr) < 0) { pts = (P3[])pts.Clone(); Array.Reverse(pts); }
            int bse = P.Count;
            foreach (var q in pts) V(q, nr, c);
            for (int j = 1; j < pts.Length - 1; j++) { I.Add(bse); I.Add(bse + j); I.Add(bse + j + 1); }
        }

        /// <summary><see cref="Poly"/> for a quad, without an array: the same vertices, in the same order.</summary>
        public void Quad(P3 p0, P3 p1, P3 p2, P3 p3, P3 nr, Swatch c)
        {
            if (ShapeOnly) return;
            if (P3.Dot(P3.Cross(p1 - p0, p2 - p0), nr) < 0) { var t = p0; p0 = p3; p3 = t; t = p1; p1 = p2; p2 = t; }
            int bse = P.Count;
            V(p0, nr, c); V(p1, nr, c); V(p2, nr, c); V(p3, nr, c);
            I.Add(bse); I.Add(bse + 1); I.Add(bse + 2); I.Add(bse); I.Add(bse + 2); I.Add(bse + 3);
        }

        /// <summary>A triangulated planar polygon (earcut output): one vertex per corner, shared by its triangles.</summary>
        public void PolyTris(List<P3> pts, List<int> tris, P3 nr, Swatch c)
        {
            if (tris.Count == 0 || ShapeOnly) return;
            int bse = P.Count;
            foreach (var q in pts) V(q, nr, c);
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], d = tris[t + 2];
                P3 A = pts[a], B = pts[b], D = pts[d];
                if (P3.Dot(P3.Cross(B - A, D - A), nr) < 0) { int x = b; b = d; d = x; }
                I.Add(bse + a); I.Add(bse + b); I.Add(bse + d);
            }
        }

        /// <summary>An axis-aligned box in frame F: u0..u1 along, y0..y1 up, w0..w1 across.</summary>
        public void OBox(Frame F, double u0, double u1, double y0, double y1, double w0, double w1, Swatch c, Swatch? cIn = null, Skip skip = Skip.None)
        {
            if (u1 - u0 < 1e-4 || y1 - y0 < 1e-4 || w1 - w0 < 1e-4 || ShapeOnly) return;
            Swatch ci = cIn ?? c;
            Solids?.Add(new Solid { F = F, u0 = u0, u1 = u1, y0 = y0, y1 = y1, w0 = w0, w1 = w1 });
            P3 Pt(double a, double y, double b) => F.At(a, y, b);
            var nu = new P3(F.u.x, 0, F.u.z); var nw = new P3(F.w.x, 0, F.w.z);
            if ((skip & Skip.Out) == 0) Quad(Pt(u0, y0, w1), Pt(u1, y0, w1), Pt(u1, y1, w1), Pt(u0, y1, w1), nw, c);
            if ((skip & Skip.In) == 0) Quad(Pt(u0, y0, w0), Pt(u1, y0, w0), Pt(u1, y1, w0), Pt(u0, y1, w0), new P3(-nw.x, 0, -nw.z), ci);
            if ((skip & Skip.UEnd) == 0) Quad(Pt(u1, y0, w0), Pt(u1, y0, w1), Pt(u1, y1, w1), Pt(u1, y1, w0), nu, c);
            if ((skip & Skip.UStart) == 0) Quad(Pt(u0, y0, w0), Pt(u0, y0, w1), Pt(u0, y1, w1), Pt(u0, y1, w0), new P3(-nu.x, 0, -nu.z), c);
            if ((skip & Skip.Top) == 0) Quad(Pt(u0, y1, w0), Pt(u1, y1, w0), Pt(u1, y1, w1), Pt(u0, y1, w1), new P3(0, 1, 0), c);
            if ((skip & Skip.Bot) == 0) Quad(Pt(u0, y0, w0), Pt(u1, y0, w0), Pt(u1, y0, w1), Pt(u0, y0, w1), new P3(0, -1, 0), c);
        }

        /// <summary>A box whose start and end faces are slanted: u_start(w) = sA + kA·w, u_end(w) = eB + kB·w.</summary>
        public void MBox(Frame F, double sA, double kA, double eB, double kB, double y0, double y1, double w0, double w1, Swatch c, Swatch? cIn = null, Skip skip = Skip.None)
        {
            double s0 = sA + kA * w0, s1 = sA + kA * w1, e0 = eB + kB * w0, e1 = eB + kB * w1;
            if (e0 - s0 < 1e-4 || e1 - s1 < 1e-4 || y1 - y0 < 1e-4 || w1 - w0 < 1e-4 || ShapeOnly) return;
            Swatch ci = cIn ?? c;
            Solids?.Add(new Solid { F = F, Mitred = true, sA = sA, kA = kA, eB = eB, kB = kB, y0 = y0, y1 = y1, w0 = w0, w1 = w1 });
            P3 Pt(double a, double y, double b) => F.At(a, y, b);
            var nw = new P3(F.w.x, 0, F.w.z);
            if ((skip & Skip.Out) == 0) Quad(Pt(s1, y0, w1), Pt(e1, y0, w1), Pt(e1, y1, w1), Pt(s1, y1, w1), nw, c);
            if ((skip & Skip.In) == 0) Quad(Pt(s0, y0, w0), Pt(e0, y0, w0), Pt(e0, y1, w0), Pt(s0, y1, w0), new P3(-nw.x, 0, -nw.z), ci);
            if ((skip & Skip.UEnd) == 0) Quad(Pt(e0, y0, w0), Pt(e1, y0, w1), Pt(e1, y1, w1), Pt(e0, y1, w0), Geo.EndNormal(F, e0, e1, w0, w1, 1), c);
            if ((skip & Skip.UStart) == 0) Quad(Pt(s0, y0, w0), Pt(s1, y0, w1), Pt(s1, y1, w1), Pt(s0, y1, w0), Geo.EndNormal(F, s0, s1, w0, w1, -1), c);
            if ((skip & Skip.Top) == 0) Quad(Pt(s0, y1, w0), Pt(e0, y1, w0), Pt(e1, y1, w1), Pt(s1, y1, w1), new P3(0, 1, 0), c);
            if ((skip & Skip.Bot) == 0) Quad(Pt(s0, y0, w0), Pt(e0, y0, w0), Pt(e1, y0, w1), Pt(s1, y0, w1), new P3(0, -1, 0), c);
        }
    }
}
