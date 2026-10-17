#nullable enable
namespace Triband.Storey.Generate
{
    /// <summary>
    /// The packer's per-vertex loops over raw memory (docs/PERFORMANCE.md §8.8): a builder's chunks in, the upload's layout
    /// out, with nothing managed in between. These are the reference versions, engine-free; in Unity the same code is
    /// compiled by Burst (<c>StoreyBurst</c> in the Unity assembly sets <see cref="Vertices"/> and the rest at start) and
    /// called from the build threads. Pointers, so no bounds or null checks either way: every index is checked before.
    /// </summary>
    public static unsafe class PackKernels
    {
        /// <summary>A colour entry of a builder's table as packed: its style's number, slot and tone bytes, and whether it is a pane's (0 or 1).</summary>
        public struct Colour { public int style; public byte b, a, glass; }
        /// <summary>A normal entry as packed: SNorm8.</summary>
        public struct Normal { public sbyte x, y, z; }

        public delegate void VerticesFn(P3* pos, int* ne, int* ce, Colour* colours, Normal* normals, float tag, PackedMesh.Vertex* dst, int n, float* bounds);
        public delegate void CutawaysFn(int* kind, int* wall, PackedMesh.Cutaway* dst, int n, int mateStart, int mateCount);
        public delegate int IndicesFn(int* src, ushort* dst, int n, int verts);
        public delegate void FinishFn(PackedMesh.Vertex* v, PackedMesh.Cutaway* cut, int n, byte* lo, byte* hi, int wallBase);

        /// <summary>
        /// The kernels are Burst's (the Unity side sets this with them): a mesh packed whole then goes through them. Run as
        /// managed code they are a tenth slower than the packer's own single pass, which is used otherwise.
        /// </summary>
        public static bool Compiled;

        /// <summary>The kernels packing calls: these, or the same compiled by Burst.</summary>
        public static VerticesFn Vertices = VerticesManaged;
        public static CutawaysFn Cutaways = CutawaysManaged;
        public static IndicesFn Indices16 = Indices16Managed;
        public static FinishFn Finish = FinishManaged;

        /// <summary>
        /// <paramref name="n"/> vertices in the upload's layout: each position as floats, its normal's and colour's packed
        /// entries, the tag; the bounds (x0, y0, z0, x1, y1, z1) widened to take them. A pane's place is written after.
        /// </summary>
        public static void VerticesManaged(P3* pos, int* ne, int* ce, Colour* colours, Normal* normals, float tag, PackedMesh.Vertex* dst, int n, float* bounds)
        {
            float x0 = bounds[0], y0 = bounds[1], z0 = bounds[2], x1 = bounds[3], y1 = bounds[4], z1 = bounds[5];
            for (int i = 0; i < n; i++)
            {
                Colour* c = colours + ce[i]; Normal* nm = normals + ne[i]; PackedMesh.Vertex* v = dst + i;
                float x = (float)pos[i].x, y = (float)pos[i].y, z = (float)pos[i].z;
                v->x = x; v->y = y; v->z = z;
                v->nx = nm->x; v->ny = nm->y; v->nz = nm->z; v->nw = 0;
                int si = c->style;
                v->r = (byte)(si & 255); v->g = (byte)(si >> 8); v->b = c->b; v->a = c->a; v->tag = tag;
                x0 = Min(x0, x); y0 = Min(y0, y); z0 = Min(z0, z); x1 = Max(x1, x); y1 = Max(y1, y); z1 = Max(z1, z);
            }
            bounds[0] = x0; bounds[1] = y0; bounds[2] = z0; bounds[3] = x1; bounds[4] = y1; bounds[5] = z1;
        }

        /// <summary>
        /// The cutaway stream: each vertex's kind, a party wall's neighbour moved into the site's block of the building table
        /// (<see cref="Lod.MateIndex.Shift"/>; <paramref name="mateCount"/> below 0: not moved), and its wall id.
        /// </summary>
        public static void CutawaysManaged(int* kind, int* wall, PackedMesh.Cutaway* dst, int n, int mateStart, int mateCount)
        {
            for (int i = 0; i < n; i++)
            {
                int k = kind[i];
                if (mateCount >= 0 && k >= 8) { int mate = (k >> 3) - 1; k = mate < mateCount ? (k & 7) + ((mateStart + mate + 1) << 3) : k & 7; }
                dst[i].kind = k; dst[i].wid = wall[i];
            }
        }

        /// <summary><paramref name="n"/> indices as 16 bits, each checked against <paramref name="verts"/>: the place of the first out of range, or −1.</summary>
        public static int Indices16Managed(int* src, ushort* dst, int n, int verts)
        {
            for (int i = 0; i < n; i++)
            {
                int x = src[i];
                if ((uint)x >= (uint)verts) return i;
                dst[i] = (ushort)x;
            }
            return -1;
        }

        /// <summary>
        /// <see cref="PackedMesh.Finish"/>'s loop: each vertex's style number (in r and g) becomes its style's colour row
        /// (<paramref name="lo"/>, <paramref name="hi"/>), and each wall id the table's (from <paramref name="wallBase"/>;
        /// none below 0). <paramref name="cut"/> null: no cutaway stream.
        /// </summary>
        public static void FinishManaged(PackedMesh.Vertex* v, PackedMesh.Cutaway* cut, int n, byte* lo, byte* hi, int wallBase)
        {
            for (int i = 0; i < n; i++) { int s = v[i].r | v[i].g << 8; v[i].r = lo[s]; v[i].g = hi[s]; }
            if (cut == null) return;
            for (int i = 0; i < n; i++) { float wi = cut[i].wid; cut[i].wid = wi > 0 && wallBase >= 0 ? wallBase + wi - 1 : 0; }
        }

        // Math.Min and Math.Max for floats as .NET has them (−0 below +0, a NaN carried), written out so Burst's build
        // gives the same bounds
        static bool Negative(float f) => *(int*)&f < 0;
        static float Min(float a, float b)
        {
            if (a != b) return float.IsNaN(a) ? a : (a < b ? a : b);
            return Negative(a) ? a : b;
        }
        static float Max(float a, float b)
        {
            if (a != b) return float.IsNaN(a) ? a : (b < a ? a : b);
            return Negative(b) ? a : b;
        }
    }
}
