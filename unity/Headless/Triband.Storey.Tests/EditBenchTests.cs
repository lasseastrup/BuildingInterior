using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>What one edit of one building costs, engine-free, on the 3,000-building test city (docs/CITY.md §4).</summary>
    [Collection(TimingCollection.Name)]
    public class EditBenchTests
    {
        readonly ITestOutputHelper log;
        public EditBenchTests(ITestOutputHelper log) { this.log = log; }

        sealed class Palette : IColorPalette
        {
            readonly Dictionary<string, int> ix = new Dictionary<string, int>(StringComparer.Ordinal);
            public StyleDefaults Defaults { get; } = new StyleDefaults();
            public event Action? Invalidated { add { } remove { } }
            public int IndexOf(string c) { if (!ix.TryGetValue(c, out int i)) ix[c] = i = ix.Count; return i; }
            public int RegisterRemap(IReadOnlyList<string> original, IReadOnlyList<string> overwrite) => 0;
        }

        sealed class Sink : IColorRowSink
        {
            int next;
            public int AllocColorRow() => next++;
            public void ReleaseColorRow(int row) { }
            public void WriteColorRow(int row, int[] paletteIndices, int remapRow) { }
        }

        [Fact]
        public void OneEditInTheCity()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 3000);
            var session = new EditSession(PrototypeJson.Write(d));
            var site = new Site(session.Document.buildings);
            var book = new ColorRowBook(new ColorResolver(site), new Palette(), new Sink());
            for (int i = 0; i < site.Buildings.Count; i++) { var b = site.Buildings[i]; for (int k = 0; k < b.floors.Count; k++) if (k == 0 || b.floors[k].HasShape) book.RowOf(new StyleRef(i, k)); }
            var palette = new List<PaletteColor> { new PaletteColor("00000000000000000000000000000001", "Teal", 0.2f, 0.4f, 0.4f) };

            var sw = Stopwatch.StartNew();
            session.Document.buildings[5].floorHeight += 0.1;
            double edit = sw.Elapsed.TotalMilliseconds; sw.Restart();
            PaletteConform.Apply(session.Document, palette); session.Commit();   // the first conform changes every building: settle it
            session.Document.buildings[5].floorHeight += 0.1; sw.Restart();
            PaletteConform.Apply(session.Document, palette);
            double conform = sw.Elapsed.TotalMilliseconds; sw.Restart();
            var change = session.Commit();
            double commit = sw.Elapsed.TotalMilliseconds; sw.Restart();
            var site2 = new Site(session.Document.buildings);
            double newSite = sw.Elapsed.TotalMilliseconds; sw.Restart();
            book.Retarget(new ColorResolver(site2));
            double retarget = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); foreach (var b in session.Document.buildings) PrototypeJson.Write(b); double each = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); PrototypeJson.Write(session.Document); double whole = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); int links = 0; foreach (var b in session.Document.buildings) foreach (var a in session.Document.buildings) if (a.bridges.Any(x => x.to == b.id)) links++; double linked = sw.Elapsed.TotalMilliseconds;
            var text0 = session.Text; sw.Restart(); PrototypeJson.Read(text0); log.WriteLine($"reading the whole layout: {sw.Elapsed.TotalMilliseconds:0} ms");
            sw.Restart(); session.Load(PrototypeJson.Write(d)); double load = sw.Elapsed.TotalMilliseconds;
            log.WriteLine($"a drag step's return to its start (read the whole layout): {load:0} ms");
            log.WriteLine($"commit parts: every building's text {each:0} ms, the whole text {whole:0} ms, bridge links {linked:0} ms");
            var bb = session.Document.buildings[9]; string bj = PrototypeJson.Write(bb); bb.floorHeight += 0.2; session.Commit();
            sw.Restart(); session.Restore(bb.id, bj); bb = session.Document.buildings[9]; bb.floorHeight += 0.1; session.Commit(); double step = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); session.Document.buildings[9].floorHeight += 0.1; session.Discard(); double discard = sw.Elapsed.TotalMilliseconds;
            log.WriteLine($"a drag step (put the building back, edit, commit): {step:0} ms; a refused edit's discard: {discard:0} ms");
            log.WriteLine($"one edit, {d.buildings.Count} buildings: conform {conform:0} ms, commit {commit:0} ms (rebuild {change.rebuild.Count}), new Site {newSite:0} ms, colour rows rewritten {retarget:0} ms ({book.Rows.Count} rows); text {session.Text.Length / 1e6:0.0} MB");
        }
    }
}
