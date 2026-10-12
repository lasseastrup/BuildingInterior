#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Triband.Storey.Lod;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// A detail mesh in the upload's vertex layout (Plan §6.5, <c>MeshUpload</c>), packed on the thread that built it
    /// (docs/CITY.md §7): a finished build waits for its upload at the size of its GPU meshes, not the generator's
    /// several times that, and the main thread only fills in two things and copies. Stream 0 is <see cref="Vertex"/>,
    /// LOD0's stream 1 <see cref="Cutaway"/>. What only the main thread knows, <see cref="Finish"/> fills in: each
    /// vertex's colour row (until then the vertex holds the index of its style in <see cref="Styles"/>) and its global
    /// wall id (the building's block of walls in the table).
    /// </summary>
    public sealed class PackedMesh
    {
        /// <summary>
        /// Stream 0, 24 bytes: position; SNorm8 normal (w: a pane's v); the colour reference: row low byte, row high byte,
        /// slot, tone × 128 (a pane's number and u in b and a); the building tag.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public float x, y, z;
            public sbyte nx, ny, nz, nw;
            public byte r, g, b, a;
            public float tag;
        }

        /// <summary>LOD0's stream 1, 8 bytes: the cutaway kind and the wall id (the building's own, 1-based, until <see cref="Finish"/>).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Cutaway { public float kind, wid; }

        /// <summary>The vertices, in <see cref="Chunks"/> given back when the mesh is uploaded (<see cref="Release"/>).</summary>
        public ChunkList<Vertex> V { get; private set; }
        /// <summary>LOD0's cutaway stream, a vertex each; null for a lean mesh (LOD1).</summary>
        public ChunkList<Cutaway>? Cut { get; private set; }
        /// <summary>
        /// The indices: 16-bit (<see cref="I16"/>) when there are 65,535 vertices or fewer, else 32-bit (one array: only a
        /// mesh packed whole has as many vertices, <see cref="MeshPack.Pack"/>'s <c>maxVerts</c>).
        /// </summary>
        public ChunkList<ushort>? I16 { get; private set; }
        public int[]? I32 { get; private set; }
        /// <summary>The styles whose colour rows the vertices name, by index.</summary>
        public readonly StyleRef[] Styles;
        /// <summary>
        /// The buildings whose party walls the mesh carries, as its cutaway kinds name them (table rows once packed with the
        /// site's block): the cutaway of any of them cuts this mesh too (<see cref="Occlusion.Steady"/>). Empty for none.
        /// </summary>
        public int[] Mates { get; internal set; } = Array.Empty<int>();
        /// <summary>The bounds of the positions.</summary>
        public readonly float X0, Y0, Z0, X1, Y1, Z1;
        bool finished;

        public int Verts { get; }
        public int Indices { get; }

        internal PackedMesh(ChunkList<Vertex> v, ChunkList<Cutaway>? cut, ChunkList<ushort>? i16, int[]? i32, int indices, StyleRef[] styles)
        {
            V = v; Cut = cut; I16 = i16; I32 = i32; Styles = styles; Verts = v.Count; Indices = indices;
            float x0 = float.MaxValue, y0 = x0, z0 = x0, x1 = float.MinValue, y1 = x1, z1 = x1;
            for (int c = 0, at = 0; at < Verts; c++, at += Chunks.Size)
            {
                var a = v.Chunk(c); int m = Math.Min(Chunks.Size, Verts - at);
                for (int i = 0; i < m; i++) { var q = a[i]; x0 = Math.Min(x0, q.x); y0 = Math.Min(y0, q.y); z0 = Math.Min(z0, q.z); x1 = Math.Max(x1, q.x); y1 = Math.Max(y1, q.y); z1 = Math.Max(z1, q.z); }
            }
            if (Verts == 0) x0 = y0 = z0 = x1 = y1 = z1 = 0;
            X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1;
        }

        /// <summary>
        /// Uploaded: the chunks go back to <see cref="Chunks"/> for the next build. Nothing may read them after; the mesh
        /// is left empty.
        /// </summary>
        public void Release()
        {
            V.Clear(); Cut?.Clear(); I16?.Clear();
            Cut = null; I16 = null; I32 = null;
        }

        /// <summary>
        /// Fill in what only the main thread knows: <paramref name="rows"/>, the colour row of each of <see cref="Styles"/>,
        /// and <paramref name="wallBase"/>, the first global id of the building's walls (-1: none). Once, before the upload.
        /// </summary>
        public void Finish(int[] rows, int wallBase)
        {
            if (finished) throw new InvalidOperationException("a packed mesh is finished once");
            if (rows.Length != Styles.Length) throw new ArgumentException($"{rows.Length} rows for {Styles.Length} styles", nameof(rows));
            finished = true;
            var lo = new byte[rows.Length]; var hi = new byte[rows.Length];
            for (int s = 0; s < rows.Length; s++) { var (r, g, _, _) = ColorRows.EncodeVertex(rows[s], ColorSlot.Wall, 1); lo[s] = r; hi[s] = g; }
            int n = Verts;
            for (int k = 0, at = 0; at < n; k++, at += Chunks.Size)
            {
                var v = V.Chunk(k); int m = Math.Min(Chunks.Size, n - at);
                for (int i = 0; i < m; i++) { int s = v[i].r | v[i].g << 8; v[i].r = lo[s]; v[i].g = hi[s]; }
            }
            if (Cut == null) return;
            for (int k = 0, at = 0; at < n; k++, at += Chunks.Size)
            {
                var c = Cut.Chunk(k); int m = Math.Min(Chunks.Size, n - at);
                for (int i = 0; i < m; i++) { float wi = c[i].wid; c[i].wid = wi > 0 && wallBase >= 0 ? wallBase + wi - 1 : 0; }
            }
        }
    }

    /// <summary>Packs a welded <see cref="MeshBuilder"/> into <see cref="PackedMesh"/>es: engine-free, so on a worker thread.</summary>
    public static class MeshPack
    {
        /// <summary>The most vertices 16-bit indices reach.</summary>
        public const int MaxVerts = 65535;

        static sbyte S8(double v) => (sbyte)Math.Round(Math.Max(-1, Math.Min(1, v)) * 127);

        /// <summary>
        /// A value as a 16-bit float's bits (IEEE 754 binary16, rounded to nearest, ties to even, as the GPU reads it):
        /// for vertex attributes the shader reads as floats, at half the size. 11 significant bits: 3 cm at 64 to 128.
        /// </summary>
        public static ushort Half(double value)
        {
            uint x = (uint)BitConverter.SingleToInt32Bits((float)value);
            uint sign = (x >> 16) & 0x8000, exp = (x >> 23) & 0xff, mant = x & 0x7fffff;
            if (exp == 0xff) return (ushort)(sign | 0x7c00 | (mant != 0 ? 0x200u : 0));   // infinity, NaN
            int e = (int)exp - 127 + 15;
            if (e >= 0x1f) return (ushort)(sign | 0x7c00);   // past 65,504: infinity
            if (e <= 0)
            {
                // a subnormal half, or zero
                if (e < -10) return (ushort)sign;
                mant |= 0x800000; int shift = 14 - e;
                uint sub = mant >> shift, rest = mant & ((1u << shift) - 1), mid = 1u << (shift - 1);
                if (rest > mid || (rest == mid && (sub & 1) != 0)) sub++;
                return (ushort)(sign | sub);
            }
            uint h = ((uint)e << 10) | (mant >> 13), r = mant & 0x1fff;
            if (r > 0x1000 || (r == 0x1000 && (h & 1) != 0)) h++;   // a carry into the exponent is right
            return (ushort)(sign | h);
        }

        /// <summary>The value a 16-bit float's bits stand for.</summary>
        public static double FromHalf(ushort h)
        {
            int sign = (h & 0x8000) != 0 ? -1 : 1, exp = (h >> 10) & 0x1f, mant = h & 0x3ff;
            if (exp == 0) return sign * mant * Math.Pow(2, -24);
            if (exp == 0x1f) return mant == 0 ? sign * double.PositiveInfinity : double.NaN;
            return sign * (1 + mant / 1024.0) * Math.Pow(2, exp - 15);
        }

        /// <summary>
        /// The builder (welded first if it isn't, unless <paramref name="weld"/> is false) in the upload's layout, in parts
        /// of at most <paramref name="maxVerts"/> vertices, each with 16-bit indices: the triangles in order, a part closed
        /// when the next would take it past, the vertices a triangle shares with the part before copied into its own. A
        /// mesh that fits is one part, its vertices as built. None for a mesh without triangles. <paramref name="windows"/>:
        /// panes carry their place on the pane (the opaque meshes and the glass, not the massing). <paramref name="mates"/>:
        /// where the site's buildings sit in the table, for party walls (<see cref="MateIndex.Shift"/>).
        /// </summary>
        /// <param name="weld">
        /// False for LOD0 and its glass: the generator already shares their vertices, and welding found under half a
        /// percent more while taking a fifth to a quarter of a worker's build (docs/CITY.md §7, WeldTests).
        /// </param>
        public static List<PackedMesh> Pack(MeshBuilder gb, bool windows = false, (int start, int count)? mates = null, int maxVerts = MaxVerts, bool weld = true)
        {
            if (weld && !gb.Welded) gb.Weld();
            int nv = gb.Verts, ni = gb.I.Count;
            var out_ = new List<PackedMesh>(1);
            if (ni == 0) return out_;
            // a bad index crashes natively on the GPU: a managed exception here, where it says which
            if (ni % 3 != 0) throw new InvalidOperationException($"{ni} indices is not whole triangles");
            var I = gb.I;
            for (int i = 0; i < ni; i++) if ((uint)I[i] >= (uint)nv) throw new InvalidOperationException($"index {I[i]} of {nv} vertices");

            var pk = packer ??= new Packer();
            pk.Start(gb, windows, mates);
            try
            {
                // each normal and colour of the builder's tables packed once; the styles numbered in the order the vertices
                // first name them (a vertex holds its style's number until Finish gives it the style's colour row); and the
                // neighbours the party walls name, in the order first named. (The builder's lists, and the packed mesh's, are
                // chunked alike: element i is in chunk i >> Chunks.Shift.)
                pk.Entries();
                for (int c = 0, at = 0; at < nv; c++, at += Chunks.Size)
                {
                    var C = gb.C.Index.Chunk(c); int m = Math.Min(Chunks.Size, nv - at);
                    for (int j = 0; j < m; j++) pk.Name(C[j]);
                }
                if (pk.Styles.Count > 65536) throw new InvalidOperationException($"{pk.Styles.Count} styles in one mesh");
                var neighbours = Array.Empty<int>();
                if (!gb.Lean)
                {
                    for (int i = 0; i < nv; i++) { int k = pk.Kind(gb.K[i]); if (k >= 8) { int m = (k >> 3) - 1; if (!pk.Found.Contains(m)) pk.Found.Add(m); } }
                    if (pk.Found.Count > 0) neighbours = pk.Found.ToArray();
                }
                var st = pk.Styles.ToArray();

                if (nv <= maxVerts)
                {
                    var v = new ChunkList<PackedMesh.Vertex>(); v.Resize(nv);
                    var cut = gb.Lean ? null : new ChunkList<PackedMesh.Cutaway>(); cut?.Resize(nv);
                    for (int c = 0, at = 0; at < nv; c++, at += Chunks.Size)
                    {
                        var P = gb.P.Chunk(c); var N = gb.N.Index.Chunk(c); var C = gb.C.Index.Chunk(c); var V = v.Chunk(c); int m = Math.Min(Chunks.Size, nv - at);
                        for (int j = 0; j < m; j++) pk.Vertex(at + j, in P[j], N[j], C[j], ref V[j]);
                        if (cut == null) continue;
                        var K = gb.K.Chunk(c); var W = gb.WI.Chunk(c); var U = cut.Chunk(c);
                        for (int j = 0; j < m; j++) U[j] = new PackedMesh.Cutaway { kind = pk.Kind(K[j]), wid = W[j] };
                    }
                    if (nv <= MaxVerts)
                    {
                        var i16 = new ChunkList<ushort>(); i16.Resize(ni);
                        for (int c = 0, at = 0; at < ni; c++, at += Chunks.Size)
                        {
                            var src = I.Chunk(c); var dst = i16.Chunk(c); int m = Math.Min(Chunks.Size, ni - at);
                            for (int j = 0; j < m; j++) dst[j] = (ushort)src[j];
                        }
                        out_.Add(new PackedMesh(v, cut, i16, null, ni, st) { Mates = neighbours });
                    }
                    else out_.Add(new PackedMesh(v, cut, null, I.ToArray(), ni, st) { Mates = neighbours });
                    return out_;
                }

                // in parts: each vertex's place in the part being filled (-1: not in it). A vertex is packed into each part
                // that takes it, as it would be packed whole.
                int cap = Math.Min(maxVerts, MaxVerts);
                var local = pk.Local; local.Resize(nv); for (int i = 0; i < nv; i++) local[i] = -1;
                var pv = pk.Pv; var pi = pk.Pi;
                void Close()
                {
                    int n = pv.Count, m = pi.Count;
                    var v = new ChunkList<PackedMesh.Vertex>(); v.Resize(n);
                    var c = gb.Lean ? null : new ChunkList<PackedMesh.Cutaway>(); c?.Resize(n);
                    for (int k = 0; k < n; k++) { int x = pv[k]; pk.Vertex(x, in gb.P[x], gb.N.Index[x], gb.C.Index[x], ref v[k]); if (c != null) c[k] = new PackedMesh.Cutaway { kind = pk.Kind(gb.K[x]), wid = gb.WI[x] }; local[x] = -1; }
                    var ix = new ChunkList<ushort>(); ix.Resize(m); for (int k = 0; k < m; k++) ix[k] = (ushort)pi[k];
                    out_.Add(new PackedMesh(v, c, ix, null, m, st) { Mates = neighbours });
                    pv.Clear(); pi.Clear();
                }
                for (int t = 0; t < ni; t += 3)
                {
                    int a = I[t], b = I[t + 1], c = I[t + 2];
                    int need = (local[a] < 0 ? 1 : 0) + (local[b] < 0 && b != a ? 1 : 0) + (local[c] < 0 && c != a && c != b ? 1 : 0);
                    if (pv.Count + need > cap) Close();
                    Take(a); Take(b); Take(c);
                }
                void Take(int x)
                {
                    if (local[x] < 0) { local[x] = pv.Count; pv.Add(x); }
                    pi.Add(local[x]);
                }
                if (pi.Count > 0) Close();
                return out_;
            }
            finally { pk.Done(); }
        }

        // what packing a mesh works with, kept by each thread for its next (the lists' chunks go back after each mesh)
        [ThreadStatic] static Packer? packer;

        sealed class Packer
        {
            public readonly Dictionary<StyleRef, int> StyleIx = new Dictionary<StyleRef, int>();
            public readonly List<StyleRef> Styles = new List<StyleRef>();
            public readonly List<int> Found = new List<int>();
            public readonly ChunkList<int> Local = new ChunkList<int>(), Pv = new ChunkList<int>(), Pi = new ChunkList<int>();
            // per entry of the builder's tables: a colour's style number (-1 until a vertex names it), slot and tone bytes, and
            // whether it is a pane's; a normal's packed bytes
            struct Colour { public int style; public byte b, a; public bool glass; }
            struct Normal { public sbyte x, y, z; }
            Colour[] colours = new Colour[64];
            Normal[] normals = new Normal[64];
            MeshBuilder gb = null!;
            bool windows;
            float tag;
            (int start, int count)? mates;
            StyleRef last;
            int lastIx;

            public void Start(MeshBuilder gb, bool windows, (int start, int count)? mates)
            {
                this.gb = gb; this.windows = windows; this.mates = mates; tag = gb.Tag;
                StyleIx.Clear(); Styles.Clear(); Found.Clear(); lastIx = -1;
            }

            public void Done() { gb = null!; Local.Clear(); Pv.Clear(); Pi.Clear(); }

            /// <summary>The builder's normals and colours packed, once each.</summary>
            public void Entries()
            {
                var cv = gb.C.Values; var nvs = gb.N.Values;
                if (colours.Length < cv.Count) colours = new Colour[Math.Max(cv.Count, colours.Length * 2)];
                if (normals.Length < nvs.Count) normals = new Normal[Math.Max(nvs.Count, normals.Length * 2)];
                for (int e = 0; e < cv.Count; e++)
                {
                    var s = cv[e]; var (_, _, cb, ca) = ColorRows.EncodeVertex(0, s.slot, s.tone);   // the slot and tone, checked
                    colours[e] = new Colour { style = -1, b = cb, a = ca, glass = s.slot == ColorSlot.Glass };
                }
                for (int e = 0; e < nvs.Count; e++) { var n = nvs[e]; normals[e] = new Normal { x = S8(n.x), y = S8(n.y), z = S8(n.z) }; }
            }

            /// <summary>A vertex names colour entry <paramref name="e"/>: its style is numbered if this is the first to.</summary>
            public void Name(int e) { if (colours[e].style < 0) colours[e].style = Style(gb.C.Values[e].style); }

            /// <summary>A style's number, given the next one the first time it is named.</summary>
            public int Style(StyleRef s)
            {
                if (lastIx >= 0 && s.Equals(last)) return lastIx;
                if (!StyleIx.TryGetValue(s, out int si)) { si = Styles.Count; StyleIx[s] = si; Styles.Add(s); }
                last = s; lastIx = si;
                return si;
            }

            /// <summary>A vertex's cutaway kind as packed: a party wall's neighbour moved into the table's block.</summary>
            public int Kind(int k) => mates is (int ms, int mc) ? MateIndex.Shift(k, ms, mc) : k;

            /// <summary>
            /// Vertex <paramref name="i"/> (at <paramref name="p"/>, its normal and colour entries <paramref name="ne"/> and
            /// <paramref name="ce"/>) in the upload's layout, every field written (the chunk held another's).
            /// </summary>
            public void Vertex(int i, in P3 p, int ne, int ce, ref PackedMesh.Vertex v)
            {
                ref var c = ref colours[ce]; ref var n = ref normals[ne];
                int si = c.style;
                v = default;
                v.x = (float)p.x; v.y = (float)p.y; v.z = (float)p.z;
                v.nx = n.x; v.ny = n.y; v.nz = n.z;
                v.r = (byte)(si & 255); v.g = (byte)(si >> 8); v.b = c.b; v.a = c.a; v.tag = tag;
                if (windows && c.glass && gb.PaneUV.TryGetValue(i, out var uv)) { var (pa, pw, pb) = ColorRows.EncodePane(uv.u, uv.v, uv.id); v.a = pa; v.nw = pw; v.b = pb; }
            }
        }
    }
}
