#nullable enable
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>
    /// Facade styles in the Facade tab: the five one-click presets, which style a tier edits, a setback's own style,
    /// and which style the roof is built with. Ported from the prototype.
    /// </summary>
    public static class Styles
    {
        static FacadeStyle S(string label, string wall, string trim, string interior, string floor, string roof, string core, string glass,
            WindowType windows, double winW, double bay, GroundType ground, bool bands, bool parapet) => new FacadeStyle
        {
            label = label, wall = wall, trim = trim, interior = interior, floor = floor, roof = roof, core = core, glass = glass,
            windows = windows, winW = winW, bay = bay, ground = ground, bands = bands, parapet = parapet,
        };

        /// <summary>The prototype's style presets, by key, in its order.</summary>
        public static readonly IReadOnlyList<(string key, FacadeStyle style)> Presets = new List<(string, FacadeStyle)>
        {
            ("brick", S("Brick walk-up", "#9A4B38", "#ECE5D8", "#EFECE5", "#B88D62", "#6E716B", "#C9C4BB", "#8DB3C8", WindowType.Punched, 1.2, 2.6, GroundType.Storefront, true, true)),
            ("glass", S("Glass office", "#C3CCD0", "#33414A", "#F2F3F1", "#A2A7A8", "#7F888D", "#BFC4C4", "#79A9C4", WindowType.Curtain, 1.4, 1.8, GroundType.Match, true, true)),
            ("stucco", S("Pastel stucco", "#E4B59B", "#FBF4E8", "#F6F1E9", "#C49A6C", "#A7725D", "#D8CFC2", "#9CC1CF", WindowType.Tall, 1.0, 2.4, GroundType.Storefront, true, true)),
            ("concrete", S("Concrete slab", "#B8B4AB", "#7F7B73", "#ECEBE7", "#9C9890", "#8C8E89", "#C4C1BA", "#88A7B5", WindowType.Ribbon, 1.2, 3.0, GroundType.Match, false, true)),
            ("warehouse", S("Warehouse", "#56645F", "#E0DBCF", "#E6E3DC", "#8F8B82", "#4E5552", "#BDB9B0", "#9BB4BE", WindowType.Tall, 1.6, 4.2, GroundType.Solid, false, false)),
        };

        /// <summary>A copy of a preset, marked with its key.</summary>
        public static FacadeStyle Preset(string key)
        {
            var s = Presets.First(p => p.key == key).style.Clone();
            s.preset = key;
            return s;
        }

        /// <summary>The prototype's suggested swatches per colour field, shown first in the picker (Color Pipeline's own subsets replace them).</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Suggested = new Dictionary<string, string[]>
        {
            ["wall"] = new[] { "#9A4B38", "#E4B59B", "#EAD9B8", "#B8B4AB", "#56645F", "#C3CCD0", "#6F8F7A" },
            ["trim"] = new[] { "#ECE5D8", "#FBF4E8", "#33414A", "#7F7B73", "#1F2622", "#C9A36A" },
            ["roof"] = new[] { "#6E716B", "#4E5552", "#A7725D", "#8C8E89", "#7F888D" },
            ["interior"] = new[] { "#EFECE5", "#F6F1E9", "#E6E3DC", "#DDE6E4", "#F2E3D3" },
            ["floor"] = new[] { "#B88D62", "#C49A6C", "#9C9890", "#A2A7A8", "#7B6A58", "#D9CFBF" },
        };

        /// <summary>
        /// The style the Facade tab edits for tier k0: the setback's own when it has one, otherwise the building's
        /// (a setback without one shows "Give it its own style" instead of the fields).
        /// </summary>
        public static FacadeStyle Edited(BuildingData b, int k0) => k0 > 0 && b.floors[k0].style != null ? b.floors[k0].style! : b.style;

        /// <summary>Whether tier k0 has a style of its own (the base always has).</summary>
        public static bool HasOwn(BuildingData b, int k0) => k0 == 0 || b.floors[k0].style != null;

        /// <summary>Apply a preset to the style tier k0 edits, keeping its roof (presets never change the roof).</summary>
        public static void ApplyPreset(BuildingData b, int k0, string key) => Apply(b, k0, Preset(key));

        /// <summary>Apply a style (a project's preset asset, say) to the style tier k0 edits, keeping its roof.</summary>
        public static void Apply(BuildingData b, int k0, FacadeStyle style)
        {
            var cur = Edited(b, k0);
            var ns = style.Clone(); ns.roofType = cur.roofType; ns.pitch = cur.pitch; ns.eave = cur.eave; ns.mansard = cur.mansard; ns.dormers = cur.dormers;
            if (k0 > 0 && b.floors[k0].style != null) b.floors[k0].style = ns; else b.style = ns;
        }

        /// <summary>Give the setback at k0 its own style, starting as what it looks like now.</summary>
        public static void GiveOwn(BuildingData b, int k0) => b.floors[k0].style = Derived.StyleAt(b, k0 - 1).Clone();

        /// <summary>The setback at k0 follows the floors below again.</summary>
        public static void MatchBelow(BuildingData b, int k0) => b.floors[k0].style = null;

        /// <summary>Whether tier k0's style is the one the roof is built with (only that one shows the roof fields).</summary>
        public static bool DrivesRoof(BuildingData b, int k0)
        {
            int j0 = Derived.TierStart(b, b.floors.Count);
            while (j0 > 0 && b.floors[j0].style == null) j0 = Derived.TierStart(b, j0 - 1);
            bool own = k0 > 0 && b.floors[k0].style != null;
            return k0 == j0 || (!own && k0 > 0 && j0 == 0);
        }
    }
}
