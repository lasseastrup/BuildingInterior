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
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly List<P3> P = new List<P3>();
        public readonly List<P3> N = new List<P3>();
        public readonly List<Rgb> C = new List<Rgb>();
        /// <summary>Cutaway data per vertex: wall start (x, z) and normal scaled by 1 + length.</summary>
        public readonly List<double> W = new List<double>();
        /// <summary>Kind per vertex: 0 slab/roof, 1 wall, 2 tall part; + 8 × (neighbour index + 1) for party walls.</summary>
        public readonly List<int> K = new List<int>();
        /// <summary>Wall id per vertex, 1-based within the building; 0 = none.</summary>
        public readonly List<int> WI = new List<int>();
        public readonly List<int> I = new List<int>();
        /// <summary>The walls given ids, in id order.</summary>
        public List<(double[] W, int K)> Walls = new List<(double[], int)>();
        public List<Solid>? Solids;
        public readonly int Tag;
        public readonly bool Lean;

        double[] curW = Zero4;
        int curK, curI;
        public static readonly double[] Zero4 = { 0, 0, 0, 0 };

        public MeshBuilder(int tag = 0, bool lean = false) { Tag = tag; Lean = lean; }

        public int Tris => I.Count / 3;
        public int Verts => P.Count;

        /// <summary>Set the cutaway context for what follows. A wall (kind 1) gets an id so it can slide.</summary>
        public MeshBuilder Ctx(double[]? w, int k)
        {
            curW = w ?? Zero4; curK = k;
            if (!Lean && curK % 8 == 1) { Walls.Add(((double[])curW.Clone(), curK)); curI = Walls.Count; }
            else curI = 0;
            return this;
        }

        void V(P3 q, P3 nr, Rgb c)
        {
            P.Add(q); N.Add(nr); C.Add(c);
            if (!Lean) { W.Add(curW[0]); W.Add(curW[1]); W.Add(curW[2]); W.Add(curW[3]); K.Add(curK); WI.Add(curI); }
        }

        /// <summary>A convex planar polygon, fanned; winding fixed to face along <paramref name="nr"/>.</summary>
        public void Poly(P3[] pts, P3 nr, Rgb c)
        {
            P3 a = pts[0], b = pts[1], d = pts[2];
            var cr = P3.Cross(b - a, d - a);
            if (P3.Dot(cr, nr) < 0) { pts = (P3[])pts.Clone(); Array.Reverse(pts); }
            int bse = P.Count;
            foreach (var q in pts) V(q, nr, c);
            for (int j = 1; j < pts.Length - 1; j++) { I.Add(bse); I.Add(bse + j); I.Add(bse + j + 1); }
        }

        /// <summary>A triangulated planar polygon (earcut output): one vertex per corner, shared by its triangles.</summary>
        public void PolyTris(List<P3> pts, List<int> tris, P3 nr, Rgb c)
        {
            if (tris.Count == 0) return;
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
        public void OBox(Frame F, double u0, double u1, double y0, double y1, double w0, double w1, Rgb c, Rgb? cIn = null, Skip skip = Skip.None)
        {
            if (u1 - u0 < 1e-4 || y1 - y0 < 1e-4 || w1 - w0 < 1e-4) return;
            Rgb ci = cIn ?? c;
            Solids?.Add(new Solid { F = F, u0 = u0, u1 = u1, y0 = y0, y1 = y1, w0 = w0, w1 = w1 });
            P3 Pt(double a, double y, double b) => F.At(a, y, b);
            var nu = new P3(F.u.x, 0, F.u.z); var nw = new P3(F.w.x, 0, F.w.z);
            if ((skip & Skip.Out) == 0) Poly(new[] { Pt(u0, y0, w1), Pt(u1, y0, w1), Pt(u1, y1, w1), Pt(u0, y1, w1) }, nw, c);
            if ((skip & Skip.In) == 0) Poly(new[] { Pt(u0, y0, w0), Pt(u1, y0, w0), Pt(u1, y1, w0), Pt(u0, y1, w0) }, new P3(-nw.x, 0, -nw.z), ci);
            if ((skip & Skip.UEnd) == 0) Poly(new[] { Pt(u1, y0, w0), Pt(u1, y0, w1), Pt(u1, y1, w1), Pt(u1, y1, w0) }, nu, c);
            if ((skip & Skip.UStart) == 0) Poly(new[] { Pt(u0, y0, w0), Pt(u0, y0, w1), Pt(u0, y1, w1), Pt(u0, y1, w0) }, new P3(-nu.x, 0, -nu.z), c);
            if ((skip & Skip.Top) == 0) Poly(new[] { Pt(u0, y1, w0), Pt(u1, y1, w0), Pt(u1, y1, w1), Pt(u0, y1, w1) }, new P3(0, 1, 0), c);
            if ((skip & Skip.Bot) == 0) Poly(new[] { Pt(u0, y0, w0), Pt(u1, y0, w0), Pt(u1, y0, w1), Pt(u0, y0, w1) }, new P3(0, -1, 0), c);
        }

        /// <summary>A box whose start and end faces are slanted: u_start(w) = sA + kA·w, u_end(w) = eB + kB·w.</summary>
        public void MBox(Frame F, double sA, double kA, double eB, double kB, double y0, double y1, double w0, double w1, Rgb c, Rgb? cIn = null, Skip skip = Skip.None)
        {
            double s0 = sA + kA * w0, s1 = sA + kA * w1, e0 = eB + kB * w0, e1 = eB + kB * w1;
            if (e0 - s0 < 1e-4 || e1 - s1 < 1e-4 || y1 - y0 < 1e-4 || w1 - w0 < 1e-4) return;
            Rgb ci = cIn ?? c;
            Solids?.Add(new Solid { F = F, Mitred = true, sA = sA, kA = kA, eB = eB, kB = kB, y0 = y0, y1 = y1, w0 = w0, w1 = w1 });
            P3 Pt(double a, double y, double b) => F.At(a, y, b);
            var nw = new P3(F.w.x, 0, F.w.z);
            if ((skip & Skip.Out) == 0) Poly(new[] { Pt(s1, y0, w1), Pt(e1, y0, w1), Pt(e1, y1, w1), Pt(s1, y1, w1) }, nw, c);
            if ((skip & Skip.In) == 0) Poly(new[] { Pt(s0, y0, w0), Pt(e0, y0, w0), Pt(e0, y1, w0), Pt(s0, y1, w0) }, new P3(-nw.x, 0, -nw.z), ci);
            if ((skip & Skip.UEnd) == 0) Poly(new[] { Pt(e0, y0, w0), Pt(e1, y0, w1), Pt(e1, y1, w1), Pt(e0, y1, w0) }, Geo.EndNormal(F, e0, e1, w0, w1, 1), c);
            if ((skip & Skip.UStart) == 0) Poly(new[] { Pt(s0, y0, w0), Pt(s1, y0, w1), Pt(s1, y1, w1), Pt(s0, y1, w0) }, Geo.EndNormal(F, s0, s1, w0, w1, -1), c);
            if ((skip & Skip.Top) == 0) Poly(new[] { Pt(s0, y1, w0), Pt(e0, y1, w0), Pt(e1, y1, w1), Pt(s1, y1, w1) }, new P3(0, 1, 0), c);
            if ((skip & Skip.Bot) == 0) Poly(new[] { Pt(s0, y0, w0), Pt(e0, y0, w0), Pt(e1, y0, w1), Pt(s1, y0, w1) }, new P3(0, -1, 0), c);
        }
    }
}
