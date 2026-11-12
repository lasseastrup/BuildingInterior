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
        /// <summary>
        /// A newel's width: at least as wide as the two rails whose handrails end in it, where they meet at the openings' corners;
        /// or, <see cref="Slim"/>, as wide as one rail, each rail with its own on its line.
        /// </summary>
        public readonly double Newel;
        /// <summary>
        /// Newels narrower than the two rails side by side: each rail has its own, on its line, where the line across the
        /// opening's front crosses it (its handrail running on to them), not one between the two.
        /// </summary>
        public readonly bool Slim;
        /// <summary>
        /// How far each rail's line stands off the flight lane's edge: out, over the floor round the opening; in, over the
        /// treads. Its parts stay 2 cm clear of the edge, so no rail face lies on a step's and the two never meet.
        /// </summary>
        public readonly double Off;
        /// <summary>The walls the walk model and the collision mesh have along a rail: half as wide as its widest part, 4 cm at least.</summary>
        public readonly double R;
        /// <summary>How far a newel stands over the rails' height <see cref="H"/>, though never under a handrail that ends in it.</summary>
        public readonly double NewelAbove;
        /// <summary>A newel's foot, a square block round its base: its width (0: none) and height.</summary>
        public readonly double FootW, FootH;
        /// <summary>A bottom rail the spindles stand on: whether there is one, the gap under it, its width and thickness.</summary>
        public readonly bool Bottom;
        public readonly double BottomGap, BottomW, BottomT;

        Railing(double h, double w, double t, double gap, double spindle, int posts, double post, double newel, bool slim, double off, double r,
            double newelAbove, double footW, double footH, bool bottom, double bottomGap, double bottomW, double bottomT)
        {
            H = h; W = w; T = t; Gap = gap; Spindle = spindle; Posts = posts; Post = post; Newel = newel; Slim = slim; Off = off; R = r;
            NewelAbove = newelAbove; FootW = footW; FootH = footH; Bottom = bottom; BottomGap = bottomGap; BottomW = bottomW; BottomT = bottomT;
        }

        /// <summary>The flights' handrail's top over the nosings' line.</summary>
        public double FlightH => H - Below;
        /// <summary>A newel's top over its floor, with a flight's handrail ending in it this high over that floor (0: none).</summary>
        public double NewelTop(double flight = 0) => Math.Max(H + NewelAbove, flight);
        /// <summary>A newel's top over its floor, with no flight's handrail ending in it.</summary>
        public double NewelH => NewelTop();

        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        /// <summary>A stair's look as built; null for every default.</summary>
        public static Railing Of(StairLook? l)
        {
            double h = Clamp(l?.height ?? Lod0.Shared.StairRail, 0.6, 2.0);
            double w = Clamp(l?.railW ?? 0.04, 0.02, 0.2), t = Clamp(l?.railT ?? 0.07, 0.02, Math.Min(0.3, h - 0.3));
            double spindle = Clamp(l?.spindle ?? 0.025, 0.01, 0.1), gap = Clamp(l?.gap ?? 0.135, spindle + 0.01, 2.0);
            int posts = Math.Max(0, Math.Min(100, l?.posts ?? 0));
            double post = Clamp(l?.post ?? 0.05, spindle, 0.2);
            // a bottom rail at least 10 cm under the flights' handrail
            bool bottom = l?.bottomGap != null;
            double bottomW = Clamp(l?.bottomW ?? w, 0.02, 0.2), bottomT = Clamp(l?.bottomT ?? 0.04, 0.02, 0.2);
            double bottomGap = bottom ? Clamp(l!.bottomGap!.Value, 0, Math.Max(0, h - Below - t - bottomT - 0.1)) : 0;
            double widest = Math.Max(Math.Max(w, spindle), Math.Max(posts > 0 ? post : 0, bottom ? bottomW : 0));
            double off = Math.Max(0.04, widest / 2 + 0.02), rails = Math.Max(w, bottom ? bottomW : 0);
            // as wide as both rails' ends side by side, or slim: as wide as one's, and each rail's line then 2 cm clear of the
            // lane's edge past its newels too
            double pair = 2 * (off + rails / 2) + 0.01, want = l?.newel ?? 0.13;
            bool slim = l?.newel != null && want < pair - 1e-9;   // only asked for: left alone, a newel grows to take both
            double newel = slim ? Clamp(want, rails + 0.01, 0.4) : Clamp(want, pair, 0.4);
            double r = Math.Max(0.04, widest / 2);
            double above = Clamp(l?.newelAbove ?? Above, 0, 0.6);
            bool foot = l?.footW != null || l?.footH != null;
            double footW = foot ? Clamp(l!.footW ?? newel + 0.04, newel + 0.01, 0.8) : 0, footH = foot ? Clamp(l!.footH ?? 0.08, 0.02, Math.Min(0.5, h - 0.1)) : 0;
            if (slim) off = Math.Max(off, Math.Max(newel, footW) / 2 + 0.02);   // a slim newel and its foot on their side of the edge
            return new Railing(h, w, t, gap, spindle, posts, post, newel, slim, off, r, above, footW, footH, bottom, bottomGap, bottomW, bottomT);
        }

        /// <summary>A part's colour: the look's own (a slot of its own, <see cref="KindColors"/>), or the style's.</summary>
        public static Swatch Colour(Palette C, string? colorRef, Swatch style) =>
            colorRef != null && KindColors.SlotOf(colorRef) is ColorSlot slot ? new Swatch(C.style, slot) : style;
    }
}
