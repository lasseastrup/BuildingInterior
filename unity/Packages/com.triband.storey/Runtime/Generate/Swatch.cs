#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// The colours of a style, in the order of a colour row (docs/COLOURS.md §3.3). The first seven
    /// are the style's own colours; the rest are optional per style and fall back to project defaults.
    /// </summary>
    public enum ColorSlot : byte
    {
        Wall, Trim, Interior, Floor, Roof, Core, Glass,
        Door, Metal, Rail, Ceiling, LiftInterior, LiftButton,
        DetailMetal, Grille, DetailDark, Dish,
    }

    /// <summary>
    /// The style a colour comes from: building <see cref="building"/> (site index) at the setback tier that
    /// starts at floor <see cref="tier"/>. Resolves to <c>Derived.StyleAt(building, tier)</c>.
    /// </summary>
    public readonly struct StyleRef : IEquatable<StyleRef>
    {
        public readonly int building, tier;
        public StyleRef(int building, int tier) { this.building = building; this.tier = tier; }
        public bool Equals(StyleRef o) => building == o.building && tier == o.tier;
        public override bool Equals(object? o) => o is StyleRef s && Equals(s);
        public override int GetHashCode() => building * 397 ^ tier;
        public override string ToString() => $"b{building}/t{tier}";
    }

    /// <summary>
    /// A colour by reference: a slot of a style at a shade (a factor on the linear colour). The
    /// generator emits these instead of RGB, so a palette edit or a restyle never needs new geometry.
    /// </summary>
    public readonly struct Swatch : IEquatable<Swatch>
    {
        public readonly StyleRef style;
        public readonly ColorSlot slot;
        public readonly double tone;
        public Swatch(StyleRef style, ColorSlot slot, double tone = 1) { this.style = style; this.slot = slot; this.tone = tone; }

        /// <summary>The same colour, darker or lighter (the prototype's <c>shade</c>).</summary>
        public Swatch Shade(double f) => new Swatch(style, slot, tone * f);

        public bool Equals(Swatch o) => style.Equals(o.style) && slot == o.slot && tone == o.tone;
        public override bool Equals(object? o) => o is Swatch s && Equals(s);
        public override int GetHashCode() => (style.GetHashCode() * 31 + (int)slot) * 31 + tone.GetHashCode();
        public override string ToString() => $"{style}:{slot}x{tone:0.###}";
    }

    /// <summary>
    /// The value of each slot of a style: its own field, or the project default for the optional
    /// slots (the prototype's fixed colours) when the style leaves one out.
    /// </summary>
    public static class StyleColors
    {
        public const string Door = "#3B3129", Metal = "#A3ABAE", Rail = "#3D4448", Ceiling = "#F3F2EE", LiftInterior = "#8E9A9E",
            LiftButton = "#FFB36B", DetailMetal = "#C9CDCB", Grille = "#6E7476", DetailDark = "#5B5F5E", Dish = "#DDE0DE";

        public static string Of(FacadeStyle st, ColorSlot slot) => slot switch
        {
            ColorSlot.Wall => st.wall, ColorSlot.Trim => st.trim, ColorSlot.Interior => st.interior, ColorSlot.Floor => st.floor,
            ColorSlot.Roof => st.roof, ColorSlot.Core => st.core, ColorSlot.Glass => st.glass,
            ColorSlot.Door => st.door ?? Door, ColorSlot.Metal => st.metal ?? Metal, ColorSlot.Rail => st.rail ?? Rail,
            ColorSlot.Ceiling => st.ceiling ?? Ceiling, ColorSlot.LiftInterior => st.liftInterior ?? LiftInterior,
            ColorSlot.LiftButton => st.liftButton ?? LiftButton, ColorSlot.DetailMetal => st.detailMetal ?? DetailMetal,
            ColorSlot.Grille => st.grille ?? Grille, ColorSlot.DetailDark => st.detailDark ?? DetailDark, ColorSlot.Dish => st.dish ?? Dish,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }

    /// <summary>
    /// Swatches back to linear RGB through the site's styles, with the prototype's arithmetic. Used by the
    /// census tests and, until the GPU reads colour rows, by the mesh upload. A palette id needs
    /// <paramref name="palette"/>; without one it is an error.
    /// </summary>
    public sealed class ColorResolver
    {
        readonly Site site;
        readonly Func<string, Rgb>? palette;
        readonly Dictionary<StyleRef, FacadeStyle> styles = new Dictionary<StyleRef, FacadeStyle>();

        public ColorResolver(Site site, Func<string, Rgb>? palette = null) { this.site = site; this.palette = palette; }

        public FacadeStyle StyleOf(StyleRef s)
        {
            if (!styles.TryGetValue(s, out var st)) styles[s] = st = Derived.StyleAt(site.Buildings[s.building], s.tier);
            return st;
        }

        public Rgb Resolve(Swatch s)
        {
            string v = StyleColors.Of(StyleOf(s.style), s.slot);
            if (ColorRef.IsPaletteId(v))
            {
                if (palette == null) throw new FormatException($"{s}: palette id {v} and no palette to resolve it");
                return palette(v).Shade(s.tone);
            }
            return Colors.Shade(v, s.tone);
        }
    }
}
