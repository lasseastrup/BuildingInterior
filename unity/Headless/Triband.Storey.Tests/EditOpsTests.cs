using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Edit;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The floor, outline and setback operations (docs/EDITOR.md slice 6.1) against the prototype's own:
    /// unity/Fixtures/ops.json holds every case prototype/tools/export-ops.mjs ran, with what the prototype's
    /// operation returned and the building afterwards. The C# operation must return the same and leave the
    /// same building (compared as the layout JSON Storey writes).
    /// </summary>
    public class EditOpsTests
    {
        sealed class Corpus
        {
            public readonly StoreyDocument doc;
            public readonly string[] buildings;
            public Corpus(string name)
            {
                doc = PrototypeJson.Read(Fixtures.Text(name + ".json")).Document;
                buildings = doc.buildings.Select(PrototypeJson.Write).ToArray();
            }
        }

        static readonly Lazy<(Dictionary<string, Corpus> corpora, JsonElement[] cases)> Data = new(() =>
        {
            var corpora = new Dictionary<string, Corpus> { ["demo"] = new Corpus("demo"), ["variants"] = new Corpus("variants") };
            var cases = JsonDocument.Parse(Fixtures.Text("ops.json")).RootElement.GetProperty("cases").EnumerateArray().ToArray();
            return (corpora, cases);
        });

        public static IEnumerable<object[]> Ops() =>
            Data.Value.cases.Select(c => c.GetProperty("op").GetString()!).Distinct().Select(op => new object[] { op });

        [Fact]
        public void EveryOperationHasCases()
        {
            var ops = Ops().Select(o => (string)o[0]).ToHashSet();
            foreach (var op in new[] { "setFloorCount", "addFloorTop", "deleteFloor", "insertAbove", "copyLayoutUp", "insertVertex", "insertVertexSimplify",
                         "removeVertex", "pushEdge", "insetPoly", "outlineSnap", "addSetback", "removeSetback", "moveSetback", "applyShape", "snapMove" })
                Assert.Contains(op, ops);
        }

        [Theory, MemberData(nameof(Ops))]
        public void MatchesThePrototype(string op)
        {
            var (corpora, cases) = Data.Value;
            var failures = new List<string>(); int n = 0;
            foreach (var c in cases.Where(c => c.GetProperty("op").GetString() == op))
            {
                n++;
                var corpus = corpora[c.GetProperty("corpus").GetString()!];
                int i = c.GetProperty("building").GetInt32();
                var b = PrototypeJson.ReadBuilding(corpus.buildings[i]);
                var neighbours = corpus.doc.buildings.Select((x, j) => j == i ? b : x).ToList();
                var args = c.GetProperty("args").EnumerateArray().ToArray();
                string? why = Check(op, b, neighbours, args, c.GetProperty("ret"));
                string want = PrototypeJson.Write(PrototypeJson.ReadBuilding(c.GetProperty("after").GetRawText())), got = PrototypeJson.Write(b);
                if (why == null && want != got) why = "building differs:\n  want " + Diff(want, got) + "\n  got  " + Diff(got, want);
                if (why != null && failures.Count < 8) failures.Add($"{c.GetProperty("corpus").GetString()}[{i}] {b.name} {op}({string.Join(", ", args.Select(a => a.GetRawText()))}): {why}");
                else if (why != null) failures.Add("");
            }
            Assert.True(n > 0);
            Assert.True(failures.Count == 0, $"{failures.Count} of {n} cases differ:\n" + string.Join("\n", failures.Where(f => f != "")));
        }

        static string? Check(string op, BuildingData b, List<BuildingData> neighbours, JsonElement[] a, JsonElement ret)
        {
            int I(int j) => a[j].GetInt32();
            double D(int j) => a[j].GetDouble();
            switch (op)
            {
                case "setFloorCount": Floors.SetCount(b, I(0)); return null;
                case "addFloorTop": Floors.AddTop(b); return null;
                case "deleteFloor": return Same(ret.GetBoolean(), Floors.Delete(b, I(0)));
                case "insertAbove": Floors.InsertAbove(b, I(0)); return null;
                case "copyLayoutUp": Floors.CopyLayoutUp(b, I(0)); return null;
                case "insertVertex": return Same(ret.GetInt32(), Outlines.InsertVertex(b, I(0), I(1)));
                case "insertVertexSimplify": Outlines.InsertVertex(b, I(0), I(1)); return Same(ret.GetBoolean(), Outlines.Simplify(b, I(1)));
                case "removeVertex": return Same(ret.GetBoolean(), Outlines.RemoveVertex(b, I(0), I(1)));
                case "pushEdge": { var fp = Outlines.PushEdge(Tiers.Outline(b, I(1)), I(0), D(2)); return Outline(ret, fp, Outlines.Issue(b, I(1), fp)); }
                case "insetPoly": { var fp = Outlines.Inset(Tiers.Outline(b, I(0)), D(1)); return Outline(ret, fp, Outlines.Issue(b, I(0), fp)); }
                case "outlineSnap": { int k0 = I(0); return Point(ret, Outlines.Snap(b, k0, Tiers.Outline(b, k0), I(1), D(2), D(3), neighbours)); }
                case "snapMove": return Point(ret, Outlines.SnapMove(b, D(0), D(1), neighbours));
                case "addSetback": return Same(ret.GetInt32(), Setbacks.Add(b, I(0)));
                case "removeSetback": Setbacks.Remove(b, I(0)); return null;
                case "moveSetback": return Same(ret.GetBoolean(), Setbacks.Move(b, I(0), I(1)));
                case "applyShape": Setbacks.ApplyPreset(b, a[0].GetString()!); return null;
                default: return "no C# operation for " + op;
            }
        }

        static string? Same<T>(T want, T got) => EqualityComparer<T>.Default.Equals(want, got) ? null : $"returned {got}, the prototype {want}";

        static string? Point(JsonElement want, Vec2 got) =>
            Near(want[0].GetDouble(), got.x) && Near(want[1].GetDouble(), got.z) ? null : $"snapped to {got}, the prototype to ({want[0]}, {want[1]})";

        static string? Outline(JsonElement want, List<Vec2> fp, OutlineIssue issue)
        {
            var w = want.GetProperty("fp").EnumerateArray().ToArray();
            if (w.Length != fp.Count || w.Where((p, j) => !Near(p[0].GetDouble(), fp[j].x) || !Near(p[1].GetDouble(), fp[j].z)).Any())
                return $"outline {string.Join(" ", fp)}, the prototype {want.GetProperty("fp").GetRawText()}";
            var wi = want.GetProperty("issue"); string wantIssue = wi.ValueKind == JsonValueKind.Null ? "None" : wi.GetString()!;
            return string.Equals(wantIssue, issue.ToString(), StringComparison.OrdinalIgnoreCase) ? null : $"issue {issue}, the prototype {wantIssue}";
        }

        static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-9;

        /// <summary>The text around the first difference.</summary>
        static string Diff(string a, string b)
        {
            int j = 0; while (j < a.Length && j < b.Length && a[j] == b[j]) j++;
            int s = Math.Max(0, j - 60); return a.Substring(s, Math.Min(160, a.Length - s));
        }
    }
}
