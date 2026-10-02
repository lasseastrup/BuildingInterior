#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Triband.Storey
{
    /// <summary>A palette entry as matching needs it: its id, name and colour (sRGB 0..1, as Color Pipeline stores it).</summary>
    public readonly struct PaletteColor
    {
        public readonly string id, name;
        public readonly float r, g, b;
        public PaletteColor(string id, string name, float r, float g, float b) { this.id = id; this.name = name; this.r = r; this.g = g; this.b = b; }
        public string Hex => "#" + string.Concat(new[] { r, g, b }.Select(c => ((int)Math.Round(Math.Max(0, Math.Min(1, c)) * 255)).ToString("X2")));
    }

    /// <summary>
    /// The palette entry nearest a colour, as Color Pipeline's Model Remapper picks it (docs/COLOURS.md §3.8): CIE76 ΔE
    /// in L*a*b* (D65, 2°) from the colour floored to 8 bits, rounded to a whole number (<c>ColorFormulas.CompareTo</c>),
    /// smallest first, the first palette entry winning a tie (<c>PerformAutoRemap</c>). Engine-free, so the editor and
    /// the runtime bridge match colours the same way.
    /// </summary>
    public static class PaletteMatch
    {
        /// <summary>ColorEditorUtility's <c>Mathf.FloorToInt(c * 255)</c>, in single precision as Unity computes it.</summary>
        public static int Byte8(float c) => (int)Math.Floor(c * 255f);

        /// <summary>ColorFormulas: sRGB 0..255 to CIE L*a*b*.</summary>
        public static (double L, double A, double B) Lab(int r8, int g8, int b8)
        {
            static double Lin(int v)
            {
                double c = v / 255.0;
                return c > 0.04045 ? Math.Pow((c + 0.055) / 1.055, 2.4) : c / 12.92;
            }
            double R = Lin(r8) * 100, G = Lin(g8) * 100, B = Lin(b8) * 100;
            double X = R * 0.4124 + G * 0.3576 + B * 0.1805, Y = R * 0.2126 + G * 0.7152 + B * 0.0722, Z = R * 0.0193 + G * 0.1192 + B * 0.9505;
            static double F(double t) => t > 0.008856 ? Math.Pow(t, 1 / 3.0) : 7.787 * t + 16 / 116.0;
            double fx = F(X / 95.047), fy = F(Y / 100.000), fz = F(Z / 108.883);
            return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
        }

        /// <summary>ColorFormulas.CompareTo: ΔE rounded to a whole number (C#'s rounding, halves to even).</summary>
        public static int Distance((double L, double A, double B) p, (double L, double A, double B) q) =>
            (int)Math.Round(Math.Sqrt(Math.Pow(p.L - q.L, 2) + Math.Pow(p.A - q.A, 2) + Math.Pow(p.B - q.B, 2)));

        /// <summary>GetColorSimilarity: 1 − ΔE / 255, as the Remapper shows it.</summary>
        public static double Similarity(int distance) => Math.Max(0, Math.Min(1, 1 - distance / 255.0));

        static (double, double, double) LabOf(float r, float g, float b) => Lab(Byte8(r), Byte8(g), Byte8(b));

        /// <summary>A CSS colour as Unity parses it (<c>ColorUtility.TryParseHtmlString</c>: each byte / 255 in single precision).</summary>
        public static (float r, float g, float b) Parse(string hex)
        {
            string h = ColorRef.Normalise(hex);
            float C(int at) => Convert.ToInt32(h.Substring(at, 2), 16) / 255f;
            return (C(1), C(3), C(5));
        }

        /// <summary>The nearest entry to a colour and how far it is (ΔE, whole); null for an empty palette.</summary>
        public static (PaletteColor entry, int distance)? Nearest(float r, float g, float b, IReadOnlyList<PaletteColor> palette)
        {
            var p = LabOf(r, g, b);
            int best = -1, bd = int.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                int d = Distance(p, LabOf(palette[i].r, palette[i].g, palette[i].b));
                if (d < bd) { bd = d; best = i; }
            }
            return best < 0 ? null : (palette[best], bd);
        }

        public static (PaletteColor entry, int distance)? Nearest(string hex, IReadOnlyList<PaletteColor> palette)
        {
            var (r, g, b) = Parse(hex);
            return Nearest(r, g, b, palette);
        }
    }

    /// <summary>One colour the conforming changed, for the log: what it was, and the entry it became.</summary>
    public readonly struct ConformedColor
    {
        public readonly string from, id, name;
        public readonly int distance, uses;
        public ConformedColor(string from, string id, string name, int distance, int uses) { this.from = from; this.id = id; this.name = name; this.distance = distance; this.uses = uses; }
    }

    /// <summary>
    /// Keep a layout inside the palette: every CSS colour in its styles, and every palette id the palette no longer has
    /// (by the colour its palette block last saw), becomes the nearest palette entry. The palette block is refreshed.
    /// </summary>
    public static class PaletteConform
    {
        public static List<ConformedColor> Apply(StoreyDocument d, IReadOnlyList<PaletteColor> palette)
        {
            var changed = new List<ConformedColor>();
            if (palette.Count == 0) return changed;
            var known = new HashSet<string>(palette.Select(p => p.id), StringComparer.Ordinal);
            var map = new Dictionary<string, (PaletteColor e, int d)>(StringComparer.Ordinal);
            var uses = new Dictionary<string, int>(StringComparer.Ordinal);
            (PaletteColor, int)? Target(string v)
            {
                if (map.TryGetValue(v, out var m)) return m;
                string? hex = ColorRef.IsHex(v) ? v : ColorRef.IsPaletteId(v) && !known.Contains(v) && d.palette.TryGetValue(v, out var pe) ? pe.hex : null;
                if (hex == null) return null;
                var n = PaletteMatch.Nearest(hex, palette);
                if (n == null) return null;
                map[v] = n.Value; return n.Value;
            }
            foreach (var st in StyleColorFields.Styles(d))
                foreach (var f in StyleColorFields.All)
                {
                    var v = f.get(st); if (v == null) continue;
                    var key = ColorRef.IsHex(v) ? ColorRef.Normalise(v) : v;
                    var t = Target(key); if (t == null) continue;
                    f.set(st, t.Value.Item1.id);
                    uses[key] = uses.TryGetValue(key, out int u) ? u + 1 : 1;
                }
            foreach (var kv in map.Where(kv => uses.ContainsKey(kv.Key)))
                changed.Add(new ConformedColor(kv.Key, kv.Value.e.id, kv.Value.e.name, kv.Value.d, uses[kv.Key]));
            var byId = palette.GroupBy(p => p.id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            PaletteSnapshot.Refresh(d, id => byId.TryGetValue(id, out var p) ? new PaletteEntry(p.name, p.Hex) : null);
            return changed;
        }
    }
}
