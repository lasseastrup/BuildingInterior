#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Triband.Storey.Generate
{
    /// <summary>Colours as the prototype computes them: CSS hex, converted from sRGB to linear.</summary>
    public static class Colors
    {
        static readonly Dictionary<string, Rgb> cache = new Dictionary<string, Rgb>(StringComparer.Ordinal);

        static double ToLinear(double c) => c < 0.04045 ? c * 0.0773993808 : Math.Pow(c * 0.9478672986 + 0.0521327014, 2.4);

        public static Rgb Col(string hex)
        {
            if (cache.TryGetValue(hex, out var v)) return v;
            string h = hex.TrimStart('#');
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            int n = int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            v = new Rgb(ToLinear(((n >> 16) & 255) / 255.0), ToLinear(((n >> 8) & 255) / 255.0), ToLinear((n & 255) / 255.0));
            cache[hex] = v;
            return v;
        }

        public static Rgb Shade(string hex, double f) => Col(hex).Shade(f);
    }

    /// <summary>The palette a storey is built with (the prototype's <c>colorsOf</c>).</summary>
    public sealed class Palette
    {
        public Rgb wall, trim, inner, core, coreIn, liftIn, roof, step, glass, glassDark, door, metal, rail, doorTrim, plinth, floor, ceil;

        public Palette(FacadeStyle st)
        {
            wall = Colors.Col(st.wall); trim = Colors.Col(st.trim); inner = Colors.Col(st.interior); core = Colors.Col(st.core);
            coreIn = Colors.Shade(st.core, 1.08); liftIn = Colors.Col("#8E9A9E"); roof = Colors.Col(st.roof);
            step = Colors.Shade(st.core, 0.82); glass = Colors.Col(st.glass); glassDark = Colors.Shade(st.glass, 0.42);
            door = Colors.Col("#3B3129"); metal = Colors.Col("#A3ABAE"); rail = Colors.Col("#3D4448"); doorTrim = Colors.Shade(st.interior, 0.72);
            plinth = Colors.Shade(st.wall, 0.72); floor = Colors.Col(st.floor); ceil = Colors.Col("#F3F2EE");
        }
    }
}
