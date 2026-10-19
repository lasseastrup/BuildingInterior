#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// A part of an artist's mesh (an opening's or a facade detail's) made ready once (docs/PERFORMANCE.md §8.1): the points
    /// its triangles use and their normals, its triangles into them, and wound the other way for glass seen from inside.
    /// A placement transforms it straight into the builder (<see cref="Put"/>), where each placement made two arrays of the
    /// mesh's points, and each part a map, two lists and their copies (walk-in glass two more): about 21 MB a LOD0 build
    /// with a 100-vertex window. Made when the kind is registered, or when first placed if its part changed since; a part
    /// is read by every build thread, and never changed once made. Storey's own.
    /// </summary>
    internal sealed class KindPart
    {
        readonly P3[] p, n; readonly int[] t; int[]? back;
        // what it was made from: a part given other arrays since is made again
        readonly P3[] fromP, fromN; readonly int[] fromT;

        KindPart(P3[] P, P3[] N, int[] tris)
        {
            fromP = P; fromN = N; fromT = tris;
            var map = new Dictionary<int, int>(); var pl = new List<P3>(); var nl = new List<P3>(); var tl = new List<int>(tris.Length);
            int Slot(int v)
            {
                if (map.TryGetValue(v, out int j)) return j;
                map[v] = j = pl.Count; pl.Add(P[v]); nl.Add(v < N.Length ? N[v] : new P3(0, 0, 1));
                return j;
            }
            // whole triangles, in order; one naming a point the mesh hasn't is left out
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if ((uint)a >= (uint)P.Length || (uint)b >= (uint)P.Length || (uint)c >= (uint)P.Length) continue;
                tl.Add(Slot(a)); tl.Add(Slot(b)); tl.Add(Slot(c));
            }
            p = pl.ToArray(); n = nl.ToArray(); t = tl.ToArray();
        }

        /// <summary>The part ready, from its kind's cache when it is there and made from these arrays, else made (and cached).</summary>
        public static KindPart Of(P3[] P, P3[] N, int[] tris, ref KindPart? cache)
        {
            var k = Volatile.Read(ref cache);
            if (k != null && ReferenceEquals(k.fromP, P) && ReferenceEquals(k.fromN, N) && ReferenceEquals(k.fromT, tris)) return k;
            k = new KindPart(P, N, tris);
            Volatile.Write(ref cache, k);
            return k;
        }

        /// <summary>Its triangles.</summary>
        public int Tris => t.Length / 3;

        // the triangles wound the other way: glass seen from inside (made once, on first use)
        int[] Back
        {
            get
            {
                var b = Volatile.Read(ref back);
                if (b != null) return b;
                b = new int[t.Length];
                for (int i = 0; i < b.Length; i += 3) { b[i] = t[i]; b[i + 1] = t[i + 2]; b[i + 2] = t[i + 1]; }
                Volatile.Write(ref back, b);
                return b;
            }
        }

        /// <summary>
        /// Into <paramref name="m"/> where <paramref name="at"/> puts it: front faces as made, or (<paramref name="inside"/>)
        /// wound the other way with the normals turned. Returns the first new vertex.
        /// </summary>
        public int Put(MeshBuilder m, in KindPlace at, Swatch c, bool inside = false)
        {
            int bse = m.Verts;
            if (m.ShapeOnly) return bse;
            for (int i = 0; i < p.Length; i++) m.V(at.Point(p[i]), inside ? at.Normal(p[i], n[i]) * -1 : at.Normal(p[i], n[i]), c);
            var tt = inside ? Back : t;
            for (int i = 0; i < tt.Length; i++) m.I.Add(bse + tt[i]);
            return bse;
        }
    }

    /// <summary>
    /// Where an artist's mesh goes on a wall: its x along the wall from <c>u</c> (along cross(up, outward), whichever way
    /// the wall's frame runs, so its front faces stay front), its y up from <c>y0</c>, its z out from the wall's outer face.
    /// Stretched by <c>sx</c> across and <c>sy</c> up; or sliced (<see cref="Sliced"/>, docs/OPENINGS.md §4.3), as 9-slice
    /// scaling does: what lies between the slice lines is stretched to where they go in the opening, and what lies outside
    /// them moves with the nearest, unscaled. A normal goes by the inverse scale where its point is stretched. A part that
    /// keeps its shape (<see cref="Rigid"/>) moves with its anchor, unscaled.
    /// </summary>
    internal readonly struct KindPlace
    {
        readonly Frame F; readonly double u, y0, sx, sy, flip;
        // sliced: across between xL and xR (to XL..XR, scale mx), up between yB and yT (to YB..YT, scale my); else stretched
        readonly bool cutX, cutY;
        readonly double xL, xR, XL, mx, yB, yT, YB, my;
        // a part that keeps its shape: its anchor in its own space
        readonly bool rigid; readonly double ax, ay;

        public KindPlace(Frame f, double u, double y0, double sx, double sy) : this(f, u, y0, sx, sy, false, 0, 0, 0, 1, false, 0, 0, 0, 1, false, 0, 0) { }

        KindPlace(Frame f, double u, double y0, double sx, double sy, bool cutX, double xL, double xR, double XL, double mx, bool cutY, double yB, double yT, double YB, double my, bool rigid, double ax, double ay)
        {
            F = f; this.u = u; this.y0 = y0; this.sx = sx; this.sy = sy;
            flip = f.u.x * f.w.z - f.u.z * f.w.x < 0 ? -1 : 1;
            this.cutX = cutX; this.xL = xL; this.xR = xR; this.XL = XL; this.mx = mx;
            this.cutY = cutY; this.yB = yB; this.yT = yT; this.YB = YB; this.my = my;
            this.rigid = rigid; this.ax = ax; this.ay = ay;
        }

        /// <summary>
        /// A hole <paramref name="w"/> × <paramref name="h"/> sliced into an opening <paramref name="W"/> × <paramref name="H"/>
        /// at <paramref name="u"/> along the wall (its middle) and <paramref name="y0"/> up (its bottom): the slice lines
        /// <paramref name="bl"/>, <paramref name="br"/>, <paramref name="bb"/> and <paramref name="bt"/> in from the hole's
        /// sides, bottom and top. Where an opening (or the hole) has no room between them, that way is stretched instead.
        /// </summary>
        public static KindPlace Sliced(Frame f, double u, double y0, double w, double h, double W, double H, double bl, double br, double bb, double bt)
        {
            double xL = -w / 2 + bl, xR = w / 2 - br, XL = -W / 2 + bl, XR = W / 2 - br, yB = bb, yT = h - bt, YB = bb, YT = H - bt;
            bool cx = xR - xL > 1e-3 && XR - XL > 1e-3, cy = yT - yB > 1e-3 && YT - YB > 1e-3;
            return new KindPlace(f, u, y0, W / Math.Max(1e-3, w), H / Math.Max(1e-3, h),
                cx, xL, xR, XL, cx ? (XR - XL) / (xR - xL) : 1, cy, yB, yT, YB, cy ? (YT - YB) / (yT - yB) : 1, false, 0, 0);
        }

        /// <summary>The same placement for a part that keeps its shape: it goes where its anchor goes, unscaled.</summary>
        public KindPlace Rigid(P3 anchor) => new KindPlace(F, u, y0, sx, sy, cutX, xL, xR, XL, mx, cutY, yB, yT, YB, my, true, anchor.x, anchor.y);

        // a point's place across and up, in the opening's own space. One the bake set aside from a line (KindBake.Aside) goes
        // back onto it, so the pieces either side meet; its side still decides its normal.
        const double OnLine = KindBake.Aside * 2;
        double X(double x) => !cutX ? x * sx
            : Math.Abs(x - xL) <= OnLine ? XL : Math.Abs(x - xR) <= OnLine ? XL + (xR - xL) * mx
            : x < xL ? x + (XL - xL) : x > xR ? x + (XL + (xR - xL) * mx - xR) : XL + (x - xL) * mx;
        double Y(double y) => !cutY ? y * sy
            : Math.Abs(y - yB) <= OnLine ? YB : Math.Abs(y - yT) <= OnLine ? YB + (yT - yB) * my
            : y < yB ? y : y > yT ? y + (YB + (yT - yB) * my - yT) : YB + (y - yB) * my;

        public P3 Point(P3 p) => rigid
            ? F.At(u + flip * (X(ax) + p.x - ax), y0 + Y(ay) + p.y - ay, Dim.T_EXT + p.z)
            : F.At(u + flip * X(p.x), y0 + Y(p.y), Dim.T_EXT + p.z);

        /// <summary>The normal <paramref name="n"/> at point <paramref name="p"/> (its own space): by the inverse of the scale where the point is.</summary>
        public P3 Normal(P3 p, P3 n)
        {
            double kx = rigid ? 1 : !cutX ? sx : p.x < xL || p.x > xR ? 1 : mx;
            double ky = rigid ? 1 : !cutY ? sy : p.y < yB || p.y > yT ? 1 : my;
            double nx = n.x / kx, ny = n.y / ky, nz = n.z;
            return new P3(F.u.x * flip * nx + F.w.x * nz, ny, F.u.z * flip * nx + F.w.z * nz).Normalized;
        }
    }
}
