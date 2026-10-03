#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Triband.Storey.Generate
{
    /// <summary>Colours as the prototype computes them: CSS hex, converted from sRGB to linear.</summary>
    public static class Colors
    {
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Rgb> cache = new System.Collections.Concurrent.ConcurrentDictionary<string, Rgb>(StringComparer.Ordinal);   // read from the build threads too

        static double ToLinear(double c) => c < 0.04045 ? c * 0.0773993808 : Math.Pow(c * 0.9478672986 + 0.0521327014, 2.4);

        public static Rgb Col(string hex)
        {
            if (cache.TryGetValue(hex, out var v)) return v;
            if (!ColorRef.IsHex(hex)) throw new FormatException($"\"{hex}\" is not a CSS colour; palette ids resolve through a palette (ColorResolver)");
            string h = hex.TrimStart('#');
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            int n = int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            v = new Rgb(ToLinear(((n >> 16) & 255) / 255.0), ToLinear(((n >> 8) & 255) / 255.0), ToLinear((n & 255) / 255.0));
            cache[hex] = v;
            return v;
        }

        public static Rgb Shade(string hex, double f) => Col(hex).Shade(f);
    }

    /// <summary>
    /// The colours a storey is built with (the prototype's <c>colorsOf</c>), as swatches of one style:
    /// what each surface is coloured with, not the colour itself (docs/COLOURS.md §3.2).
    /// </summary>
    public sealed class Palette
    {
        public readonly StyleRef style;
        public readonly Swatch wall, trim, inner, core, coreIn, liftIn, roof, step, glass, glassDark, door, metal, rail, doorTrim, plinth, floor, ceil;
        /// <summary>Colours the prototype wrote as literals at their call sites: slab sides, soffits and awning undersides, the lift button, facade details.</summary>
        public readonly Swatch slabSide, trimShade, liftButton, detailMetal, grille, detailDark, dish;
        /// <summary>Storey's own: frames and glazing bars, the foundation, the wall's band shade and its brick patches.</summary>
        public readonly Swatch frame, foundation, wallBand, brick;

        public Palette(StyleRef s)
        {
            style = s;
            Swatch W(ColorSlot slot, double tone = 1) => new Swatch(s, slot, tone);
            wall = W(ColorSlot.Wall); trim = W(ColorSlot.Trim); inner = W(ColorSlot.Interior); core = W(ColorSlot.Core);
            coreIn = W(ColorSlot.Core, 1.08); liftIn = W(ColorSlot.LiftInterior); roof = W(ColorSlot.Roof);
            step = W(ColorSlot.Core, 0.82); glass = W(ColorSlot.Glass); glassDark = W(ColorSlot.Glass, 0.42);
            door = W(ColorSlot.Door); metal = W(ColorSlot.Metal); rail = W(ColorSlot.Rail); doorTrim = W(ColorSlot.Interior, 0.72);
            plinth = W(ColorSlot.Wall, 0.72); floor = W(ColorSlot.Floor); ceil = W(ColorSlot.Ceiling);
            slabSide = W(ColorSlot.Core, 0.85); trimShade = W(ColorSlot.Trim, 0.8); liftButton = W(ColorSlot.LiftButton);
            detailMetal = W(ColorSlot.DetailMetal); grille = W(ColorSlot.Grille); detailDark = W(ColorSlot.DetailDark); dish = W(ColorSlot.Dish);
            frame = W(ColorSlot.Frame); foundation = W(ColorSlot.Foundation); wallBand = W(ColorSlot.Wall, 0.72); brick = W(ColorSlot.Wall, 0.8);
        }
    }
}
