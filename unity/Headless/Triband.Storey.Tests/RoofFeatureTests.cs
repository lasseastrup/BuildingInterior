using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Mansard roofs and dormers (<see cref="RoofType.Mansard"/>, <see cref="FacadeStyle.dormers"/>). Storey's own.</summary>
    public class RoofFeatureTests
    {
        static (StoreyDocument d, BuildingData b, Site site) Block(RoofType type, double? dormers = null, double w = 16, double dd = 11)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(w, 0), new Vec2(w, dd), new Vec2(0, dd) };
            b.entrances.Clear(); b.details.Clear(); b.blank.Clear(); b.shafts.Clear();
            foreach (var f in b.floors) f.walls.Clear();
            b.style.roofType = type; b.style.dormers = dormers;
            return (d, b, new Site(d.buildings));
        }

        static double PlanArea(RoofPart p)
        {
            double a = 0; var r = p.Pts;
            for (int i = 0; i < r.Count; i++) { var u = r[i]; var v = r[(i + 1) % r.Count]; a += u.x * v.z - v.x * u.z; }
            double h = 0; foreach (var hole in p.Holes) for (int i = 0; i < hole.Count; i++) { var u = hole[i]; var v = hole[(i + 1) % hole.Count]; h += u.x * v.z - v.x * u.z; }
            return Math.Abs(a / 2) - Math.Abs(h / 2);
        }

        [Fact]
        public void AMansardIsSteepThenShallow()
        {
            var (_, b, site) = Block(RoofType.Mansard);
            var R = Roofs.Parts(site, b)!; double top = Derived.RoofY(b), brk = top + 2.4;
            var roof = R.Parts.Where(p => p.Kind == RoofKind.Roof).ToList();
            var steep = roof.Where(p => Math.Abs(p.N.y - Math.Cos(Roofs.MansardPitch * Math.PI / 180)) < 1e-6).ToList();
            var shallow = roof.Where(p => Math.Abs(p.N.y - Math.Cos(20 * Math.PI / 180)) < 1e-6).ToList();
            Assert.Equal(4, steep.Count); Assert.Equal(4, shallow.Count);
            Assert.All(steep.SelectMany(p => p.Pts), q => Assert.True(q.y <= brk + 1e-4, $"steep face at {q.y:0.000} above the break {brk:0.000}"));
            Assert.All(shallow.SelectMany(p => p.Pts), q => Assert.True(q.y >= brk - 1e-4));
            Assert.Contains(steep.SelectMany(p => p.Pts), q => Math.Abs(q.y - brk) < 1e-4);
        }

        [Theory]
        [InlineData(RoofType.Hip)]
        [InlineData(RoofType.Mansard)]
        public void TheRoofCoversTheBuildingWithoutGapsOrOverlaps(RoofType type)
        {
            var (_, b, site) = Block(type);
            var roof = Roofs.Parts(site, b)!.Parts.Where(p => p.Kind == RoofKind.Roof).ToList();
            double got = roof.Sum(PlanArea), e = type == RoofType.Mansard ? 0.2 : 0.35, o = Dim.T_EXT + e;
            double want = (16 + 2 * o) * (11 + 2 * o);   // the eave line all round
            Assert.Equal(want, got, 2);
        }

        [Theory]
        [InlineData(RoofType.Hip)]
        [InlineData(RoofType.Gable)]
        [InlineData(RoofType.Mansard)]
        [InlineData(RoofType.Shed)]
        public void ItBuildsCleanly(RoofType type)
        {
            var (_, b, site) = Block(type, 3);
            var r = Lod0.Build(site, b, solids: true);
            var report = CoplanarCheck.Run(r.Op);
            Assert.True(report.Overlaps.Count == 0, string.Join("\n", report.Overlaps.Take(10)));
            Assert.Empty(CoplanarCheck.Run(Lod1.Build(site, b, solids: true)).Overlaps);
            Assert.True(r.Op.Tris > 0);
        }

        [Theory]
        [InlineData(RoofType.Hip, 3)]
        [InlineData(RoofType.Gable, 3)]
        [InlineData(RoofType.Mansard, 3)]
        [InlineData(RoofType.Shed, 3)]
        public void DormersSitOnTheSlope(RoofType type, double every)
        {
            var (_, b, site) = Block(type, every);
            var R = Roofs.Parts(site, b)!; double top = Derived.RoofY(b);
            var fronts = R.Parts.Where(p => p.Dormer && p.Holes.Count == 1).ToList();
            Assert.NotEmpty(fronts);
            Assert.Equal(fronts.Count, R.Parts.Count(p => p.Dormer && p.Kind == RoofKind.Window));
            foreach (var f in fronts)
            {
                double y0 = f.Pts.Min(q => q.y);
                Assert.True(y0 > top, $"a dormer starts at {y0:0.00}, below the wall top {top:0.00}");
                // the front stands inside the walls' outer face (16 × 11 plus the wall)
                foreach (var q in f.Pts) Assert.True(q.x > -Dim.T_EXT && q.x < 16 + Dim.T_EXT && q.z > -Dim.T_EXT && q.z < 11 + Dim.T_EXT, $"dormer corner {q} outside the walls");
            }
            if (type == RoofType.Mansard)
                Assert.All(R.Parts.Where(p => p.Dormer).SelectMany(p => p.Pts), q => Assert.True(q.y <= top + 2.4 + 1e-4, "a dormer pokes above the mansard's break"));
            // fewer than every 3 m of eave, never closer than 3 m
            Assert.True(fronts.Count <= 2 * (16 / 3 + 11 / 3));
        }

        [Fact]
        public void NoDormersUnlessAsked()
        {
            var (_, b, site) = Block(RoofType.Hip);
            Assert.DoesNotContain(Roofs.Parts(site, b)!.Parts, p => p.Dormer);
        }

        [Fact]
        public void ATinyRoofTakesNoDormers()
        {
            var (_, b, site) = Block(RoofType.Hip, 3, 4, 4);
            Assert.DoesNotContain(Roofs.Parts(site, b)!.Parts, p => p.Dormer);
        }

        [Fact]
        public void MassingLeavesDormersOut()
        {
            var (_, b, site) = Block(RoofType.Hip, 3);
            var (_, b2, site2) = Block(RoofType.Hip);
            Assert.Equal(Lod2.Build(site2, b2).Tris, Lod2.Build(site, b).Tris);
            Assert.True(Lod0.Build(site, b).Op.Tris > Lod0.Build(site2, b2).Op.Tris);
        }

        [Fact]
        public void TheyRoundTripThroughTheLayout()
        {
            var (d, b, _) = Block(RoofType.Mansard, 3.5);
            b.style.mansard = 3.1;
            var rd = PrototypeJson.Read(PrototypeJson.Write(d)); Assert.Empty(rd.Unknown);
            var back = rd.Document.buildings[0].style;
            Assert.Equal(RoofType.Mansard, back.roofType);
            Assert.Equal(3.1, back.mansard);
            Assert.Equal(3.5, back.dormers);
        }

        [Fact]
        public void ExistingRoofsAreUnchanged()
        {
            // a hip roof without dormers is what it was: the same parts as before mansards existed (spot check: the eave)
            var (_, b, site) = Block(RoofType.Hip);
            var R = Roofs.Parts(site, b)!;
            double hb = Derived.RoofY(b) - 0.35 * Math.Tan(30 * Math.PI / 180);
            Assert.Contains(R.Parts, p => p.Kind == RoofKind.Trim && Math.Abs(p.Pts.Max(q => q.y) - hb) < 1e-9);
        }

        [Fact]
        public void DormerWindowsAreDressedAsTheFacadesAre()
        {
            int Frames(BuildingData b, Site site) { var op = Lod0.Build(site, b).Op; return Enumerable.Range(0, op.Verts).Count(i => op.C[i].slot == ColorSlot.Frame); }
            var (_, b0, s0) = Block(RoofType.Gable, 3.5);
            int plain = Frames(b0, s0);
            var (_, b, site) = Block(RoofType.Gable, 3.5);
            b.style.frames = true; b.style.paneCols = 2; b.style.paneRows = 2;
            var (_, bf, sf) = Block(RoofType.Gable);
            bf.style.frames = true; bf.style.paneCols = 2; bf.style.paneRows = 2;
            // the dormers' windows take frames and bars too: more than the facade's alone
            Assert.True(Frames(b, site) > Frames(bf, sf) && Frames(bf, sf) > plain);
            var r = Lod0.Build(site, b);
            var rep = CoplanarCheck.Run(r.Op);
            Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
            // and their glass carries the window shader's coordinates, in LOD0 and LOD1, above the walls
            double top = Derived.FloorBase(b, b.floors.Count);
            Assert.Contains(r.Op.PaneUV.Keys, i => r.Op.P[i].y > top);
            var l1 = Lod1.Build(site, b);
            Assert.Contains(l1.PaneUV.Keys, i => l1.P[i].y > top);
        }

        // ---- gable and shed roofs over cut corners: planned on the sharp outline ----

        static (BuildingData b, RoofParts R) Rounded(RoofType type, CornerShape shape)
        {
            var (d, b, _) = Block(type);
            Assert.True(CornerCuts.CutAll(b, 0, shape, 3) > 0);
            var site = new Site(d.buildings);
            return (b, Roofs.Parts(site, b)!);
        }

        static List<P3> Slopes(RoofParts R) =>
            R.Parts.Where(p => p.Kind == RoofKind.Roof).Select(p => p.N).Aggregate(new List<P3>(), (l, n) =>
            { if (!l.Any(o => Math.Abs(o.x - n.x) < 1e-3 && Math.Abs(o.y - n.y) < 1e-3 && Math.Abs(o.z - n.z) < 1e-3)) l.Add(n); return l; });

        [Theory]
        [InlineData(CornerShape.Round)]
        [InlineData(CornerShape.Chamfer)]
        public void AGableOverCutCornersHasTwoSlopes(CornerShape shape)
        {
            var (b, R) = Rounded(RoofType.Gable, shape);
            var slopes = Slopes(R);
            Assert.Equal(2, slopes.Count);   // not a fan of little hips at each rounded corner
            Assert.Contains(R.Parts, p => p.Kind == RoofKind.Wall);   // the gable ends
            NoSpikes(b, R);
        }

        [Theory]
        [InlineData(CornerShape.Round)]
        [InlineData(CornerShape.Chamfer)]
        public void AShedOverCutCornersIsOnePlane(CornerShape shape)
        {
            var (b, R) = Rounded(RoofType.Shed, shape);
            Assert.Single(Slopes(R));
            NoSpikes(b, R);
        }

        [Fact]
        public void AHipOverRoundedCornersStillFollowsThem()
        {
            // only gables and sheds are planned on the sharp outline: a hip's rounded corners round its hips
            var (_, R) = Rounded(RoofType.Hip, CornerShape.Round);
            Assert.True(Slopes(R).Count > 4);
        }

        /// <summary>Nothing reaches further out than the eave's overhang past the outline's bounds.</summary>
        static void NoSpikes(BuildingData b, RoofParts R)
        {
            var bb = Tiers.Bbox(b.footprint); double reach = Dim.T_EXT + 1.3;
            foreach (var p in R.Parts) foreach (var q in p.Pts)
            {
                Assert.InRange(q.x - b.pos.x, bb.x0 - reach, bb.x1 + reach);
                Assert.InRange(q.z - b.pos.z, bb.z0 - reach, bb.z1 + reach);
            }
        }
    }
}
