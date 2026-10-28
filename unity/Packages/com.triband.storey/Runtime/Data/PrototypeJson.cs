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
            var w = new Where();

            d.v = c.Int(root, "v", DocumentVersion, w);
            if (d.v != DocumentVersion)
                throw new FormatException($"layout version {d.v} is not supported (this reader knows {DocumentVersion})");
            d.seq = c.Int(root, "seq", 0, w);
            d.spawn = c.Point(root, "spawn", w);
            int i = 0;
            foreach (var item in c.Array(root, "buildings", w))
            {
                var at = w.In("buildings", i++);
                d.buildings.Add(Building(c, c.Obj(item, at), at));
            }
            if (root.TryGetValue("palette", out var pal) && pal != null)
            {
                foreach (var kv in c.Obj(pal, w.In("palette")))
                {
                    var at = w.In("palette").In(kv.Key);
                    if (!ColorRef.IsPaletteId(kv.Key)) throw new FormatException($"{at}: a palette block is keyed by palette ids");
                    var e = c.Obj(kv.Value, at);
                    d.palette[kv.Key] = new PaletteEntry(c.Str(e, "name", "", at), c.Color(e, "hex", "", at));
                    if (!ColorRef.IsHex(d.palette[kv.Key].hex)) throw new FormatException($"{at}.hex: a palette entry's colour is a #RRGGBB colour");
                    c.Check(e, at, Keys.PaletteEntry);
                }
            }
            c.Check(root, w, Keys.Root);
            return r;
        }

        /// <summary>A facade style on its own, as a style preset asset stores it.</summary>
        public static FacadeStyle ReadStyle(string json, List<string>? unknown = null)
        {
            var root = Json.Parse(json) as Dictionary<string, object?> ?? throw new FormatException("a style is a JSON object");
            return Style(new Ctx(unknown ?? new List<string>()), root, new Where().In("style"), new FacadeStyle());
        }

        public static string WriteStyle(FacadeStyle s) => Json.Write(Style(s));

        /// <summary>A single building, as the prototype copies one to the clipboard.</summary>
        public static BuildingData ReadBuilding(string json, List<string>? unknown = null)
        {
            var root = Json.Parse(json) as Dictionary<string, object?>
                       ?? throw new FormatException("a building is a JSON object");
            return Building(new Ctx(unknown ?? new List<string>()), root, new Where());
        }

        // the model's own lists and default style are filled where they are, not replaced (docs/PERFORMANCE.md §6.1): a
        // replaced one was garbage the moment it was made
        static BuildingData Building(Ctx c, Dictionary<string, object?> o, Where w)
        {
            var b = new BuildingData
            {
                id = c.Str(o, "id", "", w),
                name = c.Str(o, "name", "", w),
                pos = c.Point(o, "pos", w),
                elevation = c.Num(o, "elevation", 0, w),
                groundHeight = c.Num(o, "groundHeight", 3.6, w),
                floorHeight = c.Num(o, "floorHeight", 3.0, w),
                interior = c.Bool(o, "interior", true, w),
                gen = c.Bool(o, "gen", false, w),
            };
            c.Points(o, "footprint", w, b.footprint);
            c.Ints(o, "blank", w, b.blank);
            if (o.TryGetValue("style", out var st) && st != null) Style(c, c.Obj(st, w.In("style")), w.In("style"), b.style);
            int i = 0;
            foreach (var f in c.Array(o, "floors", w)) { var at = w.In("floors", i++); b.floors.Add(Floor(c, c.Obj(f, at), at)); }
            i = 0;
            foreach (var s in c.Array(o, "shafts", w)) { var at = w.In("shafts", i++); b.shafts.Add(Core(c, c.Obj(s, at), at)); }
            i = 0;
            foreach (var e in c.Array(o, "entrances", w))
            {
                var ep = w.In("entrances", i++); var eo = c.Obj(e, ep);
                b.entrances.Add(new EntranceData { edge = c.Int(eo, "edge", 0, ep), t = c.Num(eo, "t", 0.5, ep), k = c.Int(eo, "k", 0, ep) });
                c.Check(eo, ep, Keys.Entrance);
            }
            i = 0;
            foreach (var dd in c.Array(o, "details", w))
            {
                var dp = w.In("details", i++); var dob = c.Obj(dd, dp);
                b.details.Add(new DetailData
                {
                    kind = Kind(c.Str(dob, "kind", "", dp)),
                    k = c.Int(dob, "k", 0, dp), edge = c.Int(dob, "edge", 0, dp), t = c.Num(dob, "t", 0.5, dp), y = c.Opt(dob, "y", dp),
                });
                c.Check(dob, dp, Keys.Detail);
            }
            i = 0;
            foreach (var vv in c.Array(o, "voids", w))
            {
                var vp = w.In("voids", i++); var vo = c.Obj(vv, vp);
                var v = new VoidData { id = c.Str(vo, "id", "", vp), kind = c.Enum<VoidKind>(vo, "kind", VoidKind.Courtyard, vp), bottom = c.Int(vo, "bottom", 0, vp) };
                c.Points(vo, "shape", vp, v.shape);
                b.voids.Add(v);
                c.Check(vo, vp, Keys.Void);
            }
            i = 0;
            foreach (var bb in c.Array(o, "bridges", w))
            {
                var bp = w.In("bridges", i++); var bo = c.Obj(bb, bp);
                b.bridges.Add(new BridgeData { id = c.Str(bo, "id", "", bp), to = c.Str(bo, "to", "", bp), k = c.Int(bo, "k", 1, bp), at = c.Point(bo, "at", bp), toK = c.Int(bo, "toK", 1, bp), width = c.Num(bo, "width", 2.4, bp), open = c.Bool(bo, "open", false, bp) });
                c.Check(bo, bp, Keys.Bridge);
            }
            Corners(c, o, w, b.corners);
            c.Check(o, w, Keys.Building);
            return b;
        }

        static void Corners(Ctx c, Dictionary<string, object?> o, Where w, List<CornerData> into)
        {
            int i = 0;
            foreach (var cc in c.Array(o, "corners", w))
            {
                var cp = w.In("corners", i++); var co = c.Obj(cc, cp);
                var corner = new CornerData { at = c.Point(co, "at", cp), shape = c.Enum<CornerShape>(co, "shape", CornerShape.Chamfer, cp), size = c.Num(co, "size", 2, cp), door = c.Bool(co, "door", false, cp) };
                c.Points(co, "pts", cp, corner.pts);
                into.Add(corner);
                c.Check(co, cp, Keys.Corner);
            }
        }

        static object? Corners(List<CornerData> l)
        {
            var o = new List<object?>();
            foreach (var c in l)
            {
                var co = new Dictionary<string, object?> { ["at"] = Pt(c.at), ["shape"] = Lower(c.shape), ["size"] = c.size, ["pts"] = Pts(c.pts) };
                if (c.door) co["door"] = true;
                o.Add(co);
            }
            return o;
        }

        static FloorData Floor(Ctx c, Dictionary<string, object?> o, Where w)
        {
            var f = new FloorData
            {
                h = c.Opt(o, "h", w),
                filled = c.Bool(o, "filled", false, w),
            };
            c.Points(o, "shape", w, f.shape);
            c.Ints(o, "blank", w, f.blank);
            int i = 0;
            foreach (var wv in c.Array(o, "walls", w))
            {
                var wp = w.In("walls", i++); var wo = c.Obj(wv, wp);
                var wall = new WallData { a = c.Point(wo, "a", wp), b = c.Point(wo, "b", wp) };
                int j = 0;
                foreach (var d in c.Array(wo, "doors", wp))
                {
                    var dp = wp.In("doors", j++); var dobj = c.Obj(d, dp);
                    wall.doors.Add(new DoorData { t = c.Num(dobj, "t", 0.5, dp) });
                    c.Check(dobj, dp, Keys.Door);
                }
                c.Check(wo, wp, Keys.Wall);
                f.walls.Add(wall);
            }
            if (o.TryGetValue("style", out var st) && st != null) f.style = Style(c, c.Obj(st, w.In("style")), w.In("style"), new FacadeStyle());
            if (o.TryGetValue("terraceRoof", out var tr) && tr != null)
            {
                var tp = w.In("terraceRoof"); var to = c.Obj(tr, tp);
                f.terraceRoof = new TerraceRoofData { pitch = c.Num(to, "pitch", 30, tp) };
                c.Check(to, tp, Keys.Terrace);
            }
            // "props" is a leftover key some test layouts carry; the prototype never reads it.
            Corners(c, o, w, f.corners);
            c.Check(o, w, Keys.Floor);
            return f;
        }

        static CoreData Core(Ctx c, Dictionary<string, object?> o, Where w)
        {
            var s = new CoreData
            {
                id = c.Str(o, "id", "", w),
                type = c.Enum<CoreType>(o, "type", CoreType.Stairs, w),
                x = c.Num(o, "x", 0, w), z = c.Num(o, "z", 0, w), rot = c.Num(o, "rot", 0, w),
                bottom = c.Int(o, "bottom", 0, w), top = c.Int(o, "top", -1, w),
                roof = c.Bool(o, "roof", false, w),
            };
            c.Check(o, w, Keys.Core);
            return s;
        }

        // into a style made with its defaults (a building's own, which it has from the start, or a new one): every field
        // read is set, the rest keep the defaults
        static FacadeStyle Style(Ctx c, Dictionary<string, object?> o, Where path, FacadeStyle s)
        {
            s.preset = o.TryGetValue("preset", out var p) && p is string ps ? ps : null;
            s.label = c.Str(o, "label", "", path);
            s.wall = c.Color(o, "wall", "#9A4B38", path);
            s.trim = c.Color(o, "trim", "#ECE5D8", path);
            s.interior = c.Color(o, "interior", "#EFECE5", path);
            s.floor = c.Color(o, "floor", "#B88D62", path);
            s.roof = c.Color(o, "roof", "#6E716B", path);
            s.core = c.Color(o, "core", "#C9C4BB", path);
            s.glass = c.Color(o, "glass", "#8DB3C8", path);
            s.door = c.OptColor(o, "door", path);
            s.rail = c.OptColor(o, "rail", path);
            s.metal = c.OptColor(o, "metal", path);
            s.ceiling = c.OptColor(o, "ceiling", path);
            s.liftInterior = c.OptColor(o, "liftInterior", path);
            s.liftButton = c.OptColor(o, "liftButton", path);
            s.detailMetal = c.OptColor(o, "detailMetal", path);
            s.grille = c.OptColor(o, "grille", path);
            s.detailDark = c.OptColor(o, "detailDark", path);
            s.dish = c.OptColor(o, "dish", path);
            s.frame = c.OptColor(o, "frame", path);
            s.foundationColor = c.OptColor(o, "foundationColor", path);
            s.plinth = c.OptColor(o, "plinth", path);
            s.windows = c.Enum<WindowType>(o, "windows", WindowType.Punched, path);
            s.winW = c.Num(o, "winW", 1.2, path);
            s.bay = c.Num(o, "bay", 2.6, path);
            s.ground = c.Enum<GroundType>(o, "ground", GroundType.Storefront, path);
            s.bands = c.Bool(o, "bands", true, path);
            s.parapet = c.Bool(o, "parapet", true, path);
            s.roofType = c.Enum<RoofType>(o, "roofType", RoofType.Flat, path);
            s.pitch = c.Opt(o, "pitch", path);
            s.eave = c.Opt(o, "eave", path);
            s.mansard = c.Opt(o, "mansard", path);
            s.dormers = c.Opt(o, "dormers", path);
            s.head = c.Enum<HeadType>(o, "head", HeadType.Flat, path);
            s.frames = c.Bool(o, "frames", false, path);
            s.sills = c.Bool(o, "sills", true, path);
            s.heads = c.Bool(o, "heads", true, path);
            s.bandH = c.Opt(o, "bandH", path);
            s.bandDepth = c.Opt(o, "bandDepth", path);
            s.bandWall = c.Bool(o, "bandWall", false, path);
            s.plinthH = c.Opt(o, "plinthH", path);
            s.foundation = c.Opt(o, "foundation", path);
            s.foundationH = c.Opt(o, "foundationH", path);
            s.foundationOut = c.Opt(o, "foundationOut", path);
            s.bricks = c.Opt(o, "bricks", path);
            s.windowKind = c.OptStr(o, "windowKind", path);
            s.doorKind = c.OptStr(o, "doorKind", path);
            s.windowSet = Entries(c, o, "windowSet", path);
            s.groundSet = Entries(c, o, "groundSet", path);
            s.doorSet = Entries(c, o, "doorSet", path);
            s.windowPick = c.Enum<WindowPick>(o, "windowPick", WindowPick.Window, path);
            s.sillH = c.Opt(o, "sillH", path);
            if (o.TryGetValue("panes", out var pn) && pn != null)
            {
                var pl = pn as List<object?> ?? throw new FormatException(path.At("panes") + ": expected [columns, rows]");
                if (pl.Count != 2) throw new FormatException(path.At("panes") + ": expected [columns, rows]");
                s.paneCols = Math.Max(1, (int)Convert.ToDouble(pl[0], System.Globalization.CultureInfo.InvariantCulture)); s.paneRows = Math.Max(1, (int)Convert.ToDouble(pl[1], System.Globalization.CultureInfo.InvariantCulture));
            }
            if (o.TryGetValue("details", out var dr) && dr != null)
            {
                var dp = path.In("details"); var dob = c.Obj(dr, dp);
                s.details = new DetailRules { ac = c.Opt(dob, "ac", dp), vents = c.Opt(dob, "vents", dp) };
                // the artist's kinds, by their ids (32 hex digits)
                string[] known = Keys.DetailRules;
                foreach (var key in dob.Keys)
                    if (Generate.DetailKinds.IsAssetId(key) && c.Opt(dob, key, dp) is double pct)
                    {
                        (s.details.kinds ??= new SortedDictionary<string, double>(StringComparer.Ordinal))[key] = pct;
                        System.Array.Resize(ref known, known.Length + 1); known[known.Length - 1] = key;   // a copy: the shared set stays
                    }
                c.Check(dob, dp, known);
            }
            c.Check(o, path, Keys.Style);
            return s;
        }

        /// <summary>A style's set of windows or doors (docs/OPENINGS.md §7.1); absent is null. An entry is <c>{"kind": id}</c>, <c>{"none": true}</c> or <c>{}</c> (the generator's), with a <c>"weight"</c> when not 1.</summary>
        static List<OpeningEntry>? Entries(Ctx c, Dictionary<string, object?> o, string key, Where path)
        {
            if (!o.TryGetValue(key, out var v) || v == null) return null;
            var items = c.Array(o, key, path); var set = new List<OpeningEntry>(items.Count); int i = 0;
            foreach (var it in items)
            {
                var at = path.In(key, i++); var eo = c.Obj(it, at);
                set.Add(new OpeningEntry { kind = c.OptStr(eo, "kind", at), none = c.Bool(eo, "none", false, at), weight = c.Num(eo, "weight", 1, at) });
                c.Check(eo, at, Keys.OpeningEntry);
            }
            return set;
        }

        static List<object?> Entries(List<OpeningEntry> set)
        {
            var l = new List<object?>(set.Count);
            foreach (var e in set)
            {
                var eo = new Dictionary<string, object?>();
                if (e.none) eo["none"] = true;
                else if (!string.IsNullOrEmpty(e.kind)) eo["kind"] = e.kind;
                if (e.weight != 1) eo["weight"] = e.weight;
                l.Add(eo);
            }
            return l;
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
                ["id"] = b.id, ["name"] = b.name, ["pos"] = Pt(b.pos),
            };
            if (b.elevation != 0) o["elevation"] = b.elevation;   // only when raised: a layout on flat ground reads as before
            o["footprint"] = Pts(b.footprint);
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
                    var dobj = new Dictionary<string, object?> { ["kind"] = dd.kind, ["k"] = dd.k, ["edge"] = dd.edge, ["t"] = dd.t };
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
            if (b.voids.Count > 0)
            {
                var vs = new List<object?>();
                foreach (var v in b.voids) vs.Add(new Dictionary<string, object?> { ["id"] = v.id, ["kind"] = Lower(v.kind), ["shape"] = Pts(v.shape), ["bottom"] = v.bottom });
                o["voids"] = vs;
            }
            if (b.corners.Count > 0) o["corners"] = Corners(b.corners);
            if (b.bridges.Count > 0)
            {
                var bs = new List<object?>();
                foreach (var br in b.bridges) bs.Add(new Dictionary<string, object?> { ["id"] = br.id, ["to"] = br.to, ["k"] = br.k, ["at"] = Pt(br.at), ["toK"] = br.toK, ["width"] = br.width, ["open"] = br.open });
                o["bridges"] = bs;
            }
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
            if (f.filled) o["filled"] = true;
            if (f.HasShape) { o["shape"] = Pts(f.shape); o["blank"] = Ints(f.blank); if (f.corners.Count > 0) o["corners"] = Corners(f.corners); }
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
            if (s.mansard.HasValue) o["mansard"] = s.mansard.Value;
            if (s.dormers.HasValue) o["dormers"] = s.dormers.Value;
            if (s.head != HeadType.Flat) o["head"] = Lower(s.head);
            if (!s.sills) o["sills"] = false;
            if (!s.heads) o["heads"] = false;
            if (s.paneCols.HasValue || s.paneRows.HasValue) o["panes"] = new List<object?> { (double)(s.paneCols ?? 1), (double)(s.paneRows ?? 1) };
            if (s.frames) o["frames"] = true;
            if (s.bandH.HasValue) o["bandH"] = s.bandH.Value;
            if (s.bandDepth.HasValue) o["bandDepth"] = s.bandDepth.Value;
            if (s.bandWall) o["bandWall"] = true;
            if (s.plinthH.HasValue) o["plinthH"] = s.plinthH.Value;
            if (s.foundation.HasValue) o["foundation"] = s.foundation.Value;
            if (s.foundationH.HasValue) o["foundationH"] = s.foundationH.Value;
            if (s.foundationOut.HasValue) o["foundationOut"] = s.foundationOut.Value;
            if (s.bricks.HasValue) o["bricks"] = s.bricks.Value;
            if (!string.IsNullOrEmpty(s.windowKind)) o["windowKind"] = s.windowKind;
            if (!string.IsNullOrEmpty(s.doorKind)) o["doorKind"] = s.doorKind;
            if (s.windowSet is { Count: > 0 }) o["windowSet"] = Entries(s.windowSet);
            if (s.groundSet is { Count: > 0 }) o["groundSet"] = Entries(s.groundSet);
            if (s.doorSet is { Count: > 0 }) o["doorSet"] = Entries(s.doorSet);
            if (s.windowPick != WindowPick.Window) o["windowPick"] = Lower(s.windowPick);
            if (s.sillH.HasValue) o["sillH"] = s.sillH.Value;
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
            if (s.frame != null) o["frame"] = s.frame;
            if (s.foundationColor != null) o["foundationColor"] = s.foundationColor;
            if (s.plinth != null) o["plinth"] = s.plinth;
            if (s.details != null)
            {
                var d = new Dictionary<string, object?>();
                if (s.details.ac.HasValue) d["ac"] = s.details.ac.Value;
                if (s.details.vents.HasValue) d["vents"] = s.details.vents.Value;
                if (s.details.kinds != null) foreach (var kv in s.details.kinds) d[kv.Key] = kv.Value;
                o["details"] = d;
            }
            return o;
        }

        static Dictionary<string, object?> Pt(Vec2 p) => new Dictionary<string, object?> { ["x"] = p.x, ["z"] = p.z };
        static List<object?> Pts(List<Vec2> ps) { var l = new List<object?>(ps.Count); foreach (var p in ps) l.Add(Pt(p)); return l; }
        static List<object?> Ints(List<int> xs) { var l = new List<object?>(xs.Count); foreach (var x in xs) l.Add(x); return l; }
        static string Lower<T>(T e) where T : struct => e.ToString()!.ToLowerInvariant();

        // a detail's kind: the generator's own in any case, an artist's id as it is; none is an AC unit, as the prototype has it
        static string Kind(string s) => s.Length == 0 ? Generate.DetailKinds.Ac : Generate.DetailKinds.Id(s);

        // ---- reading helpers --------------------------------------------------------------

        /// <summary>
        /// Where an element is in the layout, made into text only when an error or an unknown key is reported
        /// (docs/PERFORMANCE.md §6.1): up to six steps from the root, each a key and, into an array, its index. A struct,
        /// passed down by value: each element made its path as text before, about 15 MB of strings for a 3,000-building
        /// layout, read once by nothing.
        /// </summary>
        struct Where
        {
            string? k0, k1, k2, k3, k4, k5; int i0, i1, i2, i3, i4, i5, n;

            /// <summary>One step further: <paramref name="key"/>, and its <paramref name="index"/> in an array (-1: none).</summary>
            public Where In(string key, int index = -1)
            {
                var w = this;
                switch (w.n)
                {
                    case 0: w.k0 = key; w.i0 = index; break;
                    case 1: w.k1 = key; w.i1 = index; break;
                    case 2: w.k2 = key; w.i2 = index; break;
                    case 3: w.k3 = key; w.i3 = index; break;
                    case 4: w.k4 = key; w.i4 = index; break;
                    case 5: w.k5 = key; w.i5 = index; break;
                    default: return w;   // deeper than a layout goes: the path stops there
                }
                w.n++;
                return w;
            }

            /// <summary>A key of the element here, as text: <c>buildings[3].floors[1].h</c>.</summary>
            public string At(string key) => In(key).ToString();

            public override string ToString()
            {
                var b = new System.Text.StringBuilder();
                for (int s = 0; s < n; s++)
                {
                    var (k, i) = s switch { 0 => (k0, i0), 1 => (k1, i1), 2 => (k2, i2), 3 => (k3, i3), 4 => (k4, i4), _ => (k5, i5) };
                    if (b.Length > 0) b.Append('.');
                    b.Append(k);
                    if (i >= 0) b.Append('[').Append(i.ToString(CultureInfo.InvariantCulture)).Append(']');
                }
                return b.ToString();
            }
        }

        /// <summary>The keys each kind of object may have, once, where each check made its list anew (3.9 MB a large layout).</summary>
        static class Keys
        {
            public static readonly string[] Root = { "v", "seq", "spawn", "buildings", "palette" };
            public static readonly string[] PaletteEntry = { "name", "hex" };
            public static readonly string[] Building = { "id", "name", "pos", "elevation", "footprint", "floors", "shafts", "entrances", "details", "blank", "groundHeight", "floorHeight", "interior", "style", "gen", "voids", "bridges", "corners" };
            public static readonly string[] Entrance = { "edge", "t", "k" };
            public static readonly string[] Detail = { "kind", "k", "edge", "t", "y" };
            public static readonly string[] Void = { "id", "kind", "shape", "bottom" };
            public static readonly string[] Bridge = { "id", "to", "k", "at", "toK", "width", "open" };
            public static readonly string[] Corner = { "at", "shape", "size", "door", "pts" };
            public static readonly string[] Floor = { "walls", "h", "shape", "blank", "style", "terraceRoof", "props", "filled", "corners" };
            public static readonly string[] Wall = { "a", "b", "doors" };
            public static readonly string[] Door = { "t" };
            public static readonly string[] Terrace = { "pitch" };
            public static readonly string[] Core = { "id", "type", "x", "z", "rot", "bottom", "top", "roof" };
            public static readonly string[] DetailRules = { "ac", "vents" };
            public static readonly string[] OpeningEntry = { "kind", "none", "weight" };
            public static readonly string[] Point = { "x", "z" };
            public static readonly string[] Style =
            {
                "preset", "label", "wall", "trim", "interior", "floor", "roof", "core", "glass", "windows", "winW", "bay", "ground", "bands", "parapet", "roofType", "pitch", "eave", "mansard", "dormers", "details",
                "head", "sills", "heads", "panes", "frames", "bandH", "bandDepth", "bandWall", "plinthH", "foundation", "foundationH", "foundationOut", "bricks", "windowKind", "doorKind", "sillH",
                "windowSet", "groundSet", "doorSet", "windowPick",
                "door", "rail", "metal", "ceiling", "liftInterior", "liftButton", "detailMetal", "grille", "detailDark", "dish", "frame", "foundationColor", "plinth",
                "doorType",   // street doors' Canopy or Glazed, gone (every generated door is plain): read and dropped, not reported
            };
        }

        /// <summary>An enum's names, any case, looked up without the allocation <c>Enum.TryParse</c> makes on a device (§12.2).</summary>
        static class EnumNames<T> where T : struct, Enum
        {
            public static readonly Dictionary<string, T> ByName = Make();

            static Dictionary<string, T> Make()
            {
                var d = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
                foreach (T v in System.Enum.GetValues(typeof(T))) d[v.ToString()] = v;
                return d;
            }
        }

        sealed class Ctx
        {
            readonly List<string> unknown;
            public Ctx(List<string> unknown) { this.unknown = unknown; }

            public Dictionary<string, object?> Obj(object? v, Where w) =>
                v as Dictionary<string, object?> ?? throw new FormatException($"{w}: expected an object");

            static readonly List<object?> none = new List<object?>();

            /// <summary>An array's items; an absent one has none (a shared empty list: nothing may add to it).</summary>
            public List<object?> Array(Dictionary<string, object?> o, string key, Where w)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return none;
                return v as List<object?> ?? throw new FormatException($"{w.At(key)}: expected an array");
            }

            public double Num(Dictionary<string, object?> o, string key, double dflt, Where w)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return dflt;
                return v is double d ? d : throw new FormatException($"{w.At(key)}: expected a number");
            }

            /// <summary>A number that may be absent, where absence means something zero does not.</summary>
            public double? Opt(Dictionary<string, object?> o, string key, Where w)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return null;
                return v is double d ? d : throw new FormatException($"{w.At(key)}: expected a number");
            }

            public int Int(Dictionary<string, object?> o, string key, int dflt, Where w)
            {
                double d = Num(o, key, dflt, w);
                if (d != Math.Floor(d)) throw new FormatException($"{w.At(key)}: expected an integer");
                return (int)d;
            }

            public bool Bool(Dictionary<string, object?> o, string key, bool dflt, Where w)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return dflt;
                return v is bool b ? b : throw new FormatException($"{w.At(key)}: expected a boolean");
            }

            public string Str(Dictionary<string, object?> o, string key, string dflt, Where w)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return dflt;
                return v is string s ? s : throw new FormatException($"{w.At(key)}: expected a string");
            }

            public string? OptStr(Dictionary<string, object?> o, string key, Where w)
            {
                string v = Str(o, key, "", w);
                return v.Length > 0 ? v : null;
            }

            /// <summary>A colour reference: a CSS literal or a palette id (docs/COLOURS.md §3.1).</summary>
            public string Color(Dictionary<string, object?> o, string key, string dflt, Where w)
            {
                string s = Str(o, key, dflt, w);
                if (!ColorRef.IsHex(s) && !ColorRef.IsPaletteId(s)) throw new FormatException($"{w.At(key)}: '{s}' is neither a #RRGGBB colour nor a palette id");
                return s;
            }

            /// <summary>An optional colour reference; absent means the project default.</summary>
            public string? OptColor(Dictionary<string, object?> o, string key, Where w) =>
                o.TryGetValue(key, out var v) && v != null ? Color(o, key, "", w) : null;

            public T Enum<T>(Dictionary<string, object?> o, string key, T dflt, Where w) where T : struct, Enum
            {
                string s = Str(o, key, "", w);
                if (s.Length == 0) return dflt;
                if (EnumNames<T>.ByName.TryGetValue(s, out var e)) return e;
                if (System.Enum.TryParse<T>(s, true, out e)) return e;   // a number, as Enum.TryParse takes one
                throw new FormatException($"{w.At(key)}: '{s}' is not a {typeof(T).Name}");
            }

            public Vec2 Point(Dictionary<string, object?> o, string key, Where w)
            {
                if (!o.TryGetValue(key, out var v) || v == null) return default;
                var at = w.In(key); var p = Obj(v, at);
                var r = new Vec2(Num(p, "x", 0, at), Num(p, "z", 0, at));
                Check(p, at, Keys.Point);
                return r;
            }

            /// <summary>An array of points, into the model's own list.</summary>
            public void Points(Dictionary<string, object?> o, string key, Where w, List<Vec2> into)
            {
                var items = Array(o, key, w);
                if (into.Capacity < into.Count + items.Count) into.Capacity = into.Count + items.Count;
                int i = 0;
                foreach (var v in items)
                {
                    var at = w.In(key, i++); var p = Obj(v, at);
                    into.Add(new Vec2(Num(p, "x", 0, at), Num(p, "z", 0, at)));
                    Check(p, at, Keys.Point);
                }
            }

            /// <summary>An array of integers, into the model's own list.</summary>
            public void Ints(Dictionary<string, object?> o, string key, Where w, List<int> into)
            {
                foreach (var v in Array(o, key, w))
                    into.Add(v is double d && d == Math.Floor(d) ? (int)d : throw new FormatException($"{w.At(key)}: expected integers"));
            }

            /// <summary>Record every key of <paramref name="o"/> that the model has no field for.</summary>
            public void Check(Dictionary<string, object?> o, Where w, string[] known)
            {
                foreach (var key in o.Keys)
                    if (System.Array.IndexOf(known, key) < 0) unknown.Add(w.At(key));
            }
        }
    }
}
