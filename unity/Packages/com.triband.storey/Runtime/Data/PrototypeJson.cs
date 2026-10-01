#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Triband.Storey.Text;

namespace Triband.Storey
{
    /// <summary>
    /// Reads and writes the prototype's JSON layout (SPEC §3): the text in the editor's
    /// "Layout data" panel, and the contents of a <c>.storey</c> file.
    /// </summary>
    /// <remarks>
    /// Reading is strict about shape and lenient about presence: a missing key takes the
    /// prototype's default, a key this version does not know is reported through
    /// <see cref="ReadResult.Unknown"/> rather than dropped silently, so a field the prototype
    /// grows shows up in the fixture tests the day it lands. Writing reproduces the
    /// prototype's own omissions (no <c>roofType</c> for flat roofs, no <c>k</c> for street
    /// doors, no <c>shape</c> where there is none), so a document that came from the
    /// prototype writes back structurally identical.
    /// </remarks>
    public static class PrototypeJson
    {
        /// <summary>Document version this reader understands (the prototype's <c>v</c>).</summary>
        public const int DocumentVersion = 2;

        public sealed class ReadResult
        {
            public StoreyDocument Document = new StoreyDocument();
            /// <summary>JSON paths of keys the model has no field for, e.g. <c>buildings[3].floors[1].foo</c>.</summary>
            public List<string> Unknown = new List<string>();
        }

        public static ReadResult Read(string json)
        {
            var root = Json.Parse(json) as Dictionary<string, object?>
                       ?? throw new FormatException("a layout is a JSON object");
            var r = new ReadResult();
            var c = new Ctx(r.Unknown);
            var d = r.Document;

            d.v = c.Int(root, "v", DocumentVersion, "");
            if (d.v != DocumentVersion)
                throw new FormatException($"layout version {d.v} is not supported (this reader knows {DocumentVersion})");
            d.seq = c.Int(root, "seq", 0, "");
            d.spawn = c.Point(root, "spawn", "");
            int i = 0;
            foreach (var item in c.Array(root, "buildings", ""))
                d.buildings.Add(Building(c, c.Obj(item, $"buildings[{i}]"), $"buildings[{i++}]"));
            if (root.TryGetValue("palette", out var pal) && pal != null)
            {
                foreach (var kv in c.Obj(pal, "palette"))
                {
                    string at = "palette." + kv.Key;
                    if (!ColorRef.IsPaletteId(kv.Key)) throw new FormatException($"{at}: a palette block is keyed by palette ids");
                    var e = c.Obj(kv.Value, at);
                    d.palette[kv.Key] = new PaletteEntry(c.Str(e, "name", "", at), c.Color(e, "hex", "", at));
                    if (!ColorRef.IsHex(d.palette[kv.Key].hex)) throw new FormatException($"{at}.hex: a palette entry's colour is a #RRGGBB colour");
                    c.Check(e, at, "name", "hex");
                }
            }
            c.Check(root, "", "v", "seq", "spawn", "buildings", "palette");
            return r;
        }

        /// <summary>A single building, as the prototype copies one to the clipboard.</summary>
        public static BuildingData ReadBuilding(string json, List<string>? unknown = null)
        {
            var root = Json.Parse(json) as Dictionary<string, object?>
                       ?? throw new FormatException("a building is a JSON object");
            return Building(new Ctx(unknown ?? new List<string>()), root, "");
        }

        static BuildingData Building(Ctx c, Dictionary<string, object?> o, string path)
        {
            var b = new BuildingData
            {
                id = c.Str(o, "id", "", path),
                name = c.Str(o, "name", "", path),
                pos = c.Point(o, "pos", path),
                footprint = c.Points(o, "footprint", path),
                groundHeight = c.Num(o, "groundHeight", 3.6, path),
                floorHeight = c.Num(o, "floorHeight", 3.0, path),
                interior = c.Bool(o, "interior", true, path),
                gen = c.Bool(o, "gen", false, path),
                blank = c.Ints(o, "blank", path),
            };
            if (o.TryGetValue("style", out var st) && st != null) b.style = Style(c, c.Obj(st, path + ".style"), path + ".style");
            int i = 0;
            foreach (var f in c.Array(o, "floors", path)) b.floors.Add(Floor(c, c.Obj(f, $"{path}.floors[{i}]"), $"{path}.floors[{i++}]"));
            i = 0;
            foreach (var s in c.Array(o, "shafts", path)) b.shafts.Add(Core(c, c.Obj(s, $"{path}.shafts[{i}]"), $"{path}.shafts[{i++}]"));
            i = 0;
            foreach (var e in c.Array(o, "entrances", path))
            {
                var eo = c.Obj(e, $"{path}.entrances[{i}]"); var ep = $"{path}.entrances[{i++}]";
                b.entrances.Add(new EntranceData { edge = c.Int(eo, "edge", 0, ep), t = c.Num(eo, "t", 0.5, ep), k = c.Int(eo, "k", 0, ep) });
                c.Check(eo, ep, "edge", "t", "k");
            }
            i = 0;
            foreach (var dd in c.Array(o, "details", path))
            {
                var dob = c.Obj(dd, $"{path}.details[{i}]"); var dp = $"{path}.details[{i++}]";
                b.details.Add(new DetailData
                {
                    kind = c.Enum<DetailKind>(dob, "kind", DetailKind.Ac, dp),
                    k = c.Int(dob, "k", 0, dp), edge = c.Int(dob, "edge", 0, dp), t = c.Num(dob, "t", 0.5, dp), y = c.Opt(dob, "y", dp),
                });
                c.Check(dob, dp, "kind", "k", "edge", "t", "y");
            }
            c.Check(o, path, "id", "name", "pos", "footprint", "floors", "shafts", "entrances", "details", "blank", "groundHeight", "floorHeight", "interior", "style", "gen");
            return b;
        }

        static FloorData Floor(Ctx c, Dictionary<string, object?> o, string path)
        {
            var f = new FloorData
            {
                h = c.Opt(o, "h", path),
                shape = c.Points(o, "shape", path),
                blank = c.Ints(o, "blank", path),
            };
            int i = 0;
            foreach (var w in c.Array(o, "walls", path))
            {
                var wo = c.Obj(w, $"{path}.walls[{i}]"); var wp = $"{path}.walls[{i++}]";
                var wall = new WallData { a = c.Point(wo, "a", wp), b = c.Point(wo, "b", wp) };
                int j = 0;
                foreach (var d in c.Array(wo, "doors", wp))
                {
                    var dobj = c.Obj(d, $"{wp}.doors[{j}]");
                    wall.doors.Add(new DoorData { t = c.Num(dobj, "t", 0.5, $"{wp}.doors[{j}]") });
                    c.Check(dobj, $"{wp}.doors[{j++}]", "t");
                }
                c.Check(wo, wp, "a", "b", "doors");
                f.walls.Add(wall);
            }
            if (o.TryGetValue("style", out var st) && st != null) f.style = Style(c, c.Obj(st, path + ".style"), path + ".style");
            if (o.TryGetValue("terraceRoof", out var tr) && tr != null)
            {
                var to = c.Obj(tr, path + ".terraceRoof");
                f.terraceRoof = new TerraceRoofData { pitch = c.Num(to, "pitch", 30, path + ".terraceRoof") };
                c.Check(to, path + ".terraceRoof", "pitch");
            }
            // "props" is a leftover key some test layouts carry; the prototype never reads it.
            c.Check(o, path, "walls", "h", "shape", "blank", "style", "terraceRoof", "props");
            return f;
        }

        static CoreData Core(Ctx c, Dictionary<string, object?> o, string path)
        {
            var s = new CoreData
            {
                id = c.Str(o, "id", "", path),
                type = c.Enum<CoreType>(o, "type", CoreType.Stairs, path),
                x = c.Num(o, "x", 0, path), z = c.Num(o, "z", 0, path), rot = c.Num(o, "rot", 0, path),
                bottom = c.Int(o, "bottom", 0, path), top = c.Int(o, "top", -1, path),
                roof = c.Bool(o, "roof", false, path),
            };
            c.Check(o, path, "id", "type", "x", "z", "rot", "bottom", "top", "roof");
            return s;
        }

        static FacadeStyle Style(Ctx c, Dictionary<string, object?> o, string path)
        {
            var s = new FacadeStyle
            {
                preset = o.TryGetValue("preset", out var p) && p is string ps ? ps : null,
                label = c.Str(o, "label", "", path),
                wall = c.Color(o, "wall", "#9A4B38", path), trim = c.Color(o, "trim", "#ECE5D8", path),
                interior = c.Color(o, "interior", "#EFECE5", path), floor = c.Color(o, "floor", "#B88D62", path),
                roof = c.Color(o, "roof", "#6E716B", path), core = c.Color(o, "core", "#C9C4BB", path), glass = c.Color(o, "glass", "#8DB3C8", path),
                door = c.OptColor(o, "door", path),
                rail = c.OptColor(o, "rail", path),
                metal = c.OptColor(o, "metal", path),
                ceiling = c.OptColor(o, "ceiling", path),
                liftInterior = c.OptColor(o, "liftInterior", path),
                liftButton = c.OptColor(o, "liftButton", path),
                detailMetal = c.OptColor(o, "detailMetal", path),
                grille = c.OptColor(o, "grille", path),
                detailDark = c.OptColor(o, "detailDark", path),
                dish = c.OptColor(o, "dish", path),
                windows = c.Enum<WindowType>(o, "windows", WindowType.Punched, path),
                winW = c.Num(o, "winW", 1.2, path), bay = c.Num(o, "bay", 2.6, path),
                ground = c.Enum<GroundType>(o, "ground", GroundType.Storefront, path),
                bands = c.Bool(o, "bands", true, path), parapet = c.Bool(o, "parapet", true, path),
                roofType = c.Enum<RoofType>(o, "roofType", RoofType.Flat, path),
                pitch = c.Opt(o, "pitch", path), eave = c.Opt(o, "eave", path),
            };
            if (o.TryGetValue("details", out var dr) && dr != null)
            {
                var dob = c.Obj(dr, path + ".details");
                s.details = new DetailRules { ac = c.Opt(dob, "ac", path + ".details"), vents = c.Opt(dob, "vents", path + ".details") };
                c.Check(dob, path + ".details", "ac", "vents");
            }
            c.Check(o, path, "preset", "label", "wall", "trim", "interior", "floor", "roof", "core", "glass", "windows", "winW", "bay", "ground", "bands", "parapet", "roofType", "pitch", "eave", "details",
                "door", "rail", "metal", "ceiling", "liftInterior", "liftButton", "detailMetal", "grille", "detailDark", "dish");
            return s;
        }

        // ---- writing -----------------------------------------------------------------

        public static string Write(StoreyDocument d) => Json.Write(ToTree(d));

        public static string Write(BuildingData b) => Json.Write(ToTree(b));

        public static Dictionary<string, object?> ToTree(StoreyDocument d)
        {
            var o = new Dictionary<string, object?> { ["v"] = d.v, ["seq"] = d.seq };
            var bs = new List<object?>();
            foreach (var b in d.buildings) bs.Add(ToTree(b));
            o["buildings"] = bs;
            o["spawn"] = Pt(d.spawn);
            if (d.palette.Count > 0)
            {
                var p = new Dictionary<string, object?>();
                foreach (var kv in d.palette) p[kv.Key] = new Dictionary<string, object?> { ["name"] = kv.Value.name, ["hex"] = kv.Value.hex };
                o["palette"] = p;
            }
            return o;
        }

        public static Dictionary<string, object?> ToTree(BuildingData b)
        {
            var o = new Dictionary<string, object?>
            {
                ["id"] = b.id, ["name"] = b.name, ["pos"] = Pt(b.pos), ["footprint"] = Pts(b.footprint),
            };
            var floors = new List<object?>();
            foreach (var f in b.floors) floors.Add(Floor(f));
            o["floors"] = floors;
            var shafts = new List<object?>();
            foreach (var s in b.shafts) shafts.Add(new Dictionary<string, object?>
            {
                ["id"] = s.id, ["type"] = Lower(s.type), ["x"] = s.x, ["z"] = s.z, ["rot"] = s.rot, ["bottom"] = s.bottom, ["top"] = s.top, ["roof"] = s.roof,
            });
            o["shafts"] = shafts;
            var ents = new List<object?>();
            foreach (var e in b.entrances)
            {
                var eo = new Dictionary<string, object?> { ["edge"] = e.edge, ["t"] = e.t };
                if (e.k != 0) eo["k"] = e.k;
                ents.Add(eo);
            }
            o["entrances"] = ents;
            if (b.details.Count > 0)
            {
                var ds = new List<object?>();
                foreach (var dd in b.details)
                {
                    var dobj = new Dictionary<string, object?> { ["kind"] = Lower(dd.kind), ["k"] = dd.k, ["edge"] = dd.edge, ["t"] = dd.t };
                    if (dd.y.HasValue) dobj["y"] = dd.y.Value;
                    ds.Add(dobj);
                }
                o["details"] = ds;
            }
            o["blank"] = Ints(b.blank);   // the prototype always writes it, empty or not
            o["groundHeight"] = b.groundHeight;
            o["floorHeight"] = b.floorHeight;
            o["interior"] = b.interior;
            o["style"] = Style(b.style);
            if (b.gen) o["gen"] = true;
            return o;
        }

        static Dictionary<string, object?> Floor(FloorData f)
        {
            var o = new Dictionary<string, object?>();
            var walls = new List<object?>();
            foreach (var w in f.walls)
            {
                var doors = new List<object?>();
                foreach (var d in w.doors) doors.Add(new Dictionary<string, object?> { ["t"] = d.t });
                walls.Add(new Dictionary<string, object?> { ["a"] = Pt(w.a), ["b"] = Pt(w.b), ["doors"] = doors });
            }
            o["walls"] = walls;
            if (f.h.HasValue) o["h"] = f.h.Value;
            if (f.HasShape) { o["shape"] = Pts(f.shape); o["blank"] = Ints(f.blank); }
            if (f.style != null) o["style"] = Style(f.style);
            if (f.terraceRoof != null) o["terraceRoof"] = new Dictionary<string, object?> { ["pitch"] = f.terraceRoof.pitch };
            return o;
        }

        static Dictionary<string, object?> Style(FacadeStyle s)
        {
            var o = new Dictionary<string, object?>
            {
                ["label"] = s.label, ["wall"] = s.wall, ["trim"] = s.trim, ["interior"] = s.interior, ["floor"] = s.floor, ["roof"] = s.roof, ["core"] = s.core, ["glass"] = s.glass,
                ["windows"] = Lower(s.windows), ["winW"] = s.winW, ["bay"] = s.bay, ["ground"] = Lower(s.ground), ["bands"] = s.bands, ["parapet"] = s.parapet,
                ["preset"] = s.preset,
            };
            if (s.roofType != RoofType.Flat) o["roofType"] = Lower(s.roofType);
            if (s.pitch.HasValue) o["pitch"] = s.pitch.Value;
            if (s.eave.HasValue) o["eave"] = s.eave.Value;
            if (s.door != null) o["door"] = s.door;
            if (s.rail != null) o["rail"] = s.rail;
            if (s.metal != null) o["metal"] = s.metal;
            if (s.ceiling != null) o["ceiling"] = s.ceiling;
            if (s.liftInterior != null) o["liftInterior"] = s.liftInterior;
            if (s.liftButton != null) o["liftButton"] = s.liftButton;
            if (s.detailMetal != null) o["detailMetal"] = s.detailMetal;
            if (s.grille != null) o["grille"] = s.grille;
            if (s.detailDark != null) o["detailDark"] = s.detailDark;
            if (s.dish != null) o["dish"] = s.dish;
            if (s.details != null)
            {
                var d = new Dictionary<string, object?>();
                if (s.details.ac.HasValue) d["ac"] = s.details.ac.Value;
                if (s.details.vents.HasValue) d["vents"] = s.details.vents.Value;
                o["details"] = d;
            }
            return o;
        }

        static Dictionary<string, object?> Pt(Vec2 p) => new Dictionary<string, object?> { ["x"] = p.x, ["z"] = p.z };
        static List<object?> Pts(List<Vec2> ps) { var l = new List<object?>(ps.Count); foreach (var p in ps) l.Add(Pt(p)); return l; }
        static List<object?> Ints(List<int> xs) { var l = new List<object?>(xs.Count); foreach (var x in xs) l.Add(x); return l; }
        static string Lower<T>(T e) where T : struct => e.ToString()!.ToLowerInvariant();

        // ---- reading helpers --------------------------------------------------------------

        sealed class Ctx
        {
            readonly List<string> unknown;
            public Ctx(List<string> unknown) { this.unknown = unknown; }

            static string At(string path, string key) => path.Length == 0 ? key : path + "." + key;

            public Dictionary<string, object?> Obj(object? v, string path) =>
                v as Dictionary<string, object?> ?? throw new FormatException($"{path}: expected an object");

            public List<object?> Array(Dictionary<string, object?> o, string key, string path)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return new List<object?>();
                return v as List<object?> ?? throw new FormatException($"{At(path, key)}: expected an array");
            }

            public double Num(Dictionary<string, object?> o, string key, double dflt, string path)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return dflt;
                return v is double d ? d : throw new FormatException($"{At(path, key)}: expected a number");
            }

            /// <summary>A number that may be absent, where absence means something zero does not.</summary>
            public double? Opt(Dictionary<string, object?> o, string key, string path)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return null;
                return v is double d ? d : throw new FormatException($"{At(path, key)}: expected a number");
            }

            public int Int(Dictionary<string, object?> o, string key, int dflt, string path)
            {
                double d = Num(o, key, dflt, path);
                if (d != Math.Floor(d)) throw new FormatException($"{At(path, key)}: expected an integer");
                return (int)d;
            }

            public bool Bool(Dictionary<string, object?> o, string key, bool dflt, string path)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return dflt;
                return v is bool b ? b : throw new FormatException($"{At(path, key)}: expected a boolean");
            }

            public string Str(Dictionary<string, object?> o, string key, string dflt, string path)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return dflt;
                return v is string s ? s : throw new FormatException($"{At(path, key)}: expected a string");
            }

            /// <summary>A colour reference: a CSS literal or a palette id (docs/COLOURS.md §3.1).</summary>
            public string Color(Dictionary<string, object?> o, string key, string dflt, string path)
            {
                string s = Str(o, key, dflt, path);
                if (!ColorRef.IsHex(s) && !ColorRef.IsPaletteId(s)) throw new FormatException($"{At(path, key)}: '{s}' is neither a #RRGGBB colour nor a palette id");
                return s;
            }

            /// <summary>An optional colour reference; absent means the project default.</summary>
            public string? OptColor(Dictionary<string, object?> o, string key, string path) =>
                o.TryGetValue(key, out var v) && v != null ? Color(o, key, "", path) : null;

            public T Enum<T>(Dictionary<string, object?> o, string key, T dflt, string path) where T : struct
            {
                string s = Str(o, key, "", path);
                if (s.Length == 0) return dflt;
                if (System.Enum.TryParse<T>(s, true, out var e)) return e;
                throw new FormatException($"{At(path, key)}: '{s}' is not a {typeof(T).Name}");
            }

            public Vec2 Point(Dictionary<string, object?> o, string key, string path)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return default;
                var p = Obj(v, At(path, key));
                var r = new Vec2(Num(p, "x", 0, At(path, key)), Num(p, "z", 0, At(path, key)));
                Check(p, At(path, key), "x", "z");
                return r;
            }

            public List<Vec2> Points(Dictionary<string, object?> o, string key, string path)
            {
                var l = new List<Vec2>();
                int i = 0;
                foreach (var v in Array(o, key, path))
                {
                    var p = Obj(v, $"{At(path, key)}[{i}]");
                    l.Add(new Vec2(Num(p, "x", 0, $"{At(path, key)}[{i}]"), Num(p, "z", 0, $"{At(path, key)}[{i}]")));
                    Check(p, $"{At(path, key)}[{i++}]", "x", "z");
                }
                return l;
            }

            public List<int> Ints(Dictionary<string, object?> o, string key, string path)
            {
                var l = new List<int>();
                foreach (var v in Array(o, key, path))
                    l.Add(v is double d && d == Math.Floor(d) ? (int)d : throw new FormatException($"{At(path, key)}: expected integers"));
                return l;
            }

            /// <summary>Record every key of <paramref name="o"/> that the model has no field for.</summary>
            public void Check(Dictionary<string, object?> o, string path, params string[] known)
            {
                foreach (var key in o.Keys)
                    if (System.Array.IndexOf(known, key) < 0) unknown.Add(At(path, key));
            }
        }
    }
}
