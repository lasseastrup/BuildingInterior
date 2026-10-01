using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Replays a fixture of multi-step edit cases written by prototype/tools/export-ops.mjs: each case starts from a
    /// corpus building (the others in place), runs its steps, and must return what the prototype's last step returned
    /// and leave the building the prototype left ("unchanged" when it is as it started; none for queries).
    /// </summary>
    internal sealed class OpsReplay
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

        public delegate object? StepFn(Site site, BuildingData b, string op, JsonElement[] args);

        readonly Dictionary<string, Corpus> corpora = new Dictionary<string, Corpus> { ["demo"] = new Corpus("demo"), ["variants"] = new Corpus("variants") };
        public readonly JsonElement Root;
        readonly JsonElement[] cases;

        public OpsReplay(string fixture)
        {
            Root = JsonDocument.Parse(Fixtures.Text(fixture)).RootElement;
            cases = Root.GetProperty("cases").EnumerateArray().ToArray();
        }

        public StoreyDocument Demo => corpora["demo"].doc;

        public IEnumerable<object[]> Names() => cases.Select(c => c.GetProperty("name").GetString()!).Distinct().Select(n => new object[] { n });

        public void Check(string name, StepFn step)
        {
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
                    try { ret = step(site, b, s.GetProperty("op").GetString()!, s.GetProperty("args").EnumerateArray().ToArray()); }
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

        /// <summary>The prototype's recorded value against ours: numbers to 1e-9, objects key by key (keys the prototype records only).</summary>
        public static string? Compare(JsonElement want, object? got, string at = "returned")
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

        public static string Show(object? o) => o switch
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
