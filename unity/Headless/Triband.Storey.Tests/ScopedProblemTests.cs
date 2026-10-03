using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Checking only what an edit touched (<see cref="Problems.Scope"/>, <see cref="Problems.Merge"/>; docs/EDITOR.md
    /// §6.5) gives exactly the list a check of the whole layout does.
    /// </summary>
    public class ScopedProblemTests
    {
        static List<string> Key(IEnumerable<Problem> ps) => ps.Select(p => $"{p.buildingId}|{p.code}|{p.k}|{p.message}|{p.x:0.00}|{p.z:0.00}").OrderBy(x => x, StringComparer.Ordinal).ToList();

        static StoreyDocument Read(StoreyDocument d) => PrototypeJson.Read(PrototypeJson.Write(d)).Document;

        static void SameAsFull(StoreyDocument before, Action<StoreyDocument> edit)
        {
            var site0 = new Site(before.buildings); var last = Problems.Check(site0, new PlayWorld(site0));
            var after = Read(before); edit(after);
            var site = new Site(after.buildings);
            var scope = Problems.Scope(site, site0, Problems.Changed(Problems.Prints(before), Problems.Prints(after)));
            var merged = Problems.Merge(site, last, Problems.Check(site, new PlayWorld(site), scope), scope);
            var full = Problems.Check(site, new PlayWorld(site));
            Assert.Equal(Key(full), Key(merged));
            Assert.True(scope.Count < after.buildings.Count, "a scope, not the whole layout");
        }

        static StoreyDocument Demo() => PrototypeJson.Read(Fixtures.Text("demo.json")).Document;

        static StoreyDocument City() { var d = Demo(); TestCity.Generate(d, 200); return d; }

        [Fact]
        public void RemovingStairs() => SameAsFull(Demo(), d => { var b = d.buildings.First(x => x.interior && x.shafts.Count > 0); b.shafts.Clear(); });

        [Fact]
        public void DeletingABuilding() => SameAsFull(City(), d => Buildings.Delete(d, d.buildings.First(x => x.interior).id));

        [Fact]
        public void MovingABuildingOntoAnother() => SameAsFull(City(), d => { var a = d.buildings[0]; var c = d.buildings[^1]; a.pos = new Vec2(c.pos.x + 1, c.pos.z + 1); });

        [Fact]
        public void AddingABuilding() => SameAsFull(City(), d => Buildings.Add(d, "rect", new Vec2(0, 0)));

        [Fact]
        public void EditingAFacade() => SameAsFull(City(), d => { var b = d.buildings.First(x => x.entrances.Count > 0); b.entrances.Clear(); });

        [Fact]
        public void AnEditFarAwayChecksLittle()
        {
            var d = City(); var site = new Site(d.buildings);
            var b = d.buildings[^1];
            var scope = Problems.Scope(site, site, new[] { b.id });
            Assert.InRange(scope.Count, 1, 12);
        }
    }
}
