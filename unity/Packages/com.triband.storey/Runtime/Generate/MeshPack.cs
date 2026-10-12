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

        public readonly Vertex[] V;
        /// <summary>LOD0's cutaway stream; null for a lean mesh (LOD1).</summary>
        public readonly Cutaway[]? Cut;
        /// <summary>The indices: 16-bit (<see cref="I16"/>) when there are 65,535 vertices or fewer, else 32-bit.</summary>
        public readonly ushort[]? I16;
        public readonly int[]? I32;
        /// <summary>The styles whose colour rows the vertices name, by index.</summary>
        public readonly StyleRef[] Styles;
        /// <summary>The bounds of the positions.</summary>
        public readonly float X0, Y0, Z0, X1, Y1, Z1;
        bool finished;

        public int Verts => V.Length;
        public int Indices => I16 != null ? I16.Length : I32!.Length;

        internal PackedMesh(Vertex[] v, Cutaway[]? cut, ushort[]? i16, int[]? i32, StyleRef[] styles)
        {
            V = v; Cut = cut; I16 = i16; I32 = i32; Styles = styles;
            float x0 = float.MaxValue, y0 = x0, z0 = x0, x1 = float.MinValue, y1 = x1, z1 = x1;
            foreach (var q in v) { x0 = Math.Min(x0, q.x); y0 = Math.Min(y0, q.y); z0 = Math.Min(z0, q.z); x1 = Math.Max(x1, q.x); y1 = Math.Max(y1, q.y); z1 = Math.Max(z1, q.z); }
            if (v.Length == 0) x0 = y0 = z0 = x1 = y1 = z1 = 0;
            X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1;
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
            var v = V;
            for (int i = 0; i < v.Length; i++) { int s = v[i].r | v[i].g << 8; v[i].r = lo[s]; v[i].g = hi[s]; }
            if (Cut == null) return;
            var c = Cut;
            for (int i = 0; i < c.Length; i++) { float wi = c[i].wid; c[i].wid = wi > 0 && wallBase >= 0 ? wallBase + wi - 1 : 0; }
        }
    }

    /// <summary>Packs a welded <see cref="MeshBuilder"/> into <see cref="PackedMesh"/>es: engine-free, so on a worker thread.</summary>
    public static class MeshPack
    {
        /// <summary>The most vertices 16-bit indices reach.</summary>
        public const int MaxVerts = 65535;

        static sbyte S8(double v) => (sbyte)Math.Round(Math.Max(-1, Math.Min(1, v)) * 127);

        /// <summary>
        /// The builder (welded first if it isn't) in the upload's layout, in parts of at most <paramref name="maxVerts"/>
        /// vertices, each with 16-bit indices: the triangles in order, a part closed when the next would take it past,
        /// the vertices a triangle shares with the part before copied into its own. A mesh that fits is one part, its
        /// vertices as welded. None for a mesh without triangles. <paramref name="windows"/>: panes carry their place on
        /// the pane (the opaque meshes and the glass, not the massing). <paramref name="mates"/>: where the site's
        /// buildings sit in the table, for party walls (<see cref="MateIndex.Shift"/>).
        /// </summary>
        public static List<PackedMesh> Pack(MeshBuilder gb, bool windows = false, (int start, int count)? mates = null, int maxVerts = MaxVerts)
        {
            if (!gb.Welded) gb.Weld();
            int nv = gb.Verts, ni = gb.I.Count;
            var out_ = new List<PackedMesh>(1);
            if (ni == 0) return out_;
            // a bad index crashes natively on the GPU: a managed exception here, where it says which
            if (ni % 3 != 0) throw new InvalidOperationException($"{ni} indices is not whole triangles");
            var I = gb.I;
            for (int i = 0; i < ni; i++) if ((uint)I[i] >= (uint)nv) throw new InvalidOperationException($"index {I[i]} of {nv} vertices");

            // every vertex once, with its style's index where its colour row goes
            var verts = new PackedMesh.Vertex[nv];
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
                v.x = (float)p.x; v.y = (float)p.y; v.z = (float)p.z;
                v.nx = S8(n.x); v.ny = S8(n.y); v.nz = S8(n.z);
                v.r = (byte)(si & 255); v.g = (byte)(si >> 8); v.b = cb; v.a = ca; v.tag = tag;
                if (windows && s.slot == ColorSlot.Glass && gb.PaneUV.TryGetValue(i, out var uv)) { var (pa, pw, pb) = ColorRows.EncodePane(uv.u, uv.v, uv.id); v.a = pa; v.nw = pw; v.b = pb; }
            }
            if (styles.Count > 65536) throw new InvalidOperationException($"{styles.Count} styles in one mesh");
            PackedMesh.Cutaway[]? cut = null;
            if (!gb.Lean)
            {
                cut = new PackedMesh.Cutaway[nv];
                for (int i = 0; i < nv; i++) cut[i] = new PackedMesh.Cutaway { kind = mates is (int ms, int mc) ? MateIndex.Shift(gb.K[i], ms, mc) : gb.K[i], wid = gb.WI[i] };
            }
            var st = styles.ToArray();

            if (nv <= maxVerts)
            {
                if (nv <= MaxVerts) { var i16 = new ushort[ni]; for (int i = 0; i < ni; i++) i16[i] = (ushort)I[i]; out_.Add(new PackedMesh(verts, cut, i16, null, st)); }
                else out_.Add(new PackedMesh(verts, cut, null, I.ToArray(), st));
                return out_;
            }

            // in parts: each vertex's place in the part being filled (-1: not in it)
            int cap = Math.Min(maxVerts, MaxVerts);
            var local = new int[nv]; for (int i = 0; i < nv; i++) local[i] = -1;
            var pv = new List<int>(cap); var pi = new List<int>(Math.Min(ni, cap * 3));
            void Close()
            {
                var v = new PackedMesh.Vertex[pv.Count]; var c = cut != null ? new PackedMesh.Cutaway[pv.Count] : null;
                for (int k = 0; k < pv.Count; k++) { v[k] = verts[pv[k]]; if (c != null) c[k] = cut![pv[k]]; local[pv[k]] = -1; }
                var ix = new ushort[pi.Count]; for (int k = 0; k < pi.Count; k++) ix[k] = (ushort)pi[k];
                out_.Add(new PackedMesh(v, c, ix, null, st));
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
    }
}
