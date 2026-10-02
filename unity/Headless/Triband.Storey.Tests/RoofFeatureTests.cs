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
    }
}
