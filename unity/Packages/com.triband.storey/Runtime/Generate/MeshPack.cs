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

        /// <summary>The vertices, the first <see cref="Verts"/> of the array (it may be longer: <see cref="ArrayShelf"/>).</summary>
        public Vertex[] V { get; private set; }
        /// <summary>LOD0's cutaway stream, the first <see cref="Verts"/>; null for a lean mesh (LOD1).</summary>
        public Cutaway[]? Cut { get; private set; }
        /// <summary>The indices, the first <see cref="Indices"/>: 16-bit (<see cref="I16"/>) when there are 65,535 vertices or fewer, else 32-bit.</summary>
        public ushort[]? I16 { get; private set; }
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

        internal PackedMesh(Vertex[] v, int verts, Cutaway[]? cut, ushort[]? i16, int[]? i32, int indices, StyleRef[] styles)
        {
            V = v; Cut = cut; I16 = i16; I32 = i32; Styles = styles; Verts = verts; Indices = indices;
            float x0 = float.MaxValue, y0 = x0, z0 = x0, x1 = float.MinValue, y1 = x1, z1 = x1;
            for (int i = 0; i < verts; i++) { var q = v[i]; x0 = Math.Min(x0, q.x); y0 = Math.Min(y0, q.y); z0 = Math.Min(z0, q.z); x1 = Math.Max(x1, q.x); y1 = Math.Max(y1, q.y); z1 = Math.Max(z1, q.z); }
            if (verts == 0) x0 = y0 = z0 = x1 = y1 = z1 = 0;
            X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1;
        }

        /// <summary>
        /// Uploaded: the arrays go back to <see cref="ArrayShelf"/> for the next build's packing. Nothing may read them
        /// after; the mesh is left empty.
        /// </summary>
        public void Release()
        {
            ArrayShelf<Vertex>.Return(V); ArrayShelf<Cutaway>.Return(Cut); ArrayShelf<ushort>.Return(I16);
            V = Array.Empty<Vertex>(); Cut = null; I16 = null; I32 = null;
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
            var v = V; int n = Verts;
            for (int i = 0; i < n; i++) { int s = v[i].r | v[i].g << 8; v[i].r = lo[s]; v[i].g = hi[s]; }
            if (Cut == null) return;
            var c = Cut;
            for (int i = 0; i < n; i++) { float wi = c[i].wid; c[i].wid = wi > 0 && wallBase >= 0 ? wallBase + wi - 1 : 0; }
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

            // every vertex once, with its style's index where its colour row goes; the arrays are rented (ArrayShelf), so
            // every field of every vertex is written
            var verts = ArrayShelf<PackedMesh.Vertex>.Rent(nv);
            var styleIx = new Dictionary<StyleRef, int>(); var styles = new List<StyleRef>();
            StyleRef last = default; int lastIx = -1;
            float tag = gb.Tag;
            for (int i = 0; i < nv; i++)
            {
                var p = gb.P[i]; var n = gb.N[i]; var s = gb.C[i];
                int si;
                if (lastIx >= 0 && s.style.Equals(last)) si = lastIx;
                else
                {
                    if (!styleIx.TryGetValue(s.style, out si)) { si = styles.Count; styleIx[s.style] = si; styles.Add(s.style); }
                    last = s.style; lastIx = si;
                }
                var (_, _, cb, ca) = ColorRows.EncodeVertex(0, s.slot, s.tone);   // the slot and tone, checked
                ref var v = ref verts[i];
                v = default;
                v.x = (float)p.x; v.y = (float)p.y; v.z = (float)p.z;
                v.nx = S8(n.x); v.ny = S8(n.y); v.nz = S8(n.z);
                v.r = (byte)(si & 255); v.g = (byte)(si >> 8); v.b = cb; v.a = ca; v.tag = tag;
                if (windows && s.slot == ColorSlot.Glass && gb.PaneUV.TryGetValue(i, out var uv)) { var (pa, pw, pb) = ColorRows.EncodePane(uv.u, uv.v, uv.id); v.a = pa; v.nw = pw; v.b = pb; }
            }
            if (styles.Count > 65536) throw new InvalidOperationException($"{styles.Count} styles in one mesh");
            PackedMesh.Cutaway[]? cut = null; var neighbours = Array.Empty<int>();
            if (!gb.Lean)
            {
                cut = ArrayShelf<PackedMesh.Cutaway>.Rent(nv);
                List<int>? found = null;
                for (int i = 0; i < nv; i++)
                {
                    int k = mates is (int ms, int mc) ? MateIndex.Shift(gb.K[i], ms, mc) : gb.K[i];
                    cut[i] = new PackedMesh.Cutaway { kind = k, wid = gb.WI[i] };
                    if (k >= 8) { int m = (k >> 3) - 1; found ??= new List<int>(); if (!found.Contains(m)) found.Add(m); }
                }
                if (found != null) neighbours = found.ToArray();
            }
            var st = styles.ToArray();

            if (nv <= maxVerts)
            {
                if (nv <= MaxVerts) { var i16 = ArrayShelf<ushort>.Rent(ni); for (int i = 0; i < ni; i++) i16[i] = (ushort)I[i]; out_.Add(new PackedMesh(verts, nv, cut, i16, null, ni, st) { Mates = neighbours }); }
                else out_.Add(new PackedMesh(verts, nv, cut, null, I.ToArray(), ni, st) { Mates = neighbours });
                return out_;
            }

            // in parts: each vertex's place in the part being filled (-1: not in it)
            int cap = Math.Min(maxVerts, MaxVerts);
            var local = ArrayShelf<int>.Rent(nv); for (int i = 0; i < nv; i++) local[i] = -1;
            var pv = new List<int>(cap); var pi = new List<int>(Math.Min(ni, cap * 3));
            void Close()
            {
                var v = ArrayShelf<PackedMesh.Vertex>.Rent(pv.Count); var c = cut != null ? ArrayShelf<PackedMesh.Cutaway>.Rent(pv.Count) : null;
                for (int k = 0; k < pv.Count; k++) { v[k] = verts[pv[k]]; if (c != null) c[k] = cut![pv[k]]; local[pv[k]] = -1; }
                var ix = ArrayShelf<ushort>.Rent(pi.Count); for (int k = 0; k < pi.Count; k++) ix[k] = (ushort)pi[k];
                out_.Add(new PackedMesh(v, pv.Count, c, ix, null, pi.Count, st) { Mates = neighbours });
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
            // the whole mesh's arrays were for splitting it
            ArrayShelf<PackedMesh.Vertex>.Return(verts); ArrayShelf<PackedMesh.Cutaway>.Return(cut); ArrayShelf<int>.Return(local);
            return out_;
        }
    }
}
