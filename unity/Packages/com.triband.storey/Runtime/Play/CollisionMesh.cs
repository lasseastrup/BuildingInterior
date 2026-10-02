#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Play
{
    /// <summary>What a collision triangle is part of.</summary>
    public enum CollisionKind : byte { Floor, Stair, Wall, Roof }

    /// <summary>
    /// A building's collision mesh for Unity physics (a <c>MeshCollider</c>): the walk model's world as triangles, so
    /// characters, rigid bodies, raycasts and NavMesh baking meet what <see cref="PlayWorld"/> walks on and is stopped by.
    /// <list type="bullet">
    /// <item>Walls: LOD0's collision segments, each a thin box as thick as its radius says, as tall as its storey. A wall
    /// that carries on up through the storeys above is one box.</item>
    /// <item>Floors: one polygon per level (the floor, terrace or roof), as far out as the walk model lets the player step,
    /// with the stair holes cut out; none inside storeys nobody can enter (shell-only buildings, filled storeys).</item>
    /// <item>Stairs: a ramp up each flight, a flat mid landing.</item>
    /// <item>Pitched roofs: their planes.</item>
    /// </list>
    /// Engine-free, from the building's LOD0 result (already built for rendering), in the layout's coordinates. One-sided:
    /// each triangle faces out of what it bounds (floors and ramps up). Vertices are shared within a piece; there are no
    /// normals or colours.
    /// </summary>
    public sealed class CollisionMesh
    {
        public readonly List<P3> P = new List<P3>();
        public readonly List<int> I = new List<int>();
        /// <summary>Per triangle.</summary>
        public readonly List<CollisionKind> Kind = new List<CollisionKind>();
        public int Tris => I.Count / 3;

        /// <summary>A roof storey's walls (parapets, roof edges): high enough to stop the player, not a bulkhead's full height.</summary>
        public const double RoofWallHeight = 1.2;

        public static CollisionMesh Build(Site site, BuildingData b, Lod0Result l0)
        {
            var m = new CollisionMesh();
            m.Walls(b, l0);
            m.Floors(b);
            m.Stairs(b);
            m.PitchedRoof(site, b);
            return m;
        }

        // ---- building blocks ----

        int V(double x, double y, double z) { P.Add(new P3(x, y, z)); return P.Count - 1; }

        /// <summary>A triangle facing along <paramref name="n"/> (the winding is fixed, as <see cref="MeshBuilder.Poly"/> fixes it).</summary>
        void Tri(int a, int b, int c, P3 n, CollisionKind k)
        {
            var cr = P3.Cross(P[b] - P[a], P[c] - P[a]);
            if (cr.x * n.x + cr.y * n.y + cr.z * n.z < 0) { var t = b; b = c; c = t; }
            I.Add(a); I.Add(b); I.Add(c); Kind.Add(k);
        }

        void Quad(int a, int b, int c, int d, P3 n, CollisionKind k) { Tri(a, b, c, n, k); Tri(a, c, d, n, k); }

        static readonly P3 Up = new P3(0, 1, 0);

        // ---- walls ----

        void Walls(BuildingData b, Lod0Result l0)
        {
            int N = b.floors.Count;
            // runs of the same segment up consecutive storeys: (key) -> (seg, y0, y1)
            var open = new Dictionary<(double, double, double, double, double), (Seg s, double y0, double y1)>();
            for (int k = 0; k < l0.Segs.Count; k++)
            {
                double y0 = Derived.FloorBase(b, k), y1 = k < N ? Derived.FloorBase(b, k + 1) : y0 + RoofWallHeight;
                var here = new HashSet<(double, double, double, double, double)>();
                foreach (var s in l0.Segs[k])
                {
                    var key = (s.ax, s.az, s.bx, s.bz, s.r);
                    if (!here.Add(key)) continue;   // the same segment twice on a storey
                    if (open.TryGetValue(key, out var run) && Math.Abs(run.y1 - y0) < 1e-9) open[key] = (s, run.y0, y1);
                    else
                    {
                        if (open.TryGetValue(key, out run)) Box(run.s, run.y0, run.y1);
                        open[key] = (s, y0, y1);
                    }
                }
                // runs that stop below this storey are done
                foreach (var key in new List<(double, double, double, double, double)>(open.Keys))
                    if (!here.Contains(key)) { var r = open[key]; Box(r.s, r.y0, r.y1); open.Remove(key); }
            }
            foreach (var r in open.Values) Box(r.s, r.y0, r.y1);
        }

        /// <summary>A segment as a box: its radius either side, its ends grown by the radius, from y0 to y1; no bottom face.</summary>
        void Box(Seg s, double y0, double y1)
        {
            double dx = s.bx - s.ax, dz = s.bz - s.az, L = Math.Sqrt(dx * dx + dz * dz);
            double ux, uz; if (L < 1e-9) { ux = 1; uz = 0; } else { ux = dx / L; uz = dz / L; }
            double nx = -uz, nz = ux, r = s.r;
            double ax = s.ax - ux * r, az = s.az - uz * r, bx = s.bx + ux * r, bz = s.bz + uz * r;
            int[] c = new int[8];
            (double x, double z)[] xz = { (ax - nx * r, az - nz * r), (bx - nx * r, bz - nz * r), (bx + nx * r, bz + nz * r), (ax + nx * r, az + nz * r) };
            for (int i = 0; i < 4; i++) { c[i] = V(xz[i].x, y0, xz[i].z); c[i + 4] = V(xz[i].x, y1, xz[i].z); }
            var u = new P3(ux, 0, uz); var n = new P3(nx, 0, nz);
            Quad(c[0], c[1], c[5], c[4], new P3(-n.x, 0, -n.z), CollisionKind.Wall);   // the −n side
            Quad(c[2], c[3], c[7], c[6], n, CollisionKind.Wall);                        // the +n side
            Quad(c[1], c[2], c[6], c[5], u, CollisionKind.Wall);                        // the b end
            Quad(c[3], c[0], c[4], c[7], new P3(-u.x, 0, -u.z), CollisionKind.Wall);   // the a end
            Quad(c[4], c[5], c[6], c[7], Up, CollisionKind.Wall);                       // the top
        }

        // ---- floors ----

        /// <summary>Where a stair's slab is open, as a world rectangle.</summary>
        static List<Vec2> HoleOf(BuildingData b, CoreData s)
        {
            var f = Cores.FrameOf(b, s);
            if (s.type == CoreType.Flight)
            {
                double hw = Dim.FLIGHT_W / 2, xm = -hw + Dim.FLIGHT_LANE, z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;
                return new List<Vec2> { f.At2(-hw, z0), f.At2(xm, z0), f.At2(xm, z1), f.At2(-hw, z1) };
            }
            return new List<Vec2> { f.At2(-1.3, -1.6), f.At2(1.3, -1.6), f.At2(1.3, 2.6), f.At2(-1.3, 2.6) };
        }

        static List<Vec2> World(BuildingData b, List<Vec2> fp) { var o = new List<Vec2>(fp.Count); foreach (var p in fp) o.Add(new Vec2(p.x + b.pos.x, p.z + b.pos.z)); return o; }

        void Floors(BuildingData b)
        {
            int N = b.floors.Count;
            for (int k = 0; k <= N; k++)
            {
                // a level nobody can stand on: between two storeys with no inside (not a roof or a terrace)
                if (k < N && Derived.ShellAt(b, k) && Derived.ShellAt(b, k - 1) && !Derived.IsSetback(b, k)) continue;
                var outlines = new List<List<Vec2>>();
                if (k > 0) outlines.Add(World(b, Derived.OutlineAt(b, k - 1)));   // the storey below's roof or terrace
                if (k < N) outlines.Add(World(b, Derived.OutlineAt(b, k)));       // this storey's floor (an overhang reaches past the one below)
                var holes = new List<List<Vec2>>();
                foreach (var s in b.shafts) if (Cores.StairHoleAt(b, s, k)) holes.Add(HoleOf(b, s));
                double y = Derived.FloorBase(b, k);
                // as far out as the walk model lets the player stand: slabs run out under the walls
                foreach (var poly in Geo.GrownUnion(outlines, Dim.T_EXT + 0.02, holes)) Flat(poly, y, CollisionKind.Floor);
            }
        }

        void Flat(List<List<Vec2>> poly, double y, CollisionKind kind)
        {
            var holes = poly.Count > 1 ? poly.GetRange(1, poly.Count - 1) : null;
            var tris = Triangulate.Shape(poly[0], holes);
            int base0 = P.Count;
            foreach (var ring in poly) foreach (var p in ring) V(p.x, y, p.z);
            // Triangulate drops a ring's repeated closing point: index the points as it does
            var ids = new List<int>();
            int at = base0;
            foreach (var ring in poly)
            {
                int n = ring.Count; bool closed = n > 1 && ring[0].x == ring[n - 1].x && ring[0].z == ring[n - 1].z;
                for (int i = 0; i < (closed ? n - 1 : n); i++) ids.Add(at + i);
                at += n;
            }
            for (int t = 0; t + 2 < tris.Count; t += 3) Tri(ids[tris[t]], ids[tris[t + 1]], ids[tris[t + 2]], Up, kind);
        }

        // ---- stairs ----

        void Stairs(BuildingData b)
        {
            int N = b.floors.Count;
            foreach (var s in b.shafts)
            {
                if (!Cores.IsStairs(s)) continue;
                var f = Cores.FrameOf(b, s);
                int Pt(double u, double w, double y) { var q = f.At2(u, w); return V(q.x, y, q.z); }
                for (int k = s.bottom; k <= N; k++)
                {
                    if (!Cores.HasFlight(b, s, k)) continue;
                    double yb = Derived.FloorBase(b, k), fh = Derived.FloorH(b, k);
                    if (s.type == CoreType.Flight)
                    {
                        double hw = Dim.FLIGHT_W / 2, xm = -hw + Dim.FLIGHT_LANE, z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;
                        Quad(Pt(-hw, z0, yb), Pt(xm, z0, yb), Pt(xm, z1, yb + fh), Pt(-hw, z1, yb + fh), Up, CollisionKind.Stair);
                        continue;
                    }
                    double half = fh / 2;
                    // up the −u lane along +w to the mid landing, across it, and up the +u lane back along −w
                    Quad(Pt(-1.3, -1.6, yb), Pt(0, -1.6, yb), Pt(0, 1.6, yb + half), Pt(-1.3, 1.6, yb + half), Up, CollisionKind.Stair);
                    Quad(Pt(-1.3, 1.6, yb + half), Pt(1.3, 1.6, yb + half), Pt(1.3, 2.6, yb + half), Pt(-1.3, 2.6, yb + half), Up, CollisionKind.Stair);
                    Quad(Pt(0, 1.6, yb + half), Pt(1.3, 1.6, yb + half), Pt(1.3, -1.6, yb + fh), Pt(0, -1.6, yb + fh), Up, CollisionKind.Stair);
                }
            }
        }

        // ---- pitched roofs ----

        void PitchedRoof(Site site, BuildingData b)
        {
            if (!Roofs.IsPitched(b)) return;
            var parts = Roofs.Parts(site, b); if (parts == null) return;
            foreach (var part in parts.Parts)
            {
                if (part.Kind != RoofKind.Roof || part.Pts.Count < 3) continue;
                var n = part.N.y < 0 ? new P3(-part.N.x, -part.N.y, -part.N.z) : part.N;   // out of the roof: up
                var tris = Triangulate.Planar(part.Pts, part.Holes.Count > 0 ? part.Holes : null, n);
                // the triangulation drops each ring's repeated closing point: index the points as it does
                var ids = new List<int>();
                foreach (var r in new[] { part.Pts }.Concat(part.Holes))
                {
                    int cnt = r.Count; bool closed = cnt > 2 && r[0].x == r[cnt - 1].x && r[0].y == r[cnt - 1].y && r[0].z == r[cnt - 1].z;
                    for (int i = 0; i < (closed ? cnt - 1 : cnt); i++) ids.Add(V(r[i].x, r[i].y, r[i].z));
                }
                for (int t = 0; t + 2 < tris.Count; t += 3) Tri(ids[tris[t]], ids[tris[t + 1]], ids[tris[t + 2]], n, CollisionKind.Roof);
            }
        }
    }
}
