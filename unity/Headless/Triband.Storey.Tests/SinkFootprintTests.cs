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
