using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Sink's footprint (SPEC §5.1): a near-black fill of the storey's outline and a low rim of its wall, never along a party wall.</summary>
    public class SinkFootprintTests
    {
        [Fact]
        public void EveryStoreyOfTheDemoHasAFootprint()
        {
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            var site = new Site(doc.buildings);
            foreach (var b in doc.buildings)
                for (int k = 0; k < b.floors.Count; k++)
                {
                    var op = SinkFootprint.Build(site, b, k);
                    double y = Derived.FloorBase(b, k);
                    Assert.True(op.Tris > 0, $"{b.name} storey {k}");
                    Assert.All(op.P, p => Assert.InRange(p.y, y - 1e-9, y + SinkFootprint.RimHeight + 1e-9));
                    Assert.True(op.C.Count(c => c.tone == SinkFootprint.FillTone) >= 3, $"{b.name} storey {k}: no near-black fill");
                }
        }

        [Fact]
        public void ThereIsNoRimAlongAPartyWall()
        {
            // the demo's row of joined buildings: the party edge's stretch gets no rim, so the footprints meet flush
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            var site = new Site(doc.buildings);
            int withParty = 0;
            foreach (var b in doc.buildings)
            {
                var fp = Derived.OutlineAt(b, 0);
                for (int i = 0; i < fp.Count; i++)
                {
                    var party = Party.Ranges(site, b, 0, i);
                    if (party.Count == 0) continue;
                    withParty++;
                    var op = SinkFootprint.Build(site, b, 0);
                    var a = fp[i]; var c = fp[(i + 1) % fp.Count]; double L = Edit.Tiers.Hypot(c.x - a.x, c.z - a.z);
                    // rim vertices (above the fill) on this edge's line, within its party stretch, away from its ends
                    var onParty = op.P.Where(p => p.y > Derived.FloorBase(b, 0) + 0.05).Where(p =>
                    {
                        double lx = p.x - b.pos.x - a.x, lz = p.z - b.pos.z - a.z, s = (lx * (c.x - a.x) + lz * (c.z - a.z)) / L, off = System.Math.Abs(lx * (c.z - a.z) - lz * (c.x - a.x)) / L;
                        return off < 0.02 && party.Any(r => s > r.s + 0.5 && s < r.e - 0.5);
                    });
                    Assert.Empty(onParty);
                }
            }
            Assert.True(withParty > 0, "the demo has party walls");
        }

        /// <summary>
        /// Dissolve's Outline (SinkFootprint.Cage): lines round the building from the storey up, at every storey's floor and
        /// the roof's eaves, and posts up its corners, all standing off its outer walls, outside its outline. The footprint
        /// without it is as it was.
        /// </summary>
        [Fact]
        public void TheOutlineStandsOffTheWallsFromTheStoreyUp()
        {
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            var site = new Site(doc.buildings);
            const double W = 0.06, H = W / 2, Hp = H * SinkFootprint.PostOver;
            int checkedPlan = 0;
            foreach (var b in doc.buildings)
                foreach (int k in new[] { 0, b.floors.Count / 2 })
                {
                    var plain = SinkFootprint.Build(site, b, k); int n0 = plain.P.Count, t0 = plain.Tris; plain.Recycle();
                    var bare = SinkFootprint.Build(site, b, k, 0, true);
                    Assert.Equal(n0, bare.P.Count); Assert.Equal(t0, bare.Tris); bare.Recycle();
                    foreach (bool storeys in new[] { false, true })
                    {
                        var op = SinkFootprint.Build(site, b, k, W, storeys);
                        Assert.True(op.Tris > t0, $"{b.name} storey {k}: no outline");
                        var cage = op.P.Skip(n0).ToList();
                        double y = Derived.FloorBase(b, k), top = Derived.RoofY(b);
                        Assert.All(cage, p => Assert.InRange(p.y, y - H - 1e-9, top + Hp + 1e-9));
                        // the roof's eaves, and with storeys every storey's floor over this one, have a line
                        bool LineAt(double ly) => cage.Any(p => System.Math.Abs(p.y - (ly + H)) < 1e-6);
                        Assert.True(LineAt(top), $"{b.name} storey {k}: no line at the roof");
                        // (a setback's floor has one whatever: the top of the tier under it)
                        var tierTops = Party.Tiers(b).Select(t => t.k1).ToHashSet();
                        for (int j = k + 1; j < b.floors.Count; j++)
                            Assert.Equal(storeys || tierTops.Contains(j), LineAt(Derived.FloorBase(b, j)));
                        // a building of one outline: every line and post stands outside it, off its walls
                        if (Party.Tiers(b).Count == 1)
                        {
                            var fp = Derived.OutlineAt(b, 0);
                            foreach (var p in cage)
                            {
                                double lx = p.x - b.pos.x, lz = p.z - b.pos.z;
                                Assert.False(Geo.Pip(fp, lx, lz), $"{b.name}: an outline point inside the building");
                                double d = Enumerable.Range(0, fp.Count).Min(i => Edit.Tiers.SegDist(lx, lz, fp[i].x, fp[i].z, fp[(i + 1) % fp.Count].x, fp[(i + 1) % fp.Count].z).d);
                                // the outline is the walls' inner line: their faces are a wall's thickness out
                                Assert.True(d > Dim.T_EXT + SinkFootprint.CageClear - 1e-6, $"{b.name}: an outline point {d:F3} m off its wall's inner line");
                            }
                            checkedPlan++;
                        }
                        op.Recycle();
                    }
                }
            Assert.True(checkedPlan > 0, "no building of one outline in the demo");
        }

        [Fact]
        public void ItHasNoVisibleCoplanarFaces()
        {
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            var site = new Site(doc.buildings);
            foreach (var b in doc.buildings)
            {
                var op = SinkFootprint.Build(site, b, 0); op.Solids = null;
                var report = CoplanarCheck.Run(op);
                Assert.True(report.Overlaps.Count == 0, $"{b.name}: {report.Overlaps.Count} overlaps");
            }
        }
    }
}
