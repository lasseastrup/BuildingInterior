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
        /// <summary>Window and door frames, glazing bars, and their heads and sills when the style has frames. Storey's own.</summary>
        Frame,
        /// <summary>The foundation along the foot of the ground floor. Storey's own.</summary>
        Foundation,
        /// <summary>The ground floor's plinth when the style gives it a colour of its own (otherwise a shade of the wall). Storey's own.</summary>
        Plinth,
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
    /// The project's colours for the optional slots, used where a style leaves one out (docs/COLOURS.md §3.1).
    /// Defaults are the prototype's fixed colours; with Color Pipeline, <c>StoreyColorSettings</c> supplies palette ids.
    /// </summary>
    public sealed class StyleDefaults
    {
        public string door = StyleColors.Door, metal = StyleColors.Metal, rail = StyleColors.Rail, ceiling = StyleColors.Ceiling,
            liftInterior = StyleColors.LiftInterior, liftButton = StyleColors.LiftButton, detailMetal = StyleColors.DetailMetal,
            grille = StyleColors.Grille, detailDark = StyleColors.DetailDark, dish = StyleColors.Dish,
            frame = StyleColors.Frame, foundation = StyleColors.Foundation, plinth = StyleColors.Plinth;

        /// <summary>The prototype's fixed colours.</summary>
        public static readonly StyleDefaults Prototype = new StyleDefaults();
    }

    /// <summary>
    /// The value of each slot of a style: its own field, or the project default for the optional
    /// slots (the prototype's fixed colours) when the style leaves one out.
    /// </summary>
    public static class StyleColors
    {
        public const string Door = "#3B3129", Metal = "#A3ABAE", Rail = "#3D4448", Ceiling = "#F3F2EE", LiftInterior = "#8E9A9E",
            LiftButton = "#FFB36B", DetailMetal = "#C9CDCB", Grille = "#6E7476", DetailDark = "#5B5F5E", Dish = "#DDE0DE",
            Frame = "#33393B", Foundation = "#3C3F41", Plinth = "#7C7976";

        public static string Of(FacadeStyle st, ColorSlot slot, StyleDefaults? defaults = null)
        {
            var d = defaults ?? StyleDefaults.Prototype;
            return slot switch
            {
                ColorSlot.Wall => st.wall, ColorSlot.Trim => st.trim, ColorSlot.Interior => st.interior, ColorSlot.Floor => st.floor,
                ColorSlot.Roof => st.roof, ColorSlot.Core => st.core, ColorSlot.Glass => st.glass,
                ColorSlot.Door => st.door ?? d.door, ColorSlot.Metal => st.metal ?? d.metal, ColorSlot.Rail => st.rail ?? d.rail,
                ColorSlot.Ceiling => st.ceiling ?? d.ceiling, ColorSlot.LiftInterior => st.liftInterior ?? d.liftInterior,
                ColorSlot.LiftButton => st.liftButton ?? d.liftButton, ColorSlot.DetailMetal => st.detailMetal ?? d.detailMetal,
                ColorSlot.Grille => st.grille ?? d.grille, ColorSlot.DetailDark => st.detailDark ?? d.detailDark, ColorSlot.Dish => st.dish ?? d.dish,
                ColorSlot.Frame => st.frame ?? d.frame, ColorSlot.Foundation => st.foundationColor ?? d.foundation,
                ColorSlot.Plinth => st.plinth ?? d.plinth,
                _ => throw new ArgumentOutOfRangeException(nameof(slot)),
            };
        }
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

        readonly StyleDefaults defaults;

        public ColorResolver(Site site, Func<string, Rgb>? palette = null, StyleDefaults? defaults = null)
        { this.site = site; this.palette = palette; this.defaults = defaults ?? StyleDefaults.Prototype; }

        public FacadeStyle StyleOf(StyleRef s)
        {
            if (!styles.TryGetValue(s, out var st)) styles[s] = st = Derived.StyleAt(site.Buildings[s.building], s.tier);
            return st;
        }

        public Rgb Resolve(Swatch s)
        {
            string v = StyleColors.Of(StyleOf(s.style), s.slot, defaults);
            if (ColorRef.IsPaletteId(v))
            {
                if (palette == null) throw new FormatException($"{s}: palette id {v} and no palette to resolve it");
                return palette(v).Shade(s.tone);
            }
            return Colors.Shade(v, s.tone);
        }
    }
}
