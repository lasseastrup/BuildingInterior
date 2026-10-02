using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The walk model (docs/PLAY.md, slice 5.1) against the prototype's answers in <c>play.json</c>: LOD0's collision
    /// segments, the surface under a point, collision, and which building and storey a point is in.
    /// </summary>
    public class PlayWorldTests
    {
        internal static readonly Lazy<JsonDocument> Play = new(() => JsonDocument.Parse(Fixtures.Text("play.json")));
        static readonly Dictionary<string, (StoreyDocument doc, PlayWorld world)> Worlds = new();

        internal static (StoreyDocument doc, PlayWorld world) World(string corpus)
        {
            lock (Worlds)
            {
                if (!Worlds.TryGetValue(corpus, out var w))
                {
                    var doc = PrototypeJson.Read(Fixtures.Text(corpus + ".json")).Document;
                    Worlds[corpus] = w = (doc, new PlayWorld(new Site(doc.buildings)));
                }
                return w;
            }
        }

        static JsonElement Corpus(string c) => Play.Value.RootElement.GetProperty("corpora").GetProperty(c);
        static double D(JsonElement e) => e.GetDouble();

        public static IEnumerable<object[]> Corpora() { yield return new object[] { "demo" }; yield return new object[] { "variants" }; }

        [Theory, MemberData(nameof(Corpora))]
        public void CollisionSegmentsMatch(string corpus)
        {
            var (doc, world) = World(corpus);
            var bad = new List<string>();
            foreach (var e in Corpus(corpus).GetProperty("segs").EnumerateArray())
            {
                var b = doc.buildings[e.GetProperty("building").GetInt32()];
                var want = e.GetProperty("segs").EnumerateArray().Select(st => st.EnumerateArray().Select(s => s.EnumerateArray().Select(D).ToArray()).ToList()).ToList();
                var got = world.Lod0Of(b).Segs;
                if (got.Count != want.Count) { bad.Add($"{b.name}: {got.Count} storeys of segments, the prototype {want.Count}"); continue; }
                for (int k = 0; k < want.Count; k++)
                {
                    string Key(double[] s) => string.Join(",", s.Select(v => (Math.Round(v, 5) + 0.0).ToString("F5")));   // + 0.0: -0 and 0 alike
                    var w = want[k].Select(Key).ToList(); var g = got[k].Select(s => Key(new[] { s.ax, s.az, s.bx, s.bz, s.r })).ToList();
                    if (!w.SequenceEqual(g)) bad.Add($"{b.name} storey {k}: {g.Count} segments, the prototype {w.Count}; first difference: {w.Except(g).FirstOrDefault() ?? "(order)"} / {g.Except(w).FirstOrDefault() ?? "(order)"}");
                }
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad.Take(12)));
        }

        [Theory, MemberData(nameof(Corpora))]
        public void SurfacesMatch(string corpus)
        {
            var (_, world) = World(corpus);
            var bad = new List<string>(); int n = 0;
            foreach (var c in Corpus(corpus).GetProperty("surface").EnumerateArray())
            {
                var a = c.EnumerateArray().Select(D).ToArray(); n++;
                double got = world.SurfaceAt(a[0], a[1], a[2]);
                if (Math.Abs(got - a[3]) > 1e-6) bad.Add($"surfaceAt({a[0]}, {a[1]}, {a[2]}) = {got}, the prototype {a[3]}");
            }
            Assert.True(n > 0);
            Assert.True(bad.Count == 0, $"{bad.Count} of {n}:\n" + string.Join("\n", bad.Take(12)));
        }

        [Theory, MemberData(nameof(Corpora))]
        public void CollisionMatches(string corpus)
        {
            var (_, world) = World(corpus);
            var bad = new List<string>(); int n = 0;
            foreach (var c in Corpus(corpus).GetProperty("collide").EnumerateArray())
            {
                var a = c.EnumerateArray().Select(D).ToArray(); n++;
                var (x, z) = world.Collide(a[0], a[1], a[2]);
                if (Math.Abs(x - a[3]) > 1e-6 || Math.Abs(z - a[4]) > 1e-6) bad.Add($"collide({a[0]}, {a[1]}, {a[2]}) = ({x}, {z}), the prototype ({a[3]}, {a[4]})");
            }
            Assert.True(n > 0);
            Assert.True(bad.Count == 0, $"{bad.Count} of {n}:\n" + string.Join("\n", bad.Take(12)));
        }

        [Theory, MemberData(nameof(Corpora))]
        public void LocationsMatch(string corpus)
        {
            var (_, world) = World(corpus);
            var bad = new List<string>(); int n = 0;
            foreach (var c in Corpus(corpus).GetProperty("loc").EnumerateArray())
            {
                var a = c.EnumerateArray().ToArray(); n++;
                var got = world.Locate(D(a[0]), D(a[1]), D(a[2]));
                string? wantId = a[3].ValueKind == JsonValueKind.Null ? null : a[3].GetString();
                int? wantFloor = a[4].ValueKind == JsonValueKind.Null ? null : a[4].GetInt32();
                if (got?.b.id != wantId || got?.floor != wantFloor) bad.Add($"at ({D(a[0])}, {D(a[1])}, {D(a[2])}): {got?.b.id}/{got?.floor}, the prototype {wantId}/{wantFloor}");
            }
            Assert.True(bad.Count == 0, $"{bad.Count} of {n}:\n" + string.Join("\n", bad.Take(12)));
        }
    }
}
