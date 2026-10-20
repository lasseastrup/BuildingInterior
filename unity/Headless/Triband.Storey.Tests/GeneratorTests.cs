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
    /// in the same place with the same colour, whatever the triangulation.
    /// </summary>
    public class GeneratorTests
    {
        static readonly Lazy<JsonDocument>[] Census = { new(() => JsonDocument.Parse(Fixtures.Text("census0.json"))), new(() => JsonDocument.Parse(Fixtures.Text("census1.json"))), new(() => JsonDocument.Parse(Fixtures.Text("census2.json"))) };
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

        /// <summary>Every building of both corpora.</summary>
        public static IEnumerable<object[]> Core()
        {
            foreach (var file in new[] { "demo.json", "variants.json" })
            {
                var (_, doc) = SiteOf(file);
                for (int i = 0; i < doc.buildings.Count; i++) yield return new object[] { file, i };
            }
        }

        /// <summary>Every building at every LOD.</summary>
        public static IEnumerable<object[]> Lods()
        {
            foreach (var c in Core()) for (int lod = 0; lod < 3; lod++) yield return new[] { c[0], c[1], lod };
        }

        static JsonElement Expected(string file, int i, int lod = 0)
        {
            var arr = Census[lod].Value.RootElement.GetProperty(file == "demo.json" ? "demo" : "variants");
            return arr[i];
        }

        /// <summary>The LOD2 mesh as a colour-free census: plane, quad kind, bay, window span and the parameter row.</summary>
        static Dictionary<string, Bucket> CensusOf(Lod2Mesh m, ColorResolver colors)
        {
            var B = new Dictionary<string, Bucket>(StringComparer.Ordinal);
            var backing = new bool[m.Tris]; foreach (var (s, e) in m.BackingTris) for (int t = s; t < e; t++) backing[t] = true;
            for (int t = 0; t < m.I.Count; t += 3)
            {
                if (backing[t / 3]) continue;
                int a = m.I[t], bb = m.I[t + 1], c = m.I[t + 2];
                P3 A = m.P[a], Bp = m.P[bb], Cp = m.P[c]; var n = m.N[a] * (1.0 / 127);
                var (d, area) = Plane(A, Bp, Cp, n);
                var row = m.Rows[m.Slot[a]].Texels(colors);
                string key = $"{R(n.x)},{R(n.y)},{R(n.z)}|{R(d)}|{R(m.Fac2[2 * a + 1])}|{R(m.Fac2[2 * a])}|{R(m.Fac[4 * a + 2])},{R(m.Fac[4 * a + 3])}|{string.Join(",", Array.ConvertAll(row, R))}";
                B[key] = B.TryGetValue(key, out var e) ? new Bucket(key, e.Area + area, e.Tris + 1) : new Bucket(key, area, 1);
            }
            return B;
        }

        static Dictionary<string, Bucket> Census2Of(JsonElement e)
        {
            var B = new Dictionary<string, Bucket>(StringComparer.Ordinal);
            foreach (var x in e.GetProperty("buckets").EnumerateArray())
            {
                var n = x.GetProperty("n"); var span = x.GetProperty("span"); var pr = x.GetProperty("params");
                var row = new List<string>(); foreach (var v in pr.EnumerateArray()) row.Add(R(v.GetDouble()));
                string key = $"{R(n[0].GetDouble())},{R(n[1].GetDouble())},{R(n[2].GetDouble())}|{R(x.GetProperty("d").GetDouble())}|{R(x.GetProperty("kind").GetDouble())}|{R(x.GetProperty("bay").GetDouble())}|{R(span[0].GetDouble())},{R(span[1].GetDouble())}|{string.Join(",", row)}";
                double area = x.GetProperty("area").GetDouble(); int tris = x.GetProperty("tris").GetInt32();
                B[key] = B.TryGetValue(key, out var old) ? new Bucket(key, old.Area + area, old.Tris + tris) : new Bucket(key, area, tris);
            }
            return B;
        }

        record Bucket(string Key, double Area, int Tris);

        /// <summary>
        /// The plane offset from the triangle's own normal (oriented like the stored one) and its area. The
        /// stored normal is used for identity only: LOD2 stores Int8 normals, and with a normal that is not
        /// quite perpendicular the offset would depend on which vertex the triangulation put first.
        /// </summary>
        static (double d, double area) Plane(P3 A, P3 B, P3 C, P3 stored)
        {
            var cr = P3.Cross(B - A, C - A); double cl = cr.Length; if (cl == 0) cl = 1;
            var gn = cr * (1 / cl); if (P3.Dot(gn, stored) < 0) gn = gn * -1;
            return (P3.Dot(gn, A), cl / 2);
        }

        /// <summary>Rounded to what the exporter wrote; a rounded −0 prints as "-0.000" in .NET and "0.000" in JavaScript.</summary>
        static string R(double v) { v = Math.Round(v, 3); return (v == 0 ? 0.0 : v).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture); }

        static string Key(P3 n, double d, Rgb c) =>
            $"{R(n.x)},{R(n.y)},{R(n.z)}|{R(d)}|{R(c.r)},{R(c.g)},{R(c.b)}";

        /// <summary>
        /// The faces only Unity draws (<see cref="MeshBuilder.BackingTris"/>): the backs of surfaces the prototype draws
        /// two-sided, which no census of its holds.
        /// </summary>
        static bool[] Backing(MeshBuilder gb)
        {
            var o = new bool[gb.Tris];
            foreach (var (s, e) in gb.BackingTris) for (int t = s; t < e; t++) o[t] = true;
            return o;
        }

        /// <summary>The prototype's triangles: all but the faces only Unity draws.</summary>
        static int PrototypeTris(MeshBuilder gb) => gb.Tris - gb.BackingTris.Sum(r => r.end - r.start);

        /// <summary>
        /// What the prototype draws at each street door and Storey's plain doors leave out (docs/EDITOR.md §6.7): a surround
        /// of two jambs and a canopy in LOD0, the canopy in LOD1. Drawn here as the prototype draws them, to go with the
        /// port's faces, so every other face is still judged against the prototype's.
        /// </summary>
        static MeshBuilder PrototypeDoors(Site site, BuildingData b, int lod)
        {
            var m = MeshBuilder.Make(); double T = Dim.T_EXT; var L0 = new Lod0.Shared(site, b);
            for (int k = 0; k < b.floors.Count; k++)
            {
                var C = L0.At(k).C; double y = Derived.FloorBase(b, k);
                for (int i = 0; i < Derived.OutlineAt(b, k).Count; i++)
                {
                    var f = Details.EdgeFrameAt(b, k, i); var F = f.e.F;
                    foreach (var o in Facade.Ops(site, b, k, i, f.e.L, f.uS, f.uE))
                    {
                        if (!o.door || o.bare) continue;
                        if (lod == 0)
                        {
                            m.OBox(F, o.u0 - 0.08, o.u0, y, y + o.y1 + 0.08, T, T + 0.06, C.trim, C.trim, Skip.In | Skip.Bot);
                            m.OBox(F, o.u1, o.u1 + 0.08, y, y + o.y1 + 0.08, T, T + 0.06, C.trim, C.trim, Skip.In | Skip.Bot);
                        }
                        m.OBox(F, o.u0 - 0.35, o.u1 + 0.35, y + o.y1 + 0.1, y + o.y1 + 0.24, T, T + 1.1, C.trim, C.trim, Skip.In);
                    }
                }
            }
            return m;
        }

        /// <summary>The port's census with the prototype's door dressing (<see cref="PrototypeDoors"/>) added.</summary>
        static Dictionary<string, Bucket> WithPrototypeDoors(Dictionary<string, Bucket> got, Site site, BuildingData b, int lod, ColorResolver colors)
        {
            var doors = PrototypeDoors(site, b, lod);
            foreach (var (key, x) in CensusOf(doors, colors))
                got[key] = got.TryGetValue(key, out var e) ? new Bucket(key, e.Area + x.Area, e.Tris + x.Tris) : x;
            doors.Recycle();
            return got;
        }

        static Dictionary<string, Bucket> CensusOf(MeshBuilder gb, ColorResolver colors)
        {
            var B = new Dictionary<string, Bucket>(StringComparer.Ordinal); var backing = Backing(gb);
            for (int t = 0; t < gb.I.Count; t += 3)
            {
                if (backing[t / 3]) continue;
                int a = gb.I[t], bb = gb.I[t + 1], c = gb.I[t + 2];
                P3 A = gb.P[a], Bp = gb.P[bb], Cp = gb.P[c]; var n = gb.N[a]; var col = colors.Resolve(gb.C[a]);
                var (d, area) = Plane(A, Bp, Cp, n);
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

        [Theory, MemberData(nameof(Lods))]
        public void EveryFaceIsWhereThePrototypePutIt(string file, int i, int lod)
        {
            var (site, doc) = SiteOf(file);
            var b = doc.buildings[i];
            var e = Expected(file, i, lod);
            Assert.Equal(b.id, e.GetProperty("id").GetString());

            var colors = new ColorResolver(site);
            var got = lod == 0 ? CensusOf(Lod0.Build(site, b).Op, colors) : lod == 1 ? CensusOf(Lod1.Build(site, b, recessed: true), colors) : CensusOf(Lod2.Build(site, b), colors);
            if (lod < 2) got = WithPrototypeDoors(got, site, b, lod, colors);
            var want = lod < 2 ? CensusOf(e) : Census2Of(e);
            var tol = Census[lod].Value.RootElement.GetProperty("tolerance");
            double abs = tol.GetProperty("area").GetDouble(), rel = tol.GetProperty("areaRelative").GetDouble();

            var diffs = new List<string>();
            foreach (var key in want.Keys.Union(got.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                want.TryGetValue(key, out var w); got.TryGetValue(key, out var g);
                double wa = w?.Area ?? 0, ga = g?.Area ?? 0;
                if (Math.Abs(wa - ga) > abs + rel * Math.Max(wa, ga))
                    diffs.Add($"{key}: prototype {wa:0.####} m² in {w?.Tris ?? 0} tris, port {ga:0.####} m² in {g?.Tris ?? 0} tris");
            }
            Assert.True(diffs.Count == 0, $"{b.name} ({b.id}) LOD{lod}: {diffs.Count} of {want.Count} buckets differ:\n  " + string.Join("\n  ", diffs.Take(25)));
        }

        [Theory, MemberData(nameof(Lods))]
        public void TriangleCountIsWithinTenPercent(string file, int i, int lod)
        {
            var (site, doc) = SiteOf(file);
            var b = doc.buildings[i];
            int want = Expected(file, i, lod).GetProperty("tris").GetInt32();
            int got = lod == 0 ? PrototypeTris(Lod0.Build(site, b).Op) : lod == 1 ? PrototypeTris(Lod1.Build(site, b, recessed: true)) : Lod2.Build(site, b) is var m2 ? m2.Tris - m2.BackingTris.Sum(r => r.end - r.start) : 0;
            if (lod < 2) { var doors = PrototypeDoors(site, b, lod); got += PrototypeTris(doors); doors.Recycle(); }
            Assert.InRange(got, want * 0.9, want * 1.1);
        }

        [Theory, MemberData(nameof(Lods))]
        public void NoVisibleCoplanarFaces(string file, int i, int lod)
        {
            var (site, doc) = SiteOf(file);
            if (lod == 2) return;   // the massing mesh carries no solids; its overlaps are the checker's job in the editor
            var mesh = lod == 0 ? Lod0.Build(site, doc.buildings[i], solids: true).Op : Lod1.Build(site, doc.buildings[i], solids: true);
            var report = CoplanarCheck.Run(mesh);
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
