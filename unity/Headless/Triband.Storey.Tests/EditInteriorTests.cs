using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Stairs, lifts, the interior wall graph, doors and erase (docs/EDITOR.md slice 6.2) against the prototype:
    /// unity/Fixtures/ops-interior.json holds cases of one or more steps, as the tools make them, with what the last
    /// step returned and the building afterwards (none for queries, "unchanged" when it is as it started).
    /// </summary>
    public class EditInteriorTests
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
            var cases = JsonDocument.Parse(Fixtures.Text("ops-interior.json")).RootElement.GetProperty("cases").EnumerateArray().ToArray();
            return (corpora, cases);
        });

        public static IEnumerable<object[]> Names() =>
            Data.Value.cases.Select(c => c.GetProperty("name").GetString()!).Distinct().Select(n => new object[] { n });

        [Theory, MemberData(nameof(Names))]
        public void MatchesThePrototype(string name)
        {
            var (corpora, cases) = Data.Value;
            var failures = new List<string>(); int n = 0;
            foreach (var c in cases.Where(c => c.GetProperty("name").GetString() == name))
            {
                n++;
                var corpus = corpora[c.GetProperty("corpus").GetString()!];
                int i = c.GetProperty("building").GetInt32();
                var b = PrototypeJson.ReadBuilding(corpus.buildings[i]);
                var site = new Site(corpus.doc.buildings.Select((x, j) => j == i ? b : x).ToList());
                object? ret = null; string? why = null;
                foreach (var s in c.GetProperty("steps").EnumerateArray())
                {
                    try { ret = Step(site, b, s.GetProperty("op").GetString()!, s.GetProperty("args").EnumerateArray().ToArray()); }
                    catch (Exception e) { why = $"{s.GetProperty("op").GetString()} threw {e.GetType().Name}: {e.Message}"; break; }
                }
                if (why == null) why = Compare(c.GetProperty("ret"), ret);
                var after = c.GetProperty("after");
                if (why == null && after.ValueKind != JsonValueKind.Null)
                {
                    string want = after.ValueKind == JsonValueKind.String ? corpus.buildings[i] : PrototypeJson.Write(PrototypeJson.ReadBuilding(after.GetRawText())), got = PrototypeJson.Write(b);
                    if (want != got) why = "building differs:\n  want " + Diff(want, got) + "\n  got  " + Diff(got, want);
                }
                if (why != null)
                {
                    if (failures.Count < 8) failures.Add($"{c.GetProperty("corpus").GetString()}[{i}] {b.name} {c.GetProperty("steps").GetRawText()}: {why}");
                    else failures.Add("");
                }
            }
            Assert.True(n > 0);
            Assert.True(failures.Count == 0, $"{failures.Count} of {n} cases differ:\n" + string.Join("\n", failures.Where(f => f != "")));
        }

        static Vec2 V(JsonElement e) => new Vec2(e[0].GetDouble(), e[1].GetDouble());
        static CoreType Type(JsonElement e) => e.GetString() == "lift" ? CoreType.Lift : CoreType.Stairs;

        static SnapOptions Options(JsonElement o)
        {
            var r = new SnapOptions();
            if (o.TryGetProperty("from", out var f)) r.from = V(f);
            if (o.TryGetProperty("anchors", out var a)) r.anchors = a.EnumerateArray().Select(V).ToList();
            if (o.TryGetProperty("ex", out var ex)) foreach (var s in ex.EnumerateArray()) { var p = s.GetString()!.Split(':'); r.ignoreEnds.Add((int.Parse(p[0], CultureInfo.InvariantCulture), p[1] == "b")); }
            if (o.TryGetProperty("exW", out var ew)) foreach (var w in ew.EnumerateArray()) r.ignoreWalls.Add(w.GetInt32());
            if (o.TryGetProperty("free", out var fr)) r.free = fr.GetBoolean();
            return r;
        }

        /// <summary>One step, its result in the shape the exporter records it.</summary>
        static object? Step(Site site, BuildingData b, string op, JsonElement[] a)
        {
            int I(int j) => a[j].GetInt32();
            switch (op)
            {
                case "snapPoint": { var q = Walls.Snap(b, I(0), V(a[1]), Options(a[2])); return new Dictionary<string, object?> { ["x"] = q.x, ["z"] = q.z, ["kind"] = q.kind.ToString().ToLowerInvariant(), ["wi"] = q.wall, ["guides"] = q.guides.Select(g => new[] { g.x, g.z }).ToList() }; }
                case "placementAt": { var (it, ok) = Shafts.Placement(b, I(0), V(a[1]), Type(a[2]), a[3].GetDouble()); return new Dictionary<string, object?> { ["x"] = it.x, ["z"] = it.z, ["rot"] = it.rot, ["ok"] = ok }; }
                case "doorAt": return Door(Walls.DoorAt(site, b, I(0), V(a[1]), a[2].GetBoolean()));
                case "hoverTarget":
                {
                    var t = Walls.HoverTarget(b, I(0), V(a[1])); if (t == null) return null; var v = t.Value;
                    return v.kind switch
                    {
                        TargetKind.Shaft => new Dictionary<string, object?> { ["kind"] = "shaft", ["si"] = v.index },
                        TargetKind.Wall => new Dictionary<string, object?> { ["kind"] = "wall", ["wi"] = v.index },
                        TargetKind.Door => new Dictionary<string, object?> { ["kind"] = "door", ["wi"] = v.index, ["di"] = v.door },
                        _ => new Dictionary<string, object?> { ["kind"] = "entrance", ["ei"] = v.index },
                    };
                }
                case "wallAngle": { var p = V(a[1]); return Shafts.WallAngle(Derived.OutlineAt(b, I(0)), p.x, p.z); }
                case "snapCore": { var p = V(a[2]); double rot = a[3].GetDouble(); var q = Shafts.Snap(Derived.OutlineAt(b, I(0)), new CoreData { type = Type(a[1]), x = p.x, z = p.z, rot = rot }, p.x, p.z, rot); return new[] { q.x, q.z }; }
                case "itemsOverlap": return Shafts.Overlap(b.shafts[I(0)], b.shafts[I(1)], a[2].GetDouble());
                case "addWall": Walls.Add(b, I(0), V(a[1]), V(a[2])); return null;
                case "removeJoint": return Walls.RemoveJoint(b, I(0), a[1].GetString()!);
                case "dragJoint": return Walls.MoveJoint(b, I(0), a[1].GetString()!, V(a[2]), a[3].GetBoolean());
                case "splitDrag": Walls.SplitAndDrag(b, I(0), I(1), V(a[2]), a[3].GetBoolean()); return null;
                case "toggleDoor": return Walls.ToggleDoor(site, b, I(0), V(a[1]));
                case "erase": { var t = Walls.HoverTarget(b, I(0), V(a[1])); if (t == null) return false; Walls.Erase(b, I(0), t.Value); return true; }
                case "placeCore": return Shafts.Place(b, I(0), V(a[1]), Type(a[2]), a[3].GetDouble(), a[4].GetString()!) != null;
                case "dragCore": return Shafts.Drag(b, b.shafts[I(0)], V(a[1]), V(a[2]));
                case "setAngle": return Shafts.SetAngle(b, b.shafts[I(0)], a[1].GetDouble());
                case "alignCore": return Shafts.Align(b, b.shafts[I(0)]);
                case "setRange": if (a[1].GetString() == "bottom") Shafts.SetBottom(b.shafts[I(0)], I(2)); else Shafts.SetTop(b.shafts[I(0)], I(2)); return null;
                default: throw new NotSupportedException("no C# step for " + op);
            }
        }

        static object? Door(DoorSpot? d)
        {
            if (d == null) return null;
            return d.exterior
                ? new Dictionary<string, object?> { ["kind"] = "ext", ["edge"] = d.edge, ["t"] = d.t, ["ei"] = d.entrance, ["L"] = d.L, ["k"] = d.k }
                : new Dictionary<string, object?> { ["kind"] = "int", ["wi"] = d.wall, ["t"] = d.t, ["di"] = d.door, ["L"] = d.L };
        }

        /// <summary>The prototype's recorded value against ours: numbers to 1e-9, objects key by key.</summary>
        static string? Compare(JsonElement want, object? got, string at = "returned")
        {
            switch (want.ValueKind)
            {
                case JsonValueKind.Null: return got == null ? null : $"{at} {Show(got)}, the prototype null";
                case JsonValueKind.True: case JsonValueKind.False: return got is bool g && g == want.GetBoolean() ? null : $"{at} {Show(got)}, the prototype {want}";
                case JsonValueKind.String: return got is string s && s == want.GetString() ? null : $"{at} {Show(got)}, the prototype {want}";
                case JsonValueKind.Number:
                {
                    double? v = got switch { double d => d, int i => i, _ => null };
                    return v != null && Math.Abs(v.Value - want.GetDouble()) <= 1e-9 ? null : $"{at} {Show(got)}, the prototype {want}";
                }
                case JsonValueKind.Array:
                {
                    var list = got switch { double[] d => d.Cast<object?>().ToList(), System.Collections.IList l => l.Cast<object?>().ToList(), _ => null };
                    if (list == null || list.Count != want.GetArrayLength()) return $"{at} {Show(got)}, the prototype {want.GetRawText()}";
                    int j = 0; foreach (var e in want.EnumerateArray()) { var r = Compare(e, list[j], $"{at}[{j}]"); if (r != null) return r; j++; }
                    return null;
                }
                default:
                {
                    if (got is not Dictionary<string, object?> o) return $"{at} {Show(got)}, the prototype {want.GetRawText()}";
                    foreach (var p in want.EnumerateObject())
                    {
                        if (!o.TryGetValue(p.Name, out var v)) return $"{at}: no {p.Name} (the prototype {want.GetRawText()})";
                        var r = Compare(p.Value, v, $"{at}.{p.Name}"); if (r != null) return r + $" (the prototype {want.GetRawText()})";
                    }
                    return null;
                }
            }
        }

        static string Show(object? o) => o switch
        {
            null => "null",
            Dictionary<string, object?> d => "{" + string.Join(", ", d.Select(kv => kv.Key + ": " + Show(kv.Value))) + "}",
            System.Collections.IList l => "[" + string.Join(", ", l.Cast<object?>().Select(Show)) + "]",
            double v => v.ToString("R", CultureInfo.InvariantCulture),
            _ => o.ToString() ?? "",
        };

        static string Diff(string a, string b)
        {
            int j = 0; while (j < a.Length && j < b.Length && a[j] == b[j]) j++;
            int s = Math.Max(0, j - 60); return a.Substring(s, Math.Min(160, a.Length - s));
        }
    }
}
