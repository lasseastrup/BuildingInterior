#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Generate;

namespace Triband.Storey.Occlusion
{
    /// <summary>
    /// Sink's footprint (SPEC §5.1): what stands in for a building in the way once it has collapsed. The outline of
    /// the storey at the player's height as a flat near-black fill 2 cm above its floor, ringed by a low rim of its
    /// outer wall in the wall's colour, with no doors or interior. Never along a party wall: that stands while
    /// the neighbour does, or both sides sank and their outlines merge. A port of the prototype's <c>buildLid</c>.
    /// Built once per building version and storey, in a builder of the shared ones (<see cref="MeshBuilder.Make"/>): give
    /// it back once uploaded (<see cref="MeshBuilder.Recycle"/>). A fresh builder made 83 KB of garbage for 60 vertices.
    /// Dissolve's Outline and Blueprint looks add the building's outline to it (<see cref="Cage"/>).
    /// </summary>
    public static class SinkFootprint
    {
        public const double RimHeight = 0.3;
        /// <summary>How dark the fill is: the wall colour scaled down to near-black.</summary>
        public const double FillTone = 0.05;
        /// <summary>How far the outline's lines stand off the outer walls' faces (metres), so they never lie in them.</summary>
        public const double CageClear = 0.02;

        /// <param name="cage">The outline's lines' thickness (metres), 0 for none (<see cref="Cage"/>).</param>
        /// <param name="storeys">The outline has a line round every storey.</param>
        public static MeshBuilder Build(Site site, BuildingData b, int k, double cage = 0, bool storeys = false)
        {
            var shared = new Lod0.Shared(site, b); var g = shared.At(k);
            double y = Derived.FloorBase(b, k), T = Dim.T_EXT; int k0 = Derived.TierStart(b, k);
            var op = MeshBuilder.Make(0, lean: true);
            var tris = Triangulate.Shape(g.Wp, null);
            var pts = new List<P3>(g.Wp.Count); foreach (var q in g.Wp) pts.Add(new P3(q.x, y + 0.02, q.z));
            op.PolyTris(pts, tris, new P3(0, 1, 0), g.C.wall.Shade(FillTone));
            var party = new List<(double, double)>(); var span = new List<(double, double)>(1);
            for (int i = 0; i < g.Wp.Count; i++)
            {
                var e = Geo.EdgeInfo(g.Wp, i, g.ccw); var m = Geo.MiterOf(g.cor, i, e.L);
                party.Clear(); foreach (var r in Party.Ranges(site, b, k0, i)) party.Add((r.s, r.e));
                span.Clear(); span.Add(m.Span(0, T));
                Facade.StripPieces(op, e.F, m, Geo.MinusRanges(span, party), y, y + RimHeight, 0, T, g.C.wall, g.C.wall, Skip.Bot);
            }
            if (cage > 0) Cage(op, shared, b, k, cage, storeys);
            return op;
        }

        /// <summary>
        /// Dissolve's outline (<see cref="DissolveLook.Outline"/>): the building from storey k up drawn as lines of thickness
        /// <paramref name="w"/>, standing <see cref="CageClear"/> off its outer walls' faces. Each tier: a post up every corner of its
        /// outline, a line round its top (the roof's eaves, or the floor of the setback above), round its foot where it starts
        /// above storey k (a setback's, on its terrace; storey k's is the footprint's rim), and with
        /// <paramref name="storeys"/> round every storey's floor. In the ceiling colour (an off-white by default), lit. The posts
        /// are a fifth thicker than the lines, which end in them, so the two never share a face; where two lines meet at a
        /// sharp corner they overlap past the post, in one colour and facing alike, which draws as one.
        /// </summary>
        public static void Cage(MeshBuilder op, Lod0.Shared shared, BuildingData b, int k, double w, bool storeys)
        {
            // the outline is the outer walls' inner line, their faces a wall's thickness out (the frames' w points out)
            double h = w / 2, hp = h * PostOver, off = Dim.T_EXT + hp + CageClear;
            var ring = new List<Vec2>(); var lines = new List<double>();
            foreach (var (k0, k1) in Party.Tiers(b))
            {
                if (k1 <= k) continue;
                int from = Math.Max(k0, k);
                double y0 = Derived.FloorBase(b, from), y1 = Derived.FloorBase(b, k1);
                var g = shared.At(k0); var fp = g.Wp; int n = fp.Count; if (n < 3) continue;
                var c = g.C.ceil;
                // the outline moved out by off, its corners mitred (a sharp one no further than eight times off)
                ring.Clear();
                for (int i = 0; i < n; i++)
                {
                    var e0 = Geo.EdgeInfo(fp, (i + n - 1) % n, g.ccw); var e1 = Geo.EdgeInfo(fp, i, g.ccw);
                    double dot = e0.w.x * e1.w.x + e0.w.z * e1.w.z, f = off / Math.Max(1 + dot, 0.125);
                    ring.Add(new Vec2(fp[i].x + (e0.w.x + e1.w.x) * f, fp[i].z + (e0.w.z + e1.w.z) * f));
                }
                lines.Clear(); lines.Add(y1);
                if (k0 > k) lines.Add(y0);
                if (storeys) for (int j = from + 1; j < k1; j++) lines.Add(Derived.FloorBase(b, j));
                for (int i = 0; i < n; i++)
                {
                    var a = ring[i]; var z = ring[(i + 1) % n];
                    double dx = z.x - a.x, dz = z.z - a.z, L = Geo.Hypot(dx, dz); if (L < 1e-6) continue;
                    var F = new Frame(a, new Vec2(dx / L, dz / L), new Vec2(-dz / L, dx / L));
                    foreach (double y in lines) op.OBox(F, 0, L, y - h, y + h, -h, h, c);
                    op.OBox(new Frame(a, new Vec2(1, 0), new Vec2(0, 1)), -hp, hp, y0, y1 + hp, -hp, hp, c);   // the post up its corner
                }
            }
        }

        /// <summary>How much thicker the outline's posts are than its lines.</summary>
        public const double PostOver = 1.2;
    }
}
