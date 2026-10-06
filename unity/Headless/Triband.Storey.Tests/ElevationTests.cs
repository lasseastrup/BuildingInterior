using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Buildings at different heights (BuildingData.elevation). Raising every building of a layout by the same amount must
    /// move everything up by exactly that and change nothing else: the guard that no part of the generator, the walk
    /// model or the occlusion still takes the ground to be 0.
    /// </summary>
    public class ElevationTests
    {
        const double E = 7.3;

        public static IEnumerable<object[]> Corpora() { yield return new object[] { "demo" }; yield return new object[] { "variants" }; yield return new object[] { "city" }; }

        // "city": the demo street and 24 generated walk-in buildings (stairs, lifts, terraces), a foundation on every third
        static StoreyDocument Read(string corpus)
        {
            if (corpus != "city") return PrototypeJson.Read(Fixtures.Text(corpus + ".json")).Document;
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            Edit.TestCity.Generate(d, 24, walkIn: true);
            for (int i = 0; i < d.buildings.Count; i += 3) { d.buildings[i].style = d.buildings[i].style.Clone(); d.buildings[i].style.foundation = 0.8; }
            return d;
        }

        static (Site flat, Site raised) Pair(string corpus, double e = E)
        {
            var d0 = Read(corpus); var d1 = Read(corpus);
            foreach (var b in d1.buildings) b.elevation = e;
            return (new Site(d0.buildings), new Site(d1.buildings));
        }

        static void Shifted(IReadOnlyList<P3> a, IReadOnlyList<P3> b, string what)
        {
            Assert.True(a.Count == b.Count, $"{what}: {a.Count} vertices flat, {b.Count} raised");
            for (int i = 0; i < a.Count; i++)
                Assert.True(Math.Abs(a[i].x - b[i].x) < 1e-9 && Math.Abs(a[i].z - b[i].z) < 1e-9 && Math.Abs(a[i].y + E - b[i].y) < 1e-6,
                    $"{what}: vertex {i} is ({a[i].x:F3}, {a[i].y:F3}, {a[i].z:F3}) flat but ({b[i].x:F3}, {b[i].y:F3}, {b[i].z:F3}) raised");
        }

        static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) => a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y) < 1e-9 || x == y).All(t => t);

        static void Same(MeshBuilder a, MeshBuilder b, string what)
        {
            Shifted(a.P, b.P, what);
            Assert.True(a.I.SequenceEqual(b.I), $"{what}: the triangles differ");
            Assert.True(a.N.Zip(b.N, (p, q) => Math.Abs(p.x - q.x) + Math.Abs(p.y - q.y) + Math.Abs(p.z - q.z) < 1e-9).All(x => x), $"{what}: the normals differ");
            Assert.True(a.C.SequenceEqual(b.C), $"{what}: the colours differ");
            Assert.True(a.K.SequenceEqual(b.K) && a.WI.SequenceEqual(b.WI), $"{what}: the cutaway kinds differ");
            Assert.Equal(a.Walls.Count, b.Walls.Count);
            for (int i = 0; i < a.Walls.Count; i++) Assert.True(a.Walls[i].W.SequenceEqual(b.Walls[i].W) && a.Walls[i].K == b.Walls[i].K, $"{what}: wall {i} differs");
            // a window's place on its pane is the same; its number comes from its centre in the site, so it moves with it
            Assert.Equal(a.PaneUV.Count, b.PaneUV.Count);
            foreach (var kv in a.PaneUV) Assert.True(b.PaneUV.TryGetValue(kv.Key, out var q) && Math.Abs(q.u - kv.Value.u) < 1e-9 && Math.Abs(q.v - kv.Value.v) < 1e-9, $"{what}: pane {kv.Key} differs");
        }

        [Theory, MemberData(nameof(Corpora))]
        public void RaisingEveryBuildingMovesEverythingUp(string corpus)
        {
            var (flat, raised) = Pair(corpus);
            for (int bi = 0; bi < flat.Buildings.Count; bi++)
            {
                var a = flat.Buildings[bi]; var b = raised.Buildings[bi]; string n = a.name;
                var l0a = Lod0.Build(flat, a); var l0b = Lod0.Build(raised, b);
                Same(l0a.Op, l0b.Op, n + " LOD0"); Same(l0a.Glass, l0b.Glass, n + " glass");
                Assert.Equal(l0a.Segs.Count, l0b.Segs.Count);
                for (int k = 0; k < l0a.Segs.Count; k++) Assert.True(l0a.Segs[k].SequenceEqual(l0b.Segs[k]), $"{n}: storey {k}'s collision segments differ");
                Assert.True(l0a.Extra.Zip(l0b.Extra, (p, q) => p.s.Equals(q.s) && Math.Abs(p.y0 + E - q.y0) < 1e-6 && Math.Abs(p.y1 + E - q.y1) < 1e-6).All(x => x) && l0a.Extra.Count == l0b.Extra.Count, $"{n}: the bridge sides differ");
                Same(Lod1.Build(flat, a), Lod1.Build(raised, b), n + " LOD1");
                var m2a = Lod2.Build(flat, a); var m2b = Lod2.Build(raised, b);
                Shifted(m2a.P, m2b.P, n + " LOD2");
                // heights are site heights now, so a storey's height is a difference of two raised numbers: equal to rounding
                Assert.True(m2a.I.SequenceEqual(m2b.I) && Near(m2a.Fac, m2b.Fac) && Near(m2a.Fac2, m2b.Fac2) && m2a.Slot.SequenceEqual(m2b.Slot), $"{n}: LOD2's facade data differs");
                Assert.True(m2a.Rows.Count == m2b.Rows.Count && m2a.Rows.Zip(m2b.Rows, (p, q) => Near(p.spec, q.spec) && Near(p.run, q.run)).All(x => x), $"{n}: LOD2's rows differ");
                var ca = CollisionMesh.Build(flat, a, l0a); var cb = CollisionMesh.Build(raised, b, l0b);
                Shifted(ca.P, cb.P, n + " collision"); Assert.True(ca.I.SequenceEqual(cb.I) && ca.Kind.SequenceEqual(cb.Kind), $"{n}: collision triangles differ");
                for (int k = 0; k < a.floors.Count; k++) Same(SinkFootprint.Build(flat, a, k), SinkFootprint.Build(raised, b, k), $"{n} footprint {k}");
            }
        }

        [Theory, MemberData(nameof(Corpora))]
        public void TheWalkModelStandsOnRaisedFloors(string corpus)
        {
            var (flat, raised) = Pair(corpus);
            var wa = new PlayWorld(flat); var wb = new PlayWorld(raised);
            var rnd = new Random(7); int inside = 0;
            for (int bi = 0; bi < flat.Buildings.Count; bi++)
            {
                var b = flat.Buildings[bi];
                var fp = b.footprint; double x0 = fp.Min(q => q.x), x1 = fp.Max(q => q.x), z0 = fp.Min(q => q.z), z1 = fp.Max(q => q.z);
                for (int t = 0; t < 40; t++)
                {
                    double x = b.pos.x + x0 + rnd.NextDouble() * (x1 - x0), z = b.pos.z + z0 + rnd.NextDouble() * (z1 - z0);
                    int k = rnd.Next(b.floors.Count + 1); double y = Derived.FloorBase(b, k) + 0.3;
                    var la = wa.Locate(x, y, z); var lb = wb.Locate(x, y + E, z);
                    Assert.Equal(la?.b.id, lb?.b.id); Assert.Equal(la?.floor, lb?.floor);
                    if (la == null) continue;
                    inside++;
                    Assert.Equal(wa.SurfaceAt(x, z, y) + E, wb.SurfaceAt(x, z, y + E), 6);
                    Assert.Equal(wa.Collide(x, z, y), wb.Collide(x, z, y + E));
                }
            }
            Assert.True(inside > 100, $"only {inside} points landed inside a building");
        }

        [Fact]
        public void OcclusionWorksOnRaisedBuildings()
        {
            // the prototype's walks, with every building and the player raised: the same buildings sink, slice and cut
            // away, the same walls slide, every height in the rows moved up
            var (flat, raised) = Pair("demo");
            var root = PlayWorldTests.Play.Value.RootElement;
            foreach (var w in root.GetProperty("walks").EnumerateArray().Take(12))
            {
                var mode = w.GetProperty("occ").GetProperty("outside").GetString() switch { "sink" => OccluderMode.Sink, "slice" => OccluderMode.Slice, "cutout" => OccluderMode.Cutout, "fade" => OccluderMode.Fade, _ => OccluderMode.Off };
                var ca = new OcclusionCore(new PlayWorld(flat), new OcclusionSettings { mode = mode });
                var cb = new OcclusionCore(new PlayWorld(raised), new OcclusionSettings { mode = mode });
                var start = w.GetProperty("start").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                var c = w.GetProperty("cam").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                var p = new PlayerState(); p.Spawn(start[0], start[1], start[2]);
                var cam = new FollowCamera { yaw = c[0], pitch = c[1], dist = c[2] }; cam.Snap(p);
                var walkWorld = new PlayWorld(flat);
                foreach (var fr in w.GetProperty("frames").EnumerateArray())
                {
                    var input = fr.GetProperty("input");
                    bool K(string k) => input.TryGetProperty(k, out var v) && v.GetBoolean();
                    if (input.TryGetProperty("yaw", out var yv)) cam.yaw = yv.GetDouble();
                    Walker.Step(walkWorld, p, new MoveInput((K("KeyD") ? 1 : 0) - (K("KeyA") ? 1 : 0), (K("KeyW") ? 1 : 0) - (K("KeyS") ? 1 : 0), K("ShiftLeft")), cam.yaw, 1.0 / 30);
                    cam.Follow(p, 1.0 / 30);
                    var pos = cam.Position();
                    // the same walk lifted: inside a building it stands on raised floors, outside on raised ground
                    var q = new PlayerState(); q.Spawn(p.x, p.y + E, p.z);
                    ca.Frame(p, pos, (cam.tx, cam.ty, cam.tz), 1.0 / 30);
                    cb.Frame(q, (pos.x, pos.y + E, pos.z), (cam.tx, cam.ty + E, cam.tz), 1.0 / 30);
                    Assert.Equal(ca.View.active?.id, cb.View.active?.id);
                    Assert.Equal(ca.View.clipY + (ca.View.active != null ? E : 0), cb.View.clipY, 6);
                    Assert.Equal(ca.Active.Select(r => r.b.id), cb.Active.Select(r => r.b.id));
                    Assert.Equal(ca.Walls.Count, cb.Walls.Count);
                }
            }
        }

        // two buildings back to back along x = 10: a's east edge (1) is b's west edge (3)
        static (Site site, BuildingData a, BuildingData b) BackToBack(int floorsA, double elevA, int floorsB, double elevB)
        {
            var d = new StoreyDocument();
            void One(double x0, int floors, double elev)
            {
                var b = Edit.Buildings.Add(d, "rect", new Vec2(0, 0));
                b.pos = new Vec2(x0, 0); b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 8), new Vec2(0, 8) };
                b.floors = Enumerable.Range(0, floors).Select(_ => new FloorData()).ToList(); b.elevation = elev;
            }
            One(0, floorsA, elevA); One(10, floorsB, elevB);
            var site = new Site(d.buildings);
            return (site, site.Buildings[0], site.Buildings[1]);
        }

        /// <summary>The kinds of storey k's wall along edge i.</summary>
        static List<Party.PieceKind> Kinds(Site site, BuildingData b, int k, int i)
        {
            var fp = Derived.OutlineAt(b, k); bool ccw = Geo.Area2(fp) > 0; var cor = Geo.Corners(fp, ccw);
            double L = Geo.EdgeLen(fp, i);
            return Party.WallPieces(site, b, k, i, L, Geo.MiterOf(cor, i, L)).Select(pc => pc.kind).ToList();
        }

        [Fact]
        public void NeighboursAtDifferentHeightsShareOnlyTheHeightsBothReach()
        {
            // a: four storeys on the ground (top 12.6 m); b: two storeys raised a ground floor's height (3.6 to 10.2 m)
            var (site, a, b) = BackToBack(4, 0, 2, 3.6);
            var r = Assert.Single(Party.Ranges(site, a, 0, 1));
            Assert.Equal(3.6, r.Lo, 6); Assert.Equal(10.2, r.Hp, 6);
            Assert.True(r.own, "the taller owns the shared wall");
            // a's ground floor is below b: its own outside wall, windows and all; above, the shared wall, blank
            Assert.DoesNotContain(Party.PieceKind.Party, Kinds(site, a, 0, 1)); Assert.DoesNotContain(Party.PieceKind.Skip, Kinds(site, a, 0, 1));
            Assert.Contains(Party.PieceKind.Party, Kinds(site, a, 1, 1));
            // b is wholly within the band: it leaves its wall to a
            Assert.All(Enumerable.Range(0, 2), k => Assert.Contains(Party.PieceKind.Skip, Kinds(site, b, k, 3)));
        }

        [Fact]
        public void StaggeredNeighboursEachKeepTheirWallOutsideTheBand()
        {
            // a: two storeys on the ground (to 6.6 m); b: three raised 3.6 m (to 13.2 m). b owns the band 3.6 to 6.6 m
            var (site, a, b) = BackToBack(2, 0, 3, 3.6);
            var r = Assert.Single(Party.Ranges(site, a, 0, 1));
            Assert.False(r.own); Assert.Equal(3.6, r.Lo, 6); Assert.Equal(6.6, r.Hp, 6);
            Assert.DoesNotContain(Party.PieceKind.Skip, Kinds(site, a, 0, 1));   // below b: a's own wall
            Assert.Contains(Party.PieceKind.Skip, Kinds(site, a, 1, 1));         // in the band: b's
            Assert.Contains(Party.PieceKind.Party, Kinds(site, b, 0, 3));        // b builds it blank
            Assert.DoesNotContain(Party.PieceKind.Party, Kinds(site, b, 1, 3));  // above a: b's own wall
        }

        [Fact]
        public void NeighboursThatDontOverlapInHeightShareNothing()
        {
            var (site, a, _) = BackToBack(2, 0, 2, 20);
            Assert.Empty(Party.Ranges(site, a, 0, 1));
        }

        [Fact]
        public void TheWalkModelDoesntPutThePlayerInABuildingAbove()
        {
            var (site, a, _) = BackToBack(2, 5, 2, 0);
            var w = new PlayWorld(site);
            Assert.Null(w.Locate(5, 0, 4));          // on the ground under a, raised 5 m
            Assert.Equal(a, w.Locate(5, 5.1, 4)?.b);  // on its ground floor
            Assert.Equal(5, w.SurfaceAt(5, 4, 5.3), 6);
        }

        [Fact]
        public void ElevationIsSavedOnlyWhenRaised()
        {
            var (_, a, b) = BackToBack(2, 0, 2, 3.25);
            Assert.DoesNotContain("elevation", PrototypeJson.Write(a));
            Assert.Equal(3.25, PrototypeJson.ReadBuilding(PrototypeJson.Write(b)).elevation);
        }

        [Fact]
        public void GroundIsSampledInsideTheOutline()
        {
            var (_, a, _) = BackToBack(2, 0, 2, 0);
            var pts = Edit.Ground.Samples(a);
            Assert.Equal(2 * a.footprint.Count + 1, pts.Count);   // every corner, every edge's middle, the centre
            Assert.All(pts, p => Assert.True(Geo.Pip(a.footprint, p.x - a.pos.x, p.z - a.pos.z), $"({p.x}, {p.z}) is outside"));
        }

        [Fact]
        public void SnappingStandsOnTheHighestAndFoundsToTheLowest()
        {
            var (_, a, b) = BackToBack(2, 0, 2, 0);
            var shared = a.style; b.style = shared;   // two buildings with one style object: snapping one mustn't change the other
            Assert.Equal(1.3, Edit.Ground.Snap(a, new[] { 2.0, 2.4, 1.1 })!.Value, 6);
            Assert.Equal(2.4, a.elevation);
            Assert.Equal(1.4, a.style.foundation);          // 10 cm past the lowest point
            Assert.Null(b.style.foundation);

            a.style.foundation = 3;                          // one already deeper stays
            Edit.Ground.Snap(a, new[] { 5.0, 4.0 });
            Assert.Equal(5, a.elevation); Assert.Equal(3, a.style.foundation);

            var flat = b.style;
            Edit.Ground.Snap(b, new[] { 0.52, 0.5 });         // within 5 cm: no foundation needed
            Assert.Equal(0.52, b.elevation); Assert.Same(flat, b.style);

            Assert.Null(Edit.Ground.Snap(b, new double[0])); // nothing found: nothing changes
            Assert.Equal(0.52, b.elevation);
        }

        [Fact]
        public void RaisingABuildingRebuildsTheNeighbourItSharesAWallWith()
        {
            var (site, a, b) = BackToBack(3, 0, 3, 0);
            var d = new StoreyDocument(); d.buildings.AddRange(site.Buildings);
            var e = new Edit.EditSession(PrototypeJson.Write(d));
            e.Document.buildings[1].elevation = 3.6;
            var change = e.Commit();
            Assert.Contains(a.id, change.rebuild); Assert.Contains(b.id, change.rebuild);
        }
    }
}
