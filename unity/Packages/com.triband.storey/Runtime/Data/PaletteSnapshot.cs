#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey
{
    /// <summary>
    /// Every colour field of a style by name, read and written alike: the seven a style always has and the
    /// ten optional ones (null = the project default).
    /// </summary>
    public static class StyleColorFields
    {
        public static readonly (string name, Func<FacadeStyle, string?> get, Action<FacadeStyle, string> set)[] All =
        {
            ("wall", s => s.wall, (s, v) => s.wall = v), ("trim", s => s.trim, (s, v) => s.trim = v),
            ("interior", s => s.interior, (s, v) => s.interior = v), ("floor", s => s.floor, (s, v) => s.floor = v),
            ("roof", s => s.roof, (s, v) => s.roof = v), ("core", s => s.core, (s, v) => s.core = v), ("glass", s => s.glass, (s, v) => s.glass = v),
            ("door", s => s.door, (s, v) => s.door = v), ("rail", s => s.rail, (s, v) => s.rail = v), ("metal", s => s.metal, (s, v) => s.metal = v),
            ("ceiling", s => s.ceiling, (s, v) => s.ceiling = v), ("liftInterior", s => s.liftInterior, (s, v) => s.liftInterior = v),
            ("liftButton", s => s.liftButton, (s, v) => s.liftButton = v), ("detailMetal", s => s.detailMetal, (s, v) => s.detailMetal = v),
            ("grille", s => s.grille, (s, v) => s.grille = v), ("detailDark", s => s.detailDark, (s, v) => s.detailDark = v),
            ("dish", s => s.dish, (s, v) => s.dish = v),
        };

        /// <summary>Every style of a layout: each building's and each setback's own.</summary>
        public static IEnumerable<FacadeStyle> Styles(StoreyDocument d)
        {
            foreach (var b in d.buildings)
            {
                yield return b.style;
                foreach (var f in b.floors) if (f.style != null) yield return f.style;
            }
        }

        /// <summary>Every colour reference a layout's styles hold.</summary>
        public static IEnumerable<string> Values(StoreyDocument d)
        {
            foreach (var st in Styles(d))
                foreach (var f in All) { var v = f.get(st); if (v != null) yield return v; }
        }
    }

    /// <summary>
    /// The palette block of a layout (docs/COLOURS.md §3.1): what each palette id its styles use looked like when it
    /// was saved, so the prototype and headless tools can show palette colours without Color Pipeline.
    /// </summary>
    public static class PaletteSnapshot
    {
        /// <summary>The palette ids a layout's styles use.</summary>
        public static SortedSet<string> UsedIds(StoreyDocument d)
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var v in StyleColorFields.Values(d)) if (ColorRef.IsPaletteId(v)) ids.Add(v);
            return ids;
        }

        /// <summary>
        /// Rewrite the block before a save: one entry per id in use, with the palette's current name and colour. An id
        /// the palette no longer knows (<paramref name="lookup"/> returns null) keeps its last entry; unused ones go.
        /// </summary>
        public static void Refresh(StoreyDocument d, Func<string, PaletteEntry?> lookup)
        {
            var old = d.palette;
            d.palette = new SortedDictionary<string, PaletteEntry>(StringComparer.Ordinal);
            foreach (var id in UsedIds(d))
            {
                var e = lookup(id) ?? (old.TryGetValue(id, out var kept) ? kept : null);
                if (e != null) d.palette[id] = new PaletteEntry(e.name, ColorRef.Normalise(e.hex));
            }
        }

        /// <summary>Palette ids to linear RGB through the block, for <see cref="Generate.ColorResolver"/>; magenta when absent, as in the prototype.</summary>
        public static Func<string, Generate.Rgb> Resolver(StoreyDocument d) =>
            id => Generate.Colors.Col(d.palette.TryGetValue(id, out var e) ? e.hex : "#FF00FF");
    }

    /// <summary>
    /// The engine-free half of <i>Map colours to palette…</i> (docs/COLOURS.md §3.8): which CSS literals a layout
    /// still holds, which match a palette entry exactly, and rewriting them as palette ids. The editor window adds
    /// the similarity of near matches (Color Pipeline's) and the choices.
    /// </summary>
    public static class ColorMapping
    {
        /// <summary>The CSS literals in a layout's styles, normalised, with how many fields use each.</summary>
        public static SortedDictionary<string, int> Literals(StoreyDocument d)
        {
            var r = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var v in StyleColorFields.Values(d))
            {
                if (!ColorRef.IsHex(v)) continue;
                string h = ColorRef.Normalise(v);
                r[h] = r.TryGetValue(h, out int n) ? n + 1 : 1;
            }
            return r;
        }

        /// <summary>
        /// Literals whose colour a palette entry has exactly (the Model Remapper's 100 % match, accepted without
        /// asking). Palette colours are given as CSS literals; the first entry with a colour wins.
        /// </summary>
        public static Dictionary<string, string> ExactMatches(IEnumerable<string> literals, IEnumerable<(string id, string hex)> palette)
        {
            var byHex = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (id, hex) in palette) { string h = ColorRef.Normalise(hex); if (!byHex.ContainsKey(h)) byHex[h] = id; }
            var r = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var l in literals) { string h = ColorRef.Normalise(l); if (byHex.TryGetValue(h, out var id)) r[h] = id; }
            return r;
        }

        /// <summary>
        /// Rewrite every field holding a mapped literal as its palette id and refresh the palette block. Returns how
        /// many fields changed. Ids in <paramref name="idByHex"/> must be palette ids; keys are CSS literals.
        /// </summary>
        public static int Apply(StoreyDocument d, IReadOnlyDictionary<string, string> idByHex, Func<string, PaletteEntry?> lookup)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in idByHex)
            {
                if (!ColorRef.IsPaletteId(kv.Value)) throw new ArgumentException($"{kv.Value} is not a palette id", nameof(idByHex));
                map[ColorRef.Normalise(kv.Key)] = kv.Value;
            }
            int n = 0;
            foreach (var st in StyleColorFields.Styles(d))
                foreach (var f in StyleColorFields.All)
                {
                    var v = f.get(st);
                    if (v != null && ColorRef.IsHex(v) && map.TryGetValue(ColorRef.Normalise(v), out var id)) { f.set(st, id); n++; }
                }
            PaletteSnapshot.Refresh(d, lookup);
            return n;
        }
    }
}
