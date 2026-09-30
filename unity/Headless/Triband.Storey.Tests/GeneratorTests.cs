using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The LOD0 generator against the prototype's face census (<c>census.json</c>): the same surface
    /// in the same place with the same colour, whatever the triangulation. Workstream 2 covers the
    /// generator core, so buildings needing a pitched roof or a roofed terrace (workstream 3) are
    /// left to that workstream; facade details are suppressed in the census and not generated here.
    /// </summary>
    public class GeneratorTests
    {
        static readonly Lazy<JsonDocument> Census = new(() => JsonDocument.Parse(Fixtures.Text("census.json")));
        static readonly Dictionary<string, (Site site, StoreyDocument doc)> Sites = new();

        static (Site site, StoreyDocument doc) SiteOf(string file)
        {
            lock (Sites)
            {
                if (!Sites.TryGetValue(file, out var s))
                {
                    var doc = PrototypeJson.Read(Fixtures.Text(file)).Document;
                    Sites[file] = s = (new Site(doc.buildings), doc);
                }
                return s;
            }
        }

        /// <summary>Buildings the workstream-2 generator must reproduce: flat roofs, no roofed terraces.</summary>
        public static IEnumerable<object[]> Core()
        {
            foreach (var file in new[] { "demo.json", "variants.json" })
            {
                var (_, doc) = SiteOf(file);
                for (int i = 0; i < doc.buildings.Count; i++)
                {
                    var b = doc.buildings[i];
                    if (Roofs.IsPitched(b) || b.floors.Any(f => f.terraceRoof != null)) continue;
                    yield return new object[] { file, i };
                }
            }
        }

        static JsonElement Expected(string file, int i)
        {
            var arr = Census.Value.RootElement.GetProperty(file == "demo.json" ? "demo" : "variants");
            return arr[i];
        }

        record Bucket(string Key, double Area, int Tris);

        /// <summary>Rounded to what the exporter wrote; a rounded −0 prints as "-0.000" in .NET and "0.000" in JavaScript.</summary>
        static string R(double v) { v = Math.Round(v, 3); return (v == 0 ? 0.0 : v).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture); }

        static string Key(P3 n, double d, Rgb c) =>
            $"{R(n.x)},{R(n.y)},{R(n.z)}|{R(d)}|{R(c.r)},{R(c.g)},{R(c.b)}";

        static Dictionary<string, Bucket> CensusOf(MeshBuilder gb)
        {
            var B = new Dictionary<string, Bucket>(StringComparer.Ordinal);
            for (int t = 0; t < gb.I.Count; t += 3)
            {
                int a = gb.I[t], bb = gb.I[t + 1], c = gb.I[t + 2];
                P3 A = gb.P[a], Bp = gb.P[bb], Cp = gb.P[c]; var n = gb.N[a]; var col = gb.C[a];
                double d = P3.Dot(n, A);
                double area = P3.Cross(Bp - A, Cp - A).Length / 2;
                string key = Key(n, d, col);
                B[key] = B.TryGetValue(key, out var e) ? new Bucket(key, e.Area + area, e.Tris + 1) : new Bucket(key, area, 1);
            }
            return B;
        }

        static Dictionary<string, Bucket> CensusOf(JsonElement e)
        {
            var B = new Dictionary<string, Bucket>(StringComparer.Ordinal);
            foreach (var x in e.GetProperty("buckets").EnumerateArray())
            {
                var n = x.GetProperty("n"); var c = x.GetProperty("c");
                string key = Key(new P3(n[0].GetDouble(), n[1].GetDouble(), n[2].GetDouble()), x.GetProperty("d").GetDouble(), new Rgb(c[0].GetDouble(), c[1].GetDouble(), c[2].GetDouble()));
                double area = x.GetProperty("area").GetDouble(); int tris = x.GetProperty("tris").GetInt32();
                B[key] = B.TryGetValue(key, out var old) ? new Bucket(key, old.Area + area, old.Tris + tris) : new Bucket(key, area, tris);
            }
            return B;
        }

        [Theory, MemberData(nameof(Core))]
        public void EveryFaceIsWhereThePrototypePutIt(string file, int i)
        {
            var (site, doc) = SiteOf(file);
            var b = doc.buildings[i];
            var e = Expected(file, i);
            Assert.Equal(b.id, e.GetProperty("id").GetString());

            var got = CensusOf(Lod0.Build(site, b).Op);
            var want = CensusOf(e);
            var tol = Census.Value.RootElement.GetProperty("tolerance");
            double abs = tol.GetProperty("area").GetDouble(), rel = tol.GetProperty("areaRelative").GetDouble();

            var diffs = new List<string>();
            foreach (var key in want.Keys.Union(got.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                want.TryGetValue(key, out var w); got.TryGetValue(key, out var g);
                double wa = w?.Area ?? 0, ga = g?.Area ?? 0;
                if (Math.Abs(wa - ga) > abs + rel * Math.Max(wa, ga))
                    diffs.Add($"{key}: prototype {wa:0.####} m² in {w?.Tris ?? 0} tris, port {ga:0.####} m² in {g?.Tris ?? 0} tris");
            }
            Assert.True(diffs.Count == 0, $"{b.name} ({b.id}): {diffs.Count} of {want.Count} plane/colour buckets differ:\n  " + string.Join("\n  ", diffs.Take(25)));
        }

        [Theory, MemberData(nameof(Core))]
        public void TriangleCountIsWithinTenPercent(string file, int i)
        {
            var (site, doc) = SiteOf(file);
            var b = doc.buildings[i];
            int want = Expected(file, i).GetProperty("tris").GetInt32();
            int got = Lod0.Build(site, b).Op.Tris;
            Assert.InRange(got, want * 0.9, want * 1.1);
        }

        [Theory, MemberData(nameof(Core))]
        public void NoVisibleCoplanarFaces(string file, int i)
        {
            var (site, doc) = SiteOf(file);
            var r = Lod0.Build(site, doc.buildings[i], solids: true);
            var report = CoplanarCheck.Run(r.Op);
            Assert.True(report.Overlaps.Count == 0, $"{doc.buildings[i].name}: {report.Overlaps.Count} visible coplanar overlaps ({report.Buried} buried):\n  " + string.Join("\n  ", report.Overlaps.Take(10)));
        }

        [Fact]
        public void CollisionSegmentsCoverEveryStorey()
        {
            var (site, doc) = SiteOf("demo.json");
            var linden = doc.buildings.Single(b => b.name == "Linden Court");
            var r = Lod0.Build(site, linden);
            Assert.Equal(linden.floors.Count + 1, r.Segs.Count);
            Assert.All(r.Segs.Take(linden.floors.Count), s => Assert.NotEmpty(s));
            Assert.NotEmpty(r.Glass.I);
        }
    }
}
