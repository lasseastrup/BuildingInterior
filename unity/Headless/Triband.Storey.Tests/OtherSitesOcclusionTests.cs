using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Buildings of other sites in the way (docs/PLAY.md §7). The player is in one site, but a building of the next district
    /// over, or of a prop placed as a site of its own, can stand between the camera and the player. Each such site's core
    /// looks for its own buildings in the way alone (<see cref="OcclusionCore.FrameOccluders"/>), the player and the camera
    /// in its space, and every core drawing through the building table takes its occluders' slots from one set
    /// (<see cref="OccluderSlots"/>). Which sites are looked at is the Unity half's (<c>StoreyOcclusion.FrameGuests</c>,
    /// pinned below), by how near the camera's line to the player comes to each (<see cref="Districts.Area"/>). Storey's own.
    /// </summary>
    public class OtherSitesOcclusionTests
    {
        const double Dt = 1.0 / 30;
        static OcclusionSettings Dissolve => new OcclusionSettings { mode = OccluderMode.Dissolve };

        /// <summary>
        /// A pillar placed as a site of its own: one building 2 m square and four storeys (3.6 m, then 3 m), no door, its
        /// id a new layout's first, as in every other layout.
        /// </summary>
        static PlayWorld Pillar(double elevation = 0)
        {
            var d = new StoreyDocument(); var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.pos = new Vec2(0, 0); b.elevation = elevation; b.interior = false; b.entrances.Clear();
            b.footprint = new List<Vec2> { new Vec2(-1, -1), new Vec2(1, -1), new Vec2(1, 1), new Vec2(-1, 1) };
            b.floors = Enumerable.Range(0, 4).Select(_ => new FloorData()).ToList();
            return new PlayWorld(new Site(d.buildings));
        }

        static PlayerState At(double x, double y, double z) { var p = new PlayerState(); p.Spawn(x, y, z); return p; }
        static int Row(Occluder o) => o.slot * OcclusionCore.RowTexels * 4;

        /// <summary>
        /// Two sites, a building of each in the way at once: one pillar at the origin, one 3 m along the camera's line to the
        /// player and standing 2 m higher, each with the same id. Each takes a slot of its own from the set they share, its
        /// row at that slot as its own core wrote it (Dissolve: the fade, and the floor of the storey that stays, in its
        /// site's space), and what each hides is its own building's. One let go gives back its slot and no other.
        /// </summary>
        [Fact]
        public void TwoSitesBuildingsInTheWayTakeSlotsOfTheirOwn()
        {
            var slots = new OccluderSlots();
            var a = new OcclusionCore(Pillar(), Dissolve, slots);
            var b = new OcclusionCore(Pillar(2), Dissolve, slots);
            var ba = a.World.Site.Buildings[0]; var bb = b.World.Site.Buildings[0];
            Assert.Equal(ba.id, bb.id);
            Assert.Same(slots.Rows, a.Rows); Assert.Same(slots.Rows, b.Rows);
            // the camera 8 m west of the first and 7 m up, the player 6 m east of it, on the ground: each site sees them in its own space
            for (int f = 0; f < 40; f++)
            {
                a.FrameOccluders(At(6, 0, 0), (-8, 7, 0), Dt);
                b.FrameOccluders(At(3, 0, 0), (-11, 7, 0), Dt);
            }
            var oa = Assert.Single(a.Active); var ob = Assert.Single(b.Active);
            Assert.Same(ba, oa.b); Assert.Same(bb, ob.b);
            Assert.NotEqual(oa.slot, ob.slot);
            Assert.Equal(2, slots.Taken);
            Assert.Equal(1f, a.Rows[Row(oa) + 1]); Assert.Equal(0f, a.Rows[Row(oa) + 3]);
            Assert.Equal(1f, b.Rows[Row(ob) + 1]); Assert.Equal(2f, b.Rows[Row(ob) + 3]);
            Assert.True(a.HidesPoint(ba, 1)); Assert.True(b.HidesPoint(bb, 3)); Assert.False(b.HidesPoint(bb, 1));
            a.Clear();
            Assert.Empty(a.Active);
            Assert.Equal(1, slots.Taken);
            Assert.Equal(ob.slot, Assert.Single(b.Active).slot);
            Assert.Equal(2f, b.Rows[Row(ob) + 3]);
            // and the slot given back is taken again by the next building in the way
            for (int f = 0; f < 40; f++) a.FrameOccluders(At(6, 0, 0), (-8, 7, 0), Dt);
            Assert.NotEqual(ob.slot, Assert.Single(a.Active).slot);
            Assert.Equal(2, slots.Taken);
        }

        /// <summary>
        /// All sixteen slots taken: a building in the way of another site waits for one, as one of the player's site does, and
        /// takes it when it is given back.
        /// </summary>
        [Fact]
        public void WhenEverySlotIsTakenAnotherSitesBuildingWaitsForOne()
        {
            var slots = new OccluderSlots();
            var full = Enumerable.Range(0, OcclusionCore.Slots).Select(_ => new OcclusionCore(Pillar(), Dissolve, slots)).ToList();
            foreach (var c in full) c.FrameOccluders(At(6, 0, 0), (-8, 7, 0), Dt);
            Assert.Equal(OcclusionCore.Slots, slots.Taken);
            var late = new OcclusionCore(Pillar(), Dissolve, slots);
            late.FrameOccluders(At(6, 0, 0), (-8, 7, 0), Dt);
            Assert.Empty(late.Active);
            full[5].Clear();
            late.FrameOccluders(At(6, 0, 0), (-8, 7, 0), Dt);
            Assert.Single(late.Active);
            Assert.Equal(OcclusionCore.Slots, slots.Taken);
        }

        /// <summary>
        /// Two districts, B's site 10 m east of A's, with a building of B's (the pillar) between the camera and the player, who
        /// is in A. A's site holds a copy of it for the party walls along the edge (<see cref="Site.WithContext"/>), never
        /// drawn there: A's core leaves it alone, and B's own core, in B's space, takes it. One slot, not two.
        /// </summary>
        [Fact]
        public void ANeighbouringDistrictsBuildingIsItsOwnDistrictsToTake()
        {
            var a = new StoreyDocument(); var ab = Buildings.Add(a, "rect", new Vec2(0, 0));
            ab.pos = new Vec2(0, 0); ab.entrances.Clear();
            ab.footprint = new List<Vec2> { new Vec2(8, 2.5), new Vec2(12, 2.5), new Vec2(12, 6), new Vec2(8, 6) };   // beside the line, within reach of B's
            var bWorld = Pillar(); var bb = bWorld.Site.Buildings[0];
            var siteA = Site.WithContext(a.buildings, "A", new[] { (bWorld.Site.Buildings, "B", 10.0, 0.0) });
            Assert.Equal(1, siteA.Own); Assert.Equal(2, siteA.Buildings.Count);   // B's pillar kept as context
            var slots = new OccluderSlots();
            var coreA = new OcclusionCore(new PlayWorld(siteA), Dissolve, slots);
            var coreB = new OcclusionCore(bWorld, Dissolve, slots);
            // the player 4 m east of the pillar, the camera 8 m west of it and 7 m up: in A's space, and in B's
            for (int f = 0; f < 40; f++)
            {
                coreA.Frame(At(14, 0, 0), (2, 7, 0), (14, 1.2, 0), Dt);
                coreB.FrameOccluders(At(4, 0, 0), (-8, 7, 0), Dt);
            }
            Assert.Null(coreA.View.active);
            Assert.Empty(coreA.Active);
            Assert.Same(bb, Assert.Single(coreB.Active).b);
            Assert.Equal(1, slots.Taken);
        }

        /// <summary>
        /// The player inside the pillar, the camera outside it. Walked in (<see cref="OcclusionCore.Frame"/>), the pillar holds
        /// them: it is the view's, its walls between the camera and the player down, and never in the way. Looked at as another
        /// site's (<see cref="OcclusionCore.FrameOccluders"/>), it holds nobody: no view, the walls slide back up, and it is in
        /// the way like any building of the site.
        /// </summary>
        [Fact]
        public void ASiteThePlayerIsntInHasNoView()
        {
            var core = new OcclusionCore(Pillar(), Dissolve);
            var b = core.World.Site.Buildings[0];
            var p = At(0, 0, 0); var cam = (-8.0, 7.0, 0.0);
            for (int f = 0; f < 20; f++) core.Frame(p, cam, (0, 1.2, 0), Dt);
            Assert.Same(b, core.View.active);
            Assert.Empty(core.Active);
            Assert.NotEmpty(core.Walls);
            for (int f = 0; f < 20; f++) core.FrameOccluders(p, cam, Dt);
            Assert.Null(core.View.active);
            Assert.Equal(1e9, core.View.clipY);
            Assert.Equal(1e9, core.View.terraceY);
            Assert.False(core.View.cutOn);
            Assert.Same(b, Assert.Single(core.Active).b);
            Assert.Empty(core.Walls);
        }

        /// <summary>Another site's frame makes no garbage, with its building in the way and out of it as the camera swings round.</summary>
        [Fact]
        public void AnotherSitesFrameMakesNoGarbage()
        {
            var slots = new OccluderSlots();
            var walk = new OcclusionCore(Pillar(), Dissolve, slots);
            var guest = new OcclusionCore(Pillar(), Dissolve, slots);
            var p = new PlayerState();
            long bytes = 0; int frames = 0, inTheWay = 0;
            for (int f = 0; f < 800; f++)
            {
                // the camera swinging round the player, 9 m off: the pillar, 4 m from the player, is in the way part of the time
                double yaw = f * 0.02, cx = 4 + Math.Cos(yaw) * 9, cz = Math.Sin(yaw) * 9;
                p.Spawn(4, 0, 0);
                walk.FrameOccluders(p, (cx, 7, cz), Dt);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                guest.FrameOccluders(p, (cx, 7, cz), Dt);
                long a1 = GC.GetAllocatedBytesForCurrentThread();
                if (f < 400) continue;   // warm-up: the building's shape for the segment test, made once
                bytes += a1 - a0; frames++;
                if (guest.Active.Count > 0) inTheWay++;
            }
            Assert.True(inTheWay > 10 && inTheWay < frames - 10, $"in the way {inTheWay} of {frames} frames: the walk doesn't test it");
            Assert.True(bytes / frames < 64, $"{bytes / frames} bytes a frame");
        }

        /// <summary>
        /// How near the camera's line to the player comes to a site's ground plan, which decides whether its buildings are
        /// looked at (<see cref="Districts.Area.Distance(double, double, double, double)"/>): 0 through it or from inside it,
        /// the gap past it otherwise, an end or a corner the nearest.
        /// </summary>
        [Fact]
        public void HowNearTheCamerasLineComesToASite()
        {
            var a = new Districts.Area(0, 0, 10, 10);
            Assert.Equal(0, a.Distance(-5, 5, 15, 5));        // through it
            Assert.Equal(0, a.Distance(5, 5, 20, 20));        // from inside
            Assert.Equal(0, a.Distance(-1, 11, 11, -1));      // across a corner
            Assert.Equal(0, a.Distance(5, -3, 5, 0));         // up to its edge
            Assert.Equal(3, a.Distance(-5, 13, 15, 13), 9);   // along it, 3 m off
            Assert.Equal(Math.Sqrt(0.5), a.Distance(12, 9, 9, 12), 9);   // past a corner, nearest along the line
            Assert.Equal(Math.Sqrt(2), a.Distance(11, 11, 20, 20), 9);   // pointing away from a corner: its end
            Assert.Equal(5, a.Distance(15, 5, 15, 5), 9);     // a camera at the player
            Assert.Equal(a.Distance(15, 5), a.Distance(15, 5, 15, 5), 9);
            // never further than either end, as a segment ending there can't be
            var r = new Random(7);
            for (int i = 0; i < 2000; i++)
            {
                double ax = r.NextDouble() * 40 - 15, az = r.NextDouble() * 40 - 15, bx = r.NextDouble() * 40 - 15, bz = r.NextDouble() * 40 - 15;
                double d = a.Distance(ax, az, bx, bz);
                Assert.True(d <= Math.Min(a.Distance(ax, az), a.Distance(bx, bz)) + 1e-9);
                // and the segment's points sampled finely come no nearer
                double best = double.MaxValue;
                for (int s = 0; s <= 400; s++) { double t = s / 400.0; best = Math.Min(best, a.Distance(ax + (bx - ax) * t, az + (bz - az) * t)); }
                Assert.True(d <= best + 1e-9 && best - d < 0.1, $"{d} against {best} sampled");
            }
        }

        /// <summary>
        /// The Unity half compiles only in Unity, so what the tests above stand on is pinned in its source. Every site's core
        /// takes its slots from the table's one set. The sites but the player's are looked at with
        /// <see cref="OcclusionCore.FrameOccluders"/>, in their own space, when their ground plan comes near the camera's line
        /// to the player or a building of theirs is still going; their occluders go through their own renderer, at their own
        /// height, with their footprints under them; and one that stops being looked at has what it wrote undone and its slots
        /// given back. Follow Player follows districts alone, and a site's LOD and its props ask its own core.
        /// </summary>
        [Fact]
        public void TheOtherSitesAreLookedAtInTheirOwnSpaceAndOnlyDistrictsAreFollowed()
        {
            string Text(params string[] path) => System.IO.File.ReadAllText(UnityAsset.Package("com.triband.storey", path)).Replace("\r\n", "\n");
            var occ = Text("Unity", "Occlusion", "StoreyOcclusion.cs");
            string Method(string text, string start, string next)
            {
                int i = text.IndexOf(start, StringComparison.Ordinal); Assert.True(i > 0, start);
                int j = text.IndexOf(next, i, StringComparison.Ordinal); Assert.True(j > i, next);
                return text.Substring(i, j - i);
            }
            Assert.Contains("new OcclusionCore(w.world, core, r.Table.OccluderSlots)", occ);
            var guests = Method(occ, "void FrameGuests(Vector3 feetWorld, Vector3 camWorld, float dt)", "/// <summary>");
            Assert.Contains("a.Distance(camWorld.x, camWorld.z, feetWorld.x, feetWorld.z) <= GuestReach", guests);
            Assert.Contains("bool busy = gw?.occ != null && gw.occ.Active.Count > 0;", guests);
            Assert.Contains("oc.FrameOccluders(p, (camWorld.x - o.x, camWorld.y - o.y, camWorld.z - o.z), dt);", guests);
            Assert.Contains("WriteOccluders(gr, s, gw, oc, o);", guests);   // its place: heights and the kept doors' boxes in world space
            Assert.Contains("Leave(s, gw);", guests);
            Assert.Contains("WriteOccluders(r, site!, w, o, origin);", Method(occ, "void Write(SiteRenderer r,", "/// <summary>"));
            Assert.Contains("ShowFootprint(r, s, w, oc.b, oc.k, oc.slot);", Method(occ, "void WriteOccluders(", "/// <summary>"));
            Assert.Contains("go.transform.SetParent(s.transform, false);", occ);
            Assert.Contains("w.occ?.Clear();", Method(occ, "void Drop(Walked w)", "void ClearFootprints("));
            Assert.Contains("w.occ?.Clear();", Method(occ, "bool Ready(Walked w, SiteRenderer r, Site s, BuildLane lane)", "w.making = MakeWorld("));
            Assert.Contains("s.isActiveAndEnabled && s.district && s.PlanArea is Lod.Districts.Area a", Method(occ, "void Update()", "/// <summary>"));
            var site = Text("Unity", "Rendering", "StoreySite.cs");
            Assert.Contains("public bool district = true;", site);
            Assert.Contains("var core = occ != null ? occ.CoreOf(this) : null;", Method(site, "void PickFor(SiteRenderer s, SiteView? v)", "float now = Time.unscaledTime;"));
            Assert.Contains("var core = occ != null ? occ.CoreOf(e.site!) : null;", Text("Unity", "Props", "StoreyProps.cs"));
            Assert.Contains("!other.district", System.IO.File.ReadAllText(UnityAsset.Package("com.triband.storey.authoring", "Editor", "StoreySiteEditor.cs")));
        }
    }
}
