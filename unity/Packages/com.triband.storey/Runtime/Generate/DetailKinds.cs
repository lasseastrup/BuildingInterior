#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// A kind of facade detail (SPEC §4.4, docs/EDITOR.md §6.12): one of the generator's own five, which code draws, or
    /// one an artist made, a mesh in parts, each part in one of the style's colours. An artist's mesh is the detail's
    /// own: x along the wall (centred on the pivot), y up, z out of the wall (0 at the wall's outer face), in metres,
    /// with front faces wound as the generator's. Where the pivot goes depends on the <see cref="anchor"/>: on a wall,
    /// where it was clicked; up the wall, at each storey's floor; over an opening, at its middle,
    /// <see cref="Details.OverLift"/> above its top.
    /// </summary>
    public sealed class DetailKind
    {
        /// <summary>A part of an artist's mesh: its triangles (into <see cref="Mesh.P"/>) and its colour.</summary>
        public sealed class Part
        {
            public ColorSlot slot;
            /// <summary>A palette colour it keeps whatever the style (a palette id or a CSS colour), in place of <see cref="slot"/>; null takes the style's.</summary>
            public string? own;
            // own's slot (KindColors), found when the kind is registered
            internal ColorSlot? ownSlot;
            public int[] tris = Array.Empty<int>();
            // made ready once (docs/PERFORMANCE.md §8.1): when the kind is registered, or first placed
            internal KindPart? ready;
        }

        public sealed class Mesh
        {
            public P3[] P = Array.Empty<P3>(), N = Array.Empty<P3>();
            public List<Part> parts = new List<Part>();
        }

        /// <summary>The id a layout stores: the generator's own are "ac", "vent", "dish", "escape" and "awning"; an artist's is its asset's.</summary>
        public string id = "", name = "";
        public Details.Anchor anchor;
        /// <summary>Where a style's rules put it, at the density the style gives it; <see cref="Details.Rule.None"/>: by hand only.</summary>
        public Details.Rule rule;
        /// <summary>
        /// Its size in metres: across, up and out of the wall. It is what placement keeps clear, clamps and shows as the
        /// ghost. 0 across: as wide as the opening it is over; 0 up: as high as the storeys it climbs.
        /// </summary>
        public double w, h, d;
        /// <summary>How far up from its pivot an artist's mesh starts (negative: it hangs below the pivot); 0 for the generator's own.</summary>
        public double bottom;
        /// <summary>An artist's mesh is fitted across the opening it is over, or to each storey's height up the wall; otherwise it keeps its size.</summary>
        public bool stretch;
        /// <summary>An artist's mesh; null for the generator's own, which code draws.</summary>
        public Mesh? lod0;
        /// <summary>LOD1's mesh; null takes <see cref="lod0"/>.</summary>
        public Mesh? lod1;
        /// <summary>
        /// Left out of LOD1, whatever its meshes: for small details (brick patches) that LOD1's distance can't show, where
        /// many would cost the lean shell more than its walls do (docs/EDITOR.md §6.12). Storey's own.
        /// </summary>
        public bool noLod1;
        /// <summary>
        /// The colour most of its LOD0 triangles have (a part's own or its style colour), found when it is registered: what a
        /// style's colour for it replaces (<see cref="KindRule.color"/>), as the old generator recoloured a detail's most used
        /// palette colour. Null for the generator's own.
        /// </summary>
        public ColorSlot? main;
    }

    /// <summary>
    /// The facade details a layout can use, by id: the generator's own five, always there, and the artist-made ones (the
    /// project's Facade Detail assets register here). A placed detail of a kind that isn't registered isn't built, and is
    /// kept in the layout; a style's rule for one places nothing.
    /// </summary>
    public static class DetailKinds
    {
        /// <summary>The generator's own, by the ids layouts store (as the prototype writes them).</summary>
        public const string Ac = "ac", Vent = "vent", Dish = "dish", Escape = "escape", Awning = "awning";

        /// <summary>The generator's own five, in the order the Facade tab lists them.</summary>
        public static readonly IReadOnlyList<string> BuiltIn = new[] { Ac, Vent, Dish, Escape, Awning };

        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DetailKind> all = new System.Collections.Concurrent.ConcurrentDictionary<string, DetailKind>(StringComparer.Ordinal);   // read from the build threads too

        static DetailKinds()
        {
            foreach (var k in new[]
            {
                new DetailKind { id = Ac, name = "AC unit", w = 0.75, h = 0.55, d = 0.45, anchor = Details.Anchor.Wall, rule = Details.Rule.UnderWindows },
                new DetailKind { id = Vent, name = "Vent", w = 0.5, h = 0.5, d = 0.16, anchor = Details.Anchor.Wall, rule = Details.Rule.BareWall },
                new DetailKind { id = Dish, name = "Dish", w = 0.6, h = 0.6, d = 0.4, anchor = Details.Anchor.Wall },
                new DetailKind { id = Escape, name = "Fire escape", w = 2.6, h = 0, d = 1.1, anchor = Details.Anchor.Tier },
                new DetailKind { id = Awning, name = "Awning", w = 0, h = 0.5, d = 1.0, anchor = Details.Anchor.Opening, stretch = true },
            })
                all[k.id] = k;
        }

        /// <summary>Goes up with every change, so sites know to build again.</summary>
        public static int Version { get; private set; }

        /// <summary>One of the generator's own five: always registered, and never replaced.</summary>
        public static bool IsBuiltIn(string? id) => id == Ac || id == Vent || id == Dish || id == Escape || id == Awning;

        /// <summary>An artist's kind's id: 32 hex digits (the asset's GUID), as a style's rules key it.</summary>
        public static bool IsAssetId(string? id)
        {
            if (id == null || id.Length != 32) return false;
            foreach (char c in id) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        /// <summary>A layout's kind as the id it names: the generator's own in any case ("AC" is "ac"), any other as it is.</summary>
        public static string Id(string kind)
        {
            string lower = kind.ToLowerInvariant();
            return IsBuiltIn(lower) ? lower : kind;
        }

        /// <summary>
        /// Make an artist's kind available, its parts made ready for placing (a Facade Detail asset does, and sites do for the
        /// ones they use). A kind whose mesh changes is registered again.
        /// </summary>
        public static void Register(DetailKind k)
        {
            if (k.id.Length == 0 || IsBuiltIn(k.id)) return;
            foreach (var m in new[] { k.lod0, k.lod1 })
                if (m != null)
                    foreach (var part in m.parts)
                    {
                        part.ready = null;   // made again: its mesh may have changed
                        KindPart.Of(m, part);
                        part.ownSlot = part.own != null ? KindColors.SlotOf(part.own) : null;
                    }
            k.main = Main(k.lod0);
            all[k.id] = k; Version++;
        }
        // the colour of the most triangles, its parts' colours as placed; the first part's wins a tie
        static ColorSlot? Main(DetailKind.Mesh? m)
        {
            if (m == null) return null;
            ColorSlot? best = null; int most = 0;
            foreach (var part in m.parts)
            {
                var c = part.ownSlot ?? part.slot; int tris = 0;
                foreach (var q in m.parts) if ((q.ownSlot ?? q.slot) == c) tris += q.tris.Length / 3;
                if (tris > most) { most = tris; best = c; }
            }
            return best;
        }

        public static void Remove(string id) { if (!IsBuiltIn(id) && all.TryRemove(id, out _)) Version++; }
        public static DetailKind? Get(string? id) => id != null && all.TryGetValue(id, out var k) ? k : null;
        public static IEnumerable<DetailKind> All => all.Values;

        /// <summary>
        /// Put an artist's mesh on a wall: its pivot at <paramref name="u"/> along the wall and <paramref name="y0"/> up
        /// (site heights), on the wall's outer face; stretched by <paramref name="sx"/> across and <paramref name="sy"/>
        /// up. Each part takes its colour from the style; those of colour <paramref name="from"/> take <paramref name="to"/>
        /// instead (a style's colour for the kind's main colour).
        /// </summary>
        public static void Place(MeshBuilder op, Frame F, DetailKind.Mesh m, double u, double y0, double sx, double sy, Palette C, ColorSlot? from = null, ColorSlot? to = null)
        {
            if (op.ShapeOnly || m.P.Length == 0) return;
            var at = new KindPlace(F, u, y0, sx, sy);
            foreach (var whole in m.parts)
            {
                var part = KindPart.Of(m, whole);
                var c = whole.ownSlot ?? whole.slot;
                if (to.HasValue && c == from) c = to.Value;
                if (part.Tris > 0) part.Put(op, at, new Swatch(C.style, c));
            }
        }
    }
}
