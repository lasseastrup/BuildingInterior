#nullable enable
using System;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// A straight flight's rails and steps as they are built (<see cref="StairLook"/>, docs/EDITOR.md §5): each setting its look
    /// gives, held to its range, or its default, which is how they looked before they could be set. LOD0 builds the rails and
    /// steps with them, and the walk model and the collision mesh their walls. Storey's own.
    /// </summary>
    public readonly struct Railing
    {
        /// <summary>The rails' top over the floor round the openings (<see cref="Lod0.Shared.StairRail"/> by default).</summary>
        public readonly double H;
        /// <summary>How much lower the flights' handrail is over the nosings' line than <see cref="H"/>, and how much higher the newels stand.</summary>
        public const double Below = 0.12, Above = 0.16;
        /// <summary>The handrail's width and thickness.</summary>
        public readonly double W, T;
        /// <summary>The spindles: centre to centre along the rail, and how thick.</summary>
        public readonly double Gap, Spindle;
        /// <summary>Spindles between two posts (0: no posts, the newels at the ends alone), and a post's width.</summary>
        public readonly int Posts;
        public readonly double Post;
        /// <summary>A newel's width: at least as wide as the two rails whose handrails end in it.</summary>
        public readonly double Newel;
        /// <summary>
        /// How far each rail's line stands off the flight lane's edge: out, over the floor round the opening; in, over the
        /// treads. Its parts stay 2 cm clear of the edge, so no rail face lies on a step's and the two never meet.
        /// </summary>
        public readonly double Off;
        /// <summary>The walls the walk model and the collision mesh have along a rail: half as wide as its widest part, 4 cm at least.</summary>
        public readonly double R;

        Railing(double h, double w, double t, double gap, double spindle, int posts, double post, double newel, double off, double r)
        {
            H = h; W = w; T = t; Gap = gap; Spindle = spindle; Posts = posts; Post = post; Newel = newel; Off = off; R = r;
        }

        /// <summary>The flights' handrail's top over the nosings' line.</summary>
        public double FlightH => H - Below;
        /// <summary>A newel's top over its floor.</summary>
        public double NewelH => H + Above;

        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        /// <summary>A stair's look as built; null for every default.</summary>
        public static Railing Of(StairLook? l)
        {
            double h = Clamp(l?.height ?? Lod0.Shared.StairRail, 0.6, 2.0);
            double w = Clamp(l?.railW ?? 0.04, 0.02, 0.2), t = Clamp(l?.railT ?? 0.07, 0.02, Math.Min(0.3, h - 0.3));
            double spindle = Clamp(l?.spindle ?? 0.025, 0.01, 0.1), gap = Clamp(l?.gap ?? 0.135, spindle + 0.01, 2.0);
            int posts = Math.Max(0, Math.Min(100, l?.posts ?? 0));
            double post = Clamp(l?.post ?? 0.05, spindle, 0.2);
            double off = Math.Max(0.04, Math.Max(w / 2, Math.Max(spindle / 2, posts > 0 ? post / 2 : 0)) + 0.02);
            double newel = Clamp(l?.newel ?? 0.13, 2 * (off + w / 2) + 0.01, 0.4);
            double r = Math.Max(0.04, Math.Max(w, Math.Max(spindle, posts > 0 ? post : 0)) / 2);
            return new Railing(h, w, t, gap, spindle, posts, post, newel, off, r);
        }

        /// <summary>A part's colour: the look's own (a slot of its own, <see cref="KindColors"/>), or the style's.</summary>
        public static Swatch Colour(Palette C, string? colorRef, Swatch style) =>
            colorRef != null && KindColors.SlotOf(colorRef) is ColorSlot slot ? new Swatch(C.style, slot) : style;
    }
}
