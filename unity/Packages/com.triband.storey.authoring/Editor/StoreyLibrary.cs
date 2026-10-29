#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>What the Storey Library holds (docs/EDITOR.md §6.13), in the order its side bar lists them.</summary>
    internal enum LibraryKind { Styles, Windows, Doors, Details, Templates }

    /// <summary>
    /// One thing in the Storey Library: an asset, the project's or one the package ships, with what the window shows of it
    /// and what it is found by.
    /// </summary>
    internal sealed class LibraryItem
    {
        public LibraryKind kind;
        public UnityEngine.Object asset = null!;
        public string guid = "", path = "";
        /// <summary>Its name in words, without what all the project's of its kind start with (Visual_BuildingPiece08_Door (door): Piece 08 Door).</summary>
        public string title = "";
        /// <summary>The family it is listed under (Industrial, Piece 08, Storey's own), and its title without the family's words.</summary>
        public string family = "", shortTitle = "";
        /// <summary>One the package ships: shown, not changed.</summary>
        public bool package;
        /// <summary>What a layout names it by: a style's preset key, a window's, door's or detail's id; empty for a template.</summary>
        public string key = "";
        /// <summary>The words it is found by, in lower case.</summary>
        public string words = "";

        public FacadeStylePreset? Style => asset as FacadeStylePreset;
        public StoreyOpening? Opening => asset as StoreyOpening;
        public StoreyDetail? Detail => asset as StoreyDetail;
        public StoreyBuildingTemplate? Template => asset as StoreyBuildingTemplate;
        public bool Alive => asset != null;
    }

    /// <summary>
    /// The Storey Library's contents (docs/EDITOR.md §6.13): every Facade Style, artist's window and door, Facade Detail and
    /// Building Template in the project and the package, as the Facade tab and the building bar already list them, each
    /// named in words and put in a family; the buildings an item is drawn on in the window; and what the window does to them
    /// (new, duplicate, rename, delete).
    /// </summary>
    internal static class StoreyLibrary
    {
        public static readonly LibraryKind[] Kinds = { LibraryKind.Styles, LibraryKind.Windows, LibraryKind.Doors, LibraryKind.Details, LibraryKind.Templates };

        /// <summary>A kind as the side bar names it.</summary>
        public static string Name(LibraryKind k) => k switch
        {
            LibraryKind.Styles => "Styles", LibraryKind.Windows => "Windows", LibraryKind.Doors => "Doors", LibraryKind.Details => "Details", _ => "Templates",
        };

        /// <summary>One of a kind, in a sentence ("a style").</summary>
        public static string One(LibraryKind k) => k switch
        {
            LibraryKind.Styles => "style", LibraryKind.Windows => "window", LibraryKind.Doors => "door", LibraryKind.Details => "detail", _ => "template",
        };

        /// <summary>What a kind is, for the top of its list.</summary>
        public static string About(LibraryKind k) => k switch
        {
            LibraryKind.Styles => "Facade styles: the whole look of a building's walls, windows, doors, colours and details. Apply one to a building, or change one here to change it everywhere it's applied from now on.",
            LibraryKind.Windows => "Windows made from your prefabs or meshes. A style picks one or several to fill its openings. Drop window prefabs here to make more.",
            LibraryKind.Doors => "Street doors made from your prefabs or meshes. A style picks one or several, and the Doors card places them. Drop door prefabs here to make more.",
            LibraryKind.Details => "Facade details: things on the walls, placed by hand or by a style's rules (under windows, scattered, up the corners). Drop prefabs here to make more.",
            _ => "Building templates: whole buildings kept to place again with New, or to take an outline from. Save one from a building with Save as template.",
        };

        /// <summary>A kind's colour, as the site inspector's cards have it (docs/EDITOR.md §6.10): styles purple, windows blue, doors amber, details green, templates teal.</summary>
        public static Color Tint(LibraryKind k) => k switch
        {
            LibraryKind.Styles => StoreyInspectorUI.Tint.Style, LibraryKind.Windows => StoreyInspectorUI.Tint.Windows, LibraryKind.Doors => StoreyInspectorUI.Tint.Doors,
            LibraryKind.Details => StoreyInspectorUI.Tint.Details, _ => StoreyInspectorUI.Tint.Roof,
        };

        /// <summary>A kind's icon, as the site inspector's cards draw it.</summary>
        public static Action<Rect, Color> Icon(LibraryKind k) => k switch
        {
            LibraryKind.Styles => StoreyInspectorUI.StyleIcon, LibraryKind.Windows => StoreyInspectorUI.WindowFrontIcon, LibraryKind.Doors => StoreyInspectorUI.DoorFrontIcon,
            LibraryKind.Details => StoreyInspectorUI.DetailsIcon, _ => StoreyInspectorUI.ShapeIcon,
        };

        public const string OwnFamily = "Storey's own", Others = "Others";

        static List<LibraryItem>? items;
        static bool hooked;

        /// <summary>Everything in the Library, by kind, then family, then title. Gathered once and kept until the project changes.</summary>
        public static IReadOnlyList<LibraryItem> Items()
        {
            if (!hooked) { hooked = true; EditorApplication.projectChanged += () => items = null; }
            if (items != null && items.All(i => i.Alive)) return items;
            var o = new List<LibraryItem>();
            try
            {
                foreach (var e in FacadeStylePreset.All()) o.Add(Make(LibraryKind.Styles, e.asset, e.key, e.own));
                foreach (var w in StoreyOpenings.All()) o.Add(Make(w.door ? LibraryKind.Doors : LibraryKind.Windows, w, w.Id, InPackage(w)));
                foreach (var d in StoreyDetails.All()) o.Add(Make(LibraryKind.Details, d, d.Id, StoreyDetails.Ours(d)));
                foreach (var t in StoreyBuildingTemplate.All()) o.Add(Make(LibraryKind.Templates, t, "", t.Own));
            }
            catch (Exception ex) { Debug.LogWarning("Storey Library: couldn't list everything (" + ex.Message + ")."); }
            foreach (var k in Kinds) Name(o.Where(i => i.kind == k).ToList());
            foreach (var i in o) i.words = Words(i);
            return items = o.OrderBy(i => (int)i.kind).ThenBy(i => i.package ? 2 : i.family == Others ? 1 : 0)
                .ThenBy(i => i.family, Natural).ThenBy(i => i.title, Natural).ToList();
        }

        /// <summary>Gather again next time: something was just made, copied, renamed or deleted.</summary>
        public static void Changed()
        {
            items = null;
            FacadeStylePreset.Changed(); StoreyOpenings.Changed(); StoreyDetails.Changed(); StoreyBuildingTemplate.Changed();
            StoreyUsage.Changed();
        }

        public static IEnumerable<LibraryItem> Of(LibraryKind k) => Items().Where(i => i.kind == k);

        public static LibraryItem? Find(UnityEngine.Object? asset) => asset == null ? null : Items().FirstOrDefault(i => i.asset == asset);

        public static LibraryItem? Find(string guid) => guid.Length == 0 ? null : Items().FirstOrDefault(i => i.guid == guid);

        static readonly Comparer<string> Natural = Comparer<string>.Create(EditorUtility.NaturalCompare);

        static bool InPackage(UnityEngine.Object a) => AssetDatabase.GetAssetPath(a).StartsWith("Packages/com.triband.storey", StringComparison.Ordinal);

        static LibraryItem Make(LibraryKind k, UnityEngine.Object a, string key, bool package)
        {
            string path = AssetDatabase.GetAssetPath(a);
            return new LibraryItem { kind = k, asset = a, path = path, guid = AssetDatabase.AssetPathToGUID(path), key = key, package = package };
        }

        /// <summary>
        /// The titles and families of one kind's items. The project's: their names' words, less what all of them start with
        /// and the asides in brackets (Visual_BuildingPiece08_Door (door): 08 Door); the family is the first word left, with
        /// the word before it when it is a number (Piece 08); a family of one goes with the others. The package's are Storey's
        /// own, under their own names.
        /// </summary>
        static void Name(List<LibraryItem> list)
        {
            var mine = list.Where(i => !i.package).ToList();
            var words = mine.ToDictionary(i => i, i => StoreyInspectorUI.Words(i.asset.name).Where(w => !w.StartsWith("(")).ToArray());
            int common = 0;
            if (mine.Count > 1)
            {
                var first = words[mine[0]];
                while (words.Values.All(w => w.Length > common + 1 && string.Equals(w[common], first[common], StringComparison.OrdinalIgnoreCase))) common++;
            }
            foreach (var i in mine)
            {
                var w = words[i];
                var rest = w.Length > common ? w.Skip(common).ToArray() : w;
                if (rest.Length == 0) rest = new[] { i.asset.name };
                bool number = rest[0].All(char.IsDigit) && common > 0;
                i.family = number ? w[common - 1] + " " + rest[0] : rest[0];
                i.title = (number ? w[common - 1] + " " : "") + string.Join(" ", rest);
                var after = rest.Skip(1).ToArray();
                i.shortTitle = after.Length == 0 || after.All(x => x.All(char.IsDigit)) ? i.title : string.Join(" ", after);
            }
            foreach (var g in mine.GroupBy(i => i.family).Where(g => g.Count() < 2).ToList())
                foreach (var i in g) { i.family = Others; i.shortTitle = i.title; }
            foreach (var i in list.Where(i => i.package)) { i.title = i.shortTitle = i.asset.name; i.family = OwnFamily; }
        }

        /// <summary>What an item is found by: its names, its kind and family, and what it is (a style's windows, roof and colours; a detail's rule).</summary>
        static string Words(LibraryItem i)
        {
            var w = new List<string> { i.title, i.family, i.asset.name, One(i.kind), Name(i.kind), i.package ? "storey built-in package" : "project" };
            try
            {
                switch (i.kind)
                {
                    case LibraryKind.Styles:
                        var st = i.Style!.Style;
                        w.Add(StoreyStyleCards.OpeningName(st.windows)); w.Add(st.roofType.ToString()); w.Add(StoreyStyleCards.RoofSummary(st));
                        foreach (var c in new[] { st.wall, st.trim, st.roof, st.glass, st.groundWall })
                            if (c != null && StoreyColorField.Describe(c) is (Color, string name)) w.Add(name);
                        break;
                    case LibraryKind.Windows:
                    case LibraryKind.Doors:
                        var o = i.Opening!;
                        w.Add(!o.stretch ? "keep size" : o.slice ? "slice" : "stretch");
                        if (o.prefab != null) w.Add(o.prefab.name);
                        break;
                    case LibraryKind.Details:
                        var k = DetailKinds.Get(i.key);
                        var d = i.Detail!;
                        var anchor = k?.anchor ?? d.anchor; var rule = k?.rule ?? d.rule;
                        w.Add(StoreyDetails.Anchors[(int)anchor].text);
                        w.Add(StoreyDetails.Rules(anchor).FirstOrDefault(r => r.rule == rule).label?.text ?? "");
                        if (d.prefab != null) w.Add(d.prefab.name);
                        break;
                    case LibraryKind.Templates:
                        w.Add(i.Template!.Summary());
                        break;
                }
            }
            catch (Exception) { }   // a style that doesn't read is still found by its name
            return string.Join(" ", w).ToLowerInvariant();
        }

        /// <summary>Whether an item has every word of <paramref name="search"/> (any order, any case).</summary>
        public static bool Matches(LibraryItem i, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            foreach (var word in search.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (!i.words.Contains(word)) return false;
            return true;
        }

        // ---- what a card says of an item ----

        /// <summary>What a card says under an item's name, its colours (a style's), and what is wrong with it: kept until the item changes.</summary>
        public sealed class Facts
        {
            public string stamp = "", line = "";
            public Color[] swatches = Array.Empty<Color>();
            public List<(string text, MessageType type)> issues = new List<(string, MessageType)>();
            public int Warnings => issues.Count(x => x.type == MessageType.Warning || x.type == MessageType.Error);
        }

        static readonly Dictionary<string, Facts> facts = new Dictionary<string, Facts>();

        public static Facts FactsOf(LibraryItem i)
        {
            string stamp = Stamp(i);
            if (facts.TryGetValue(i.guid, out var f) && f.stamp == stamp) return f;
            f = new Facts { stamp = stamp };
            try
            {
                switch (i.kind)
                {
                    case LibraryKind.Styles:
                    {
                        var st = i.Style!.Style;
                        OpeningSets.Uses(st, out bool generated, out _, out _);
                        f.line = (generated ? StoreyStyleCards.OpeningName(st.windows) : "Your windows") + " · " + (st.roofType == RoofType.Flat ? "flat roof" : st.roofType.ToString().ToLowerInvariant() + " roof");
                        var defaults = StoreyColorField.Defaults?.Invoke() ?? new StyleDefaults();
                        f.swatches = new[] { ColorSlot.Wall, ColorSlot.Trim, ColorSlot.Roof, ColorSlot.Glass }
                            .Select(s => StyleColors.Of(st, s, defaults) is string c && StoreyColorField.Describe(c) is (Color colour, string) ? colour : Color.magenta).ToArray();
                        break;
                    }
                    case LibraryKind.Windows:
                    case LibraryKind.Doors:
                    {
                        var o = i.Opening!;
                        if (o.prefab == null && o.mesh == null) { f.line = "Empty: give it a prefab"; f.issues.Add(("It has no prefab or mesh yet.", MessageType.Warning)); break; }
                        var k = OpeningKinds.Get(o.Id) ?? o.ToKind();
                        var c = OpeningKinds.Check(k);
                        f.line = (!o.stretch ? "Keep size" : o.slice ? "Slice" : "Stretch") + $" · {c.tris} tris";
                        f.issues = StoreyOpenings.Issues(o, k, c);
                        break;
                    }
                    case LibraryKind.Details:
                    {
                        var d = i.Detail!;
                        var k = DetailKinds.Get(d.Id);
                        var anchor = k?.anchor ?? d.anchor; var rule = k?.rule ?? d.rule;
                        string where = rule != Details.Rule.None ? StoreyDetails.Rules(anchor).FirstOrDefault(r => r.rule == rule).label?.text ?? "" : StoreyDetails.Anchors[(int)anchor].text;
                        if (d.BuiltIn) f.line = where;
                        else if (d.mesh == null && d.prefab == null) { f.line = "Empty: give it a prefab"; f.issues.Add(("It has no prefab or mesh yet.", MessageType.Warning)); }
                        else f.line = where + $" · {d.Made().tris} tris";
                        f.issues.AddRange(StoreyDetails.Issues(d));
                        break;
                    }
                    case LibraryKind.Templates:
                    {
                        var b = i.Template!.Building;
                        if (b != null) { var bb = Tiers.Bbox(b.footprint); f.line = $"{b.floors.Count} storeys · {bb.x1 - bb.x0:0.#} × {bb.z1 - bb.z0:0.#} m"; }
                        break;
                    }
                }
            }
            catch (Exception ex) { f.line = "Doesn't read"; f.issues.Add(("Storey can't read it: " + ex.Message, MessageType.Error)); }
            return facts[i.guid] = f;
        }

        /// <summary>
        /// What an item's pictures and facts are kept against: a style's or template's text, or a window's, door's or detail's
        /// changes (each bake marks it changed) and its file's time.
        /// </summary>
        public static string Stamp(LibraryItem i)
        {
            // asked for by every card in view at every repaint: a text is hashed again only when it is a new string, and a
            // file's time is read at most twice a second
            double now = EditorApplication.timeSinceStartup;
            object? text = i.kind == LibraryKind.Styles ? i.Style!.style : i.kind == LibraryKind.Templates ? i.Template!.building : null;
            stamps.TryGetValue(i.guid, out var s);
            if (text != null)
            {
                if (s.stamp != null && ReferenceEquals(s.from, text)) return s.stamp;
                s = (text, now, Hash((string)text));
            }
            else
            {
                if (s.stamp == null || now - s.at > 0.5) s = (null, now, (File.Exists(i.path) ? File.GetLastWriteTimeUtc(i.path).Ticks : 0).ToString("x"));
                stamps[i.guid] = s;
                return EditorUtility.GetDirtyCount(i.asset) + "." + s.stamp;   // each bake marks it changed: seen at once
            }
            stamps[i.guid] = s;
            return s.stamp;
        }
        static readonly Dictionary<string, (object? from, double at, string stamp)> stamps = new Dictionary<string, (object?, double, string)>(StringComparer.Ordinal);

        /// <summary>A short, steady hash of a text (FNV-1a), for file names and stamps.</summary>
        public static string Hash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        // ---- the building an item is drawn on ----

        /// <summary>The placeholder's footprints, the prototype's (<see cref="Setbacks.Presets"/>), by their keys and names.</summary>
        public static readonly (string key, string name)[] Shapes = { ("rect", "Rect"), ("L", "L"), ("U", "U"), ("T", "T"), ("oct", "Octa") };

        /// <summary>
        /// The style a window, door or detail is shown on, by its preset key: the one picked, or the project's first, or the
        /// brick walk-up; with a stamp of its text, for what is drawn with it. Read once while its text stays the same (every
        /// card asks at every repaint): don't change the style it gives, clone it.
        /// </summary>
        public static (FacadeStyle style, string key, string stamp) ShowOn(string key)
        {
            var all = FacadeStylePreset.All();
            var e = all.FirstOrDefault(x => x.key == key) ?? all.FirstOrDefault(x => !x.own) ?? all.FirstOrDefault(x => x.key == FacadeStylePreset.DefaultKey);
            if (e != null && shownOn is { } s && s.key == e.key && ReferenceEquals(s.from, e.asset.style)) return (s.style, s.key, s.stamp);
            if (e != null)
                try { shownOn = (e.key, e.asset.style, e.asset.Style, e.key + "." + Hash(e.asset.style)); return (shownOn.Value.style, e.key, shownOn.Value.stamp); }
                catch (Exception) { }
            return (Styles.Preset(FacadeStylePreset.DefaultKey), FacadeStylePreset.DefaultKey, FacadeStylePreset.DefaultKey);
        }
        static (string key, string from, FacadeStyle style, string stamp)? shownOn;

        /// <summary>
        /// The building the Library draws an item on (docs/EDITOR.md §6.13), and how the camera frames it: a shell of
        /// <paramref name="storeys"/> on footprint <paramref name="shape"/> with a street door on its front wall, wearing the
        /// style; a window or door in every opening of style <paramref name="on"/>; a facade detail by its rule at 100 %, or put
        /// by hand on the front, the generator's own AC units and vents off; a template as it is, in its own style or
        /// <paramref name="on"/>'s.
        /// </summary>
        public static (StoreyDocument doc, StoreyPreview.Framing framing) Placeholder(LibraryItem i, string shape, int storeys, FacadeStyle? on)
        {
            var d = new StoreyDocument();
            if (i.kind == LibraryKind.Templates)
            {
                var tb = i.Template!.Building ?? Buildings.Make(d, "rect", new Vec2(0, 0));
                var t = Buildings.FromTemplate(d, tb, new Vec2(0, 0));
                t.interior = false;   // a shell: windows show rooms, and nothing inside needs building
                if (on != null) Styles.Apply(t, 0, on);
                return (d, StoreyPreview.Framing.Building);
            }
            var b = Buildings.Make(d, Shapes.Any(s => s.key == shape) ? shape : "L", new Vec2(0, 0));
            Floors.SetCount(b, Math.Max(1, storeys));
            b.interior = false;
            d.buildings.Add(b);
            var style = (on ?? ShowOn("").style).Clone();
            switch (i.kind)
            {
                case LibraryKind.Styles:
                    b.style = i.Style!.Style.Clone();   // its own roof too: the style as it is kept
                    return (d, StoreyPreview.Framing.Building);
                case LibraryKind.Windows:
                    Register(i);
                    style.windowSet = null; style.groundSet = null; style.windowKind = i.key; style.ground = GroundType.Match;
                    if (style.windows == WindowType.None) style.windows = WindowType.Punched;
                    b.style = style;
                    return (d, StoreyPreview.Framing.Window);
                case LibraryKind.Doors:
                    Register(i);
                    style.doorSet = null; style.doorKind = i.key;
                    b.style = style;
                    return (d, StoreyPreview.Framing.Door);
                default:
                {
                    Register(i);
                    style.details = new DetailRules { ac = 0, vents = 0 };   // this detail alone
                    b.style = style;
                    var k = DetailKinds.Get(i.key);
                    if (k == null) return (d, StoreyPreview.Framing.Building);
                    if (i.key == DetailKinds.Ac) style.details.ac = 100;
                    else if (i.key == DetailKinds.Vent) style.details.vents = 100;
                    else if (Details.RuleFits(k)) style.details.kinds = new SortedDictionary<string, double>(StringComparer.Ordinal) { [k.id] = 100 };
                    else ByHand(b, k);
                    return (d, k.rule == Details.Rule.Corners || k.rule == Details.Rule.AlongTop || k.anchor == Details.Anchor.Tier ? StoreyPreview.Framing.Building
                        : k.anchor == Details.Anchor.Opening ? StoreyPreview.Framing.Door : StoreyPreview.Framing.Window);
                }
            }
        }

        /// <summary>A detail with no rule put on the placeholder's front wall by hand: over its door, up the wall beside it, or on each storey either side of it, between bays.</summary>
        static void ByHand(BuildingData b, DetailKind k)
        {
            var a = b.footprint[0]; var c = b.footprint[1];
            double len = Math.Max(1, Math.Sqrt((c.x - a.x) * (c.x - a.x) + (c.z - a.z) * (c.z - a.z)));
            double between = Math.Min(0.45, 2.6 / len);   // a bay's edge from the corner, as the style's default bays fall
            switch (k.anchor)
            {
                case Details.Anchor.Opening: b.details.Add(new DetailData { kind = k.id, k = 0, edge = 0, t = 0.5 }); break;
                case Details.Anchor.Tier: b.details.Add(new DetailData { kind = k.id, k = 1, edge = 0, t = between }); break;
                default:
                    for (int f = 0; f < b.floors.Count; f++)
                        foreach (var t in new[] { between, 1 - between }) b.details.Add(new DetailData { kind = k.id, k = f, edge = 0, t = t, y = 1.0 });
                    break;
            }
        }

        /// <summary>A window, door or detail the generator knows: a new one registers when it bakes, and may not have yet.</summary>
        static void Register(LibraryItem i)
        {
            if (i.Opening is StoreyOpening o && OpeningKinds.Get(o.Id) == null && (o.prefab != null || o.mesh != null)) o.Register();
            if (i.Detail is StoreyDetail d && !d.BuiltIn && DetailKinds.Get(d.Id) == null) d.Register();
        }

        // ---- making, copying, renaming and deleting ----

        /// <summary>Where the project keeps its own of a kind: the folder most of them are in, or Assets.</summary>
        public static string Folder(LibraryKind k)
        {
            var dirs = Of(k).Where(i => !i.package).Select(i => Path.GetDirectoryName(i.path)?.Replace('\\', '/') ?? "").Where(x => x.StartsWith("Assets", StringComparison.Ordinal)).ToList();
            return dirs.Count == 0 ? "Assets" : dirs.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
        }

        /// <summary>
        /// A copy of an item, the project's own: beside it, or for one of the package's, where the project keeps its own of
        /// that kind (asked first). A copied window, door or detail gets an id of its own (<see cref="StoreyAssetIds"/>), and a
        /// copied style is named after its file. Returns the copy, or null.
        /// </summary>
        public static UnityEngine.Object? Duplicate(LibraryItem i)
        {
            string to;
            if (i.package)
            {
                to = EditorUtility.SaveFilePanelInProject("Duplicate " + One(i.kind), i.asset.name, "asset", "Where your copy goes: it's the project's own, to change as you like", Folder(i.kind));
                if (string.IsNullOrEmpty(to)) return null;
            }
            else to = AssetDatabase.GenerateUniqueAssetPath(i.path);
            if (!AssetDatabase.CopyAsset(i.path, to)) return null;
            var copy = AssetDatabase.LoadMainAssetAtPath(to);
            if (copy is FacadeStylePreset p)
            {
                p.key = "";   // the project's, known by its own GUID
                try { p.Set(p.Style, Path.GetFileNameWithoutExtension(to)); } catch (Exception) { }
                EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
            }
            Changed();
            StoreyOpenings.All(); StoreyDetails.All();   // a copy takes an id of its own
            return copy;
        }

        /// <summary>Rename an item's asset (a style's own name with it). Returns why it couldn't, or null.</summary>
        public static string? Rename(LibraryItem i, string name)
        {
            name = name.Trim();
            if (name.Length == 0 || name == i.asset.name) return null;
            string why = AssetDatabase.RenameAsset(i.path, name);
            if (!string.IsNullOrEmpty(why)) return why;
            if (i.asset is FacadeStylePreset p)
            {
                try { p.Set(p.Style, name); } catch (Exception) { }
                EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
            }
            Changed();
            return null;
        }

        /// <summary>Move an item's asset to the system's trash (it can be got back from there).</summary>
        public static bool Delete(LibraryItem i)
        {
            bool ok = AssetDatabase.MoveAssetToTrash(i.path);
            Changed();
            return ok;
        }

        /// <summary>A new Facade Style asset holding <paramref name="from"/>, named after its file, where the person says. Returns it, or null.</summary>
        public static FacadeStylePreset? NewStyle(FacadeStyle from, string name)
        {
            string path = EditorUtility.SaveFilePanelInProject("New facade style", name, "asset", "Where the new style goes", Folder(LibraryKind.Styles));
            if (string.IsNullOrEmpty(path)) return null;
            var p = ScriptableObject.CreateInstance<FacadeStylePreset>();
            p.Set(from, Path.GetFileNameWithoutExtension(path));
            AssetDatabase.CreateAsset(p, path);
            AssetDatabase.SaveAssets();
            Changed();
            return p;
        }

        /// <summary>Windows, doors or details made from prefabs, each beside its prefab (<see cref="StoreyOpenings.Make"/>, <see cref="StoreyDetailBake.Make"/>).</summary>
        public static List<UnityEngine.Object> FromPrefabs(LibraryKind k, IEnumerable<GameObject> prefabs)
        {
            var list = prefabs.Where(p => p != null && AssetDatabase.Contains(p)).ToList();
            var made = k == LibraryKind.Details ? StoreyDetailBake.Make(list).Cast<UnityEngine.Object>().ToList()
                : StoreyOpenings.Make(list, k == LibraryKind.Doors).Cast<UnityEngine.Object>().ToList();
            Changed();
            return made;
        }

        /// <summary>An empty window, door or detail, to give a prefab or a mesh in its inspector, where the person says. Returns it, or null.</summary>
        public static UnityEngine.Object? NewEmpty(LibraryKind k)
        {
            string path = EditorUtility.SaveFilePanelInProject("New " + One(k), k == LibraryKind.Details ? "Facade Detail" : k == LibraryKind.Doors ? "Door" : "Window", "asset", "Where it goes", Folder(k));
            if (string.IsNullOrEmpty(path)) return null;
            ScriptableObject o;
            if (k == LibraryKind.Details) o = ScriptableObject.CreateInstance<StoreyDetail>();
            else { var w = ScriptableObject.CreateInstance<StoreyOpening>(); w.door = k == LibraryKind.Doors; o = w; }
            AssetDatabase.CreateAsset(o, path);
            AssetDatabase.SaveAssets();
            Changed();
            StoreyOpenings.All(); StoreyDetails.All();   // it takes its asset's GUID as its id
            return o;
        }

        /// <summary>A building kept as a template, a new asset or over one picked, as the building bar's Save as template does. Returns it, or null.</summary>
        public static StoreyBuildingTemplate? TemplateFrom(BuildingData b)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save building template", b.name, "asset", "Where to keep the template", Folder(LibraryKind.Templates));
            if (string.IsNullOrEmpty(path)) return null;
            var t = AssetDatabase.LoadAssetAtPath<StoreyBuildingTemplate>(path);
            if (t == null)
            {
                t = ScriptableObject.CreateInstance<StoreyBuildingTemplate>(); t.Set(b);
                AssetDatabase.CreateAsset(t, path);
            }
            else { Undo.RecordObject(t, "Save building template"); t.Set(b); EditorUtility.SetDirty(t); }
            AssetDatabase.SaveAssets();
            Changed();
            return t;
        }
    }

    /// <summary>
    /// Where each item of the Storey Library is used (docs/EDITOR.md §6.13): every layout in the project, as it stands (an
    /// open edit's unsaved state included), read for the styles applied from each preset, the artists' windows and doors its
    /// styles pick or its doors name, and the facade details its styles give a chance or its walls have by hand. Looked at
    /// again every couple of seconds while asked, so an edit shows soon after.
    /// </summary>
    internal static class StoreyUsage
    {
        /// <summary>A layout that uses an item: its name and path, a site in the open scenes drawing it (or none), and its buildings that do.</summary>
        public sealed class Layout
        {
            public string name = "", path = "";
            public StoreySite? site;
            public readonly List<(string id, string name)> buildings = new List<(string, string)>();
        }

        static readonly Dictionary<string, List<Layout>> index = new Dictionary<string, List<Layout>>(StringComparer.Ordinal);
        static string[]? layouts;
        static double readAt = -100;
        static bool hooked;
        static readonly List<Layout> None = new List<Layout>();

        public static void Changed() { layouts = null; readAt = -100; }

        public static IReadOnlyList<Layout> Of(LibraryItem i)
        {
            Fresh();
            string? key = Key(i);
            var byKey = key != null && index.TryGetValue(key, out var l) ? l : None;
            // a style's: the buildings applied from it, and those styled exactly as it is, its roof aside (a building converted
            // from the old generator's themes was given a copy of its theme's style, which names no preset)
            if (i.kind != LibraryKind.Styles || !(LookOf(i) is string look) || !index.TryGetValue("l:" + look, out var alike)) return byKey;
            if (byKey.Count == 0) return alike;
            var both = byKey.Select(x => Copy(x)).ToList();
            foreach (var a in alike)
            {
                var into = both.FirstOrDefault(x => x.path == a.path);
                if (into == null) both.Add(into = Copy(a));
                else foreach (var b in a.buildings) if (!into.buildings.Exists(x => x.id == b.id)) into.buildings.Add(b);
            }
            return both;
        }

        static Layout Copy(Layout l) { var c = new Layout { name = l.name, path = l.path, site = l.site }; c.buildings.AddRange(l.buildings); return c; }

        /// <summary>A preset's look (<see cref="Styles.Look"/>), hashed; worked out again only when its text changes.</summary>
        static string? LookOf(LibraryItem i)
        {
            var p = i.Style; if (p == null) return null;
            if (looks.TryGetValue(i.guid, out var kept) && ReferenceEquals(kept.from, p.style)) return kept.look;
            string? look;
            try { look = Rendered(p.Style); } catch (Exception) { look = null; }
            looks[i.guid] = (p.style, look);
            return look;
        }
        static readonly Dictionary<string, (string from, string? look)> looks = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);

        /// <summary>
        /// A style's look as it renders, hashed: <see cref="Styles.Look"/> with each colour written as the palette entry it is
        /// drawn with (<see cref="StoreyColorField.Describe"/>), as a layout keeps them (every edit ends inside the palette)
        /// while a preset may keep CSS colours: the same look either way.
        /// </summary>
        static string Rendered(FacadeStyle s)
        {
            var c = s.Clone();
            foreach (var f in StyleColorFields.All)
                if (f.get(c) is string v && StoreyColorField.Describe(v) is (Color, string name)) f.set(c, name);
            return StoreyLibrary.Hash(Styles.Look(c));
        }

        /// <summary>How many buildings use an item, in every layout.</summary>
        public static int Buildings(LibraryItem i) => Of(i).Sum(l => l.buildings.Count);

        static string? Key(LibraryItem i) => i.kind switch
        {
            LibraryKind.Styles => "s:" + i.key,
            LibraryKind.Windows or LibraryKind.Doors => "k:" + i.key,
            LibraryKind.Details => "d:" + i.key,
            _ => null,
        };

        static void Fresh()
        {
            if (!hooked) { hooked = true; EditorApplication.projectChanged += Changed; }
            if (EditorApplication.timeSinceStartup - readAt < 2) return;
            readAt = EditorApplication.timeSinceStartup;
            index.Clear();
            layouts ??= AssetDatabase.FindAssets("t:" + nameof(StoreyDocumentAsset)).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();
            foreach (var path in layouts)
            {
                var a = AssetDatabase.LoadAssetAtPath<StoreyDocumentAsset>(path);
                if (a == null) continue;
                StoreySite? site = null;
                foreach (var s in StoreySite.All) if (s != null && s.layout == a) { site = s; break; }
                StoreyDocument doc;
                try { doc = (site != null ? StoreyEdit.Of(site)?.Document : null) ?? a.Document; }
                catch (Exception) { continue; }   // a layout that doesn't read: its site says why
                foreach (var b in doc.buildings)
                    foreach (var key in Keys(b))
                    {
                        if (!index.TryGetValue(key, out var list)) index[key] = list = new List<Layout>();
                        var l = list.FirstOrDefault(x => x.path == path);
                        if (l == null) list.Add(l = new Layout { name = a.name, path = path, site = site });
                        if (!l.buildings.Exists(x => x.id == b.id)) l.buildings.Add((b.id, b.name));
                    }
            }
        }

        /// <summary>Everything a building uses: its styles' presets and looks, its windows and doors, its details.</summary>
        static HashSet<string> Keys(BuildingData b)
        {
            var o = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in new[] { b.style }.Concat(b.floors.Select(f => f.style)))
            {
                if (s == null) continue;
                if (s.preset != null) o.Add("s:" + s.preset);
                o.Add("l:" + Rendered(s));
                if (s.windowKind != null) o.Add("k:" + s.windowKind);
                if (s.doorKind != null) o.Add("k:" + s.doorKind);
                foreach (var set in new[] { s.windowSet, s.groundSet, s.doorSet })
                    if (set != null) foreach (var en in set) if (en.kind != null && !en.none) o.Add("k:" + en.kind);
                if (s.details?.kinds != null) foreach (var kv in s.details.kinds) if (kv.Value > 0) o.Add("d:" + kv.Key);
                var (ac, vents) = Details.Rules(s);
                if (ac > 0) o.Add("d:" + DetailKinds.Ac);
                if (vents > 0) o.Add("d:" + DetailKinds.Vent);
            }
            foreach (var en in b.entrances) if (en.door != null && en.door != EntranceData.Plain) o.Add("k:" + en.door);
            foreach (var d in b.details) o.Add("d:" + d.kind);
            return o;
        }
    }

    /// <summary>
    /// The Storey Library's cards' pictures (docs/EDITOR.md §6.13): each item drawn on its placeholder building by a preview
    /// of their own, a few at a time while the window is open, the cards in view first; kept in memory and in the project's
    /// Library folder, so the window opens with them; drawn again when the item changes.
    /// </summary>
    internal sealed class StoreyThumbnails : IDisposable
    {
        const int Size = 256;
        // the pictures' look: bump it when the placeholder or the framing changes, and every picture is drawn again
        const string Version = "3";
        static string Folder => Path.Combine("Library", "StoreyLibrary", "Thumbnails");

        readonly StoreyPreview renderer = new StoreyPreview();
        readonly Dictionary<string, (string stamp, Texture2D tex)> made = new Dictionary<string, (string, Texture2D)>(StringComparer.Ordinal);
        readonly List<LibraryItem> wanted = new List<LibraryItem>();
        readonly HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>How many pictures are waiting to be drawn.</summary>
        public int Waiting => wanted.Count;

        /// <summary>An item's picture: drawn already, or read from the Library folder; otherwise null (or its old one) and asked for.</summary>
        public Texture2D? Get(LibraryItem i)
        {
            string stamp = Stamp(i);
            made.TryGetValue(i.guid, out var m);
            if (m.tex != null && m.stamp == stamp) return m.tex;
            if (failed.Contains(i.guid + stamp)) return m.tex;
            string file = FileOf(i, stamp);
            if (File.Exists(file))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (tex.LoadImage(File.ReadAllBytes(file)))
                {
                    if (m.tex != null) UnityEngine.Object.DestroyImmediate(m.tex);
                    made[i.guid] = (stamp, tex);
                    return tex;
                }
                UnityEngine.Object.DestroyImmediate(tex);
            }
            if (!wanted.Contains(i)) wanted.Add(i);
            return m.tex;
        }

        /// <summary>Draw waiting pictures for up to <paramref name="ms"/> milliseconds. Returns whether any were drawn.</summary>
        public bool Work(double ms)
        {
            if (wanted.Count == 0) return false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool any = false;
            while (wanted.Count > 0 && sw.Elapsed.TotalMilliseconds < ms)
            {
                var i = wanted[wanted.Count - 1]; wanted.RemoveAt(wanted.Count - 1);   // the last asked for: the cards drawn last are in view
                if (!i.Alive) continue;
                string stamp = Stamp(i);
                if (made.TryGetValue(i.guid, out var m) && m.stamp == stamp && m.tex != null) continue;
                Texture2D? tex = null;
                try
                {
                    var (doc, framing) = Placeholder(i);
                    renderer.Show(doc, i.guid + stamp, framing);
                    renderer.Reframe();
                    tex = renderer.Still(Size, Size);
                }
                catch (Exception) { tex = null; }
                if (tex == null) { failed.Add(i.guid + stamp); continue; }
                if (m.tex != null) UnityEngine.Object.DestroyImmediate(m.tex);
                made[i.guid] = (stamp, tex);
                any = true;
                try
                {
                    Directory.CreateDirectory(Folder);
                    foreach (var old in Directory.GetFiles(Folder, i.guid + "-*.png")) File.Delete(old);
                    File.WriteAllBytes(FileOf(i, stamp), tex.EncodeToPNG());
                }
                catch (Exception) { }   // kept in memory all the same
            }
            renderer.Clear();
            return any;
        }

        /// <summary>The placeholder a picture is drawn on: the same for everyone, whatever the window's preview shows.</summary>
        static (StoreyDocument doc, StoreyPreview.Framing framing) Placeholder(LibraryItem i) => i.kind switch
        {
            LibraryKind.Styles => StoreyLibrary.Placeholder(i, "L", 4, null),
            LibraryKind.Templates => StoreyLibrary.Placeholder(i, "", 0, null),
            LibraryKind.Details => StoreyLibrary.Placeholder(i, "rect", 3, StoreyLibrary.ShowOn(StoreyLibraryWindow.ShowOnKey).style),
            _ => StoreyLibrary.Placeholder(i, "rect", 3, StoreyLibrary.ShowOn(StoreyLibraryWindow.ShowOnKey).style),
        };

        /// <summary>What a picture is kept against: the item, the style a window, door or detail is shown on, and the pictures' look.</summary>
        static string Stamp(LibraryItem i)
        {
            string s = StoreyLibrary.Stamp(i) + "." + Version + (EditorGUIUtility.isProSkin ? "d" : "l");
            if (i.kind is LibraryKind.Windows or LibraryKind.Doors or LibraryKind.Details) s += "." + StoreyLibrary.ShowOn(StoreyLibraryWindow.ShowOnKey).stamp;
            return s;
        }

        static string FileOf(LibraryItem i, string stamp) => Path.Combine(Folder, i.guid + "-" + StoreyLibrary.Hash(stamp) + ".png");

        /// <summary>Every picture drawn again: after a material or the palette changed.</summary>
        public void Forget()
        {
            foreach (var m in made.Values) if (m.tex != null) UnityEngine.Object.DestroyImmediate(m.tex);
            made.Clear(); wanted.Clear(); failed.Clear();
            try { if (Directory.Exists(Folder)) foreach (var f in Directory.GetFiles(Folder, "*.png")) File.Delete(f); } catch (Exception) { }
        }

        public void Dispose()
        {
            renderer.Dispose();
            foreach (var m in made.Values) if (m.tex != null) UnityEngine.Object.DestroyImmediate(m.tex);
            made.Clear(); wanted.Clear();
        }
    }
}
