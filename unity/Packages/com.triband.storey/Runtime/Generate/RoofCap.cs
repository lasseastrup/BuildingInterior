#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// A cap on a flat roof's edge (docs/EDITOR.md §6.7; <see cref="FacadeStyle.capH"/>), as the old BuildingGenerator's roof
    /// insert: what the style's numbers make of it. Across is measured as everywhere on a wall, from the outline's line
    /// (the wall's inner face, w = 0) out (its outer face at <see cref="Dim.T_EXT"/>). Storey's own.
    /// <list type="bullet">
    /// <item><b>Raised</b> (height 0 or more, depth 0 or less): a ring standing on the wall's top from
    /// <see cref="wIn"/> to <see cref="wOut"/> (out past the wall by the overhang), <see cref="h"/> high (2 cm at least, so
    /// a flat strip lies on nothing). It takes the curb's place; a parapet stands on it. Its top is the cap's colour for
    /// the strip's width from its outer edge, the roof's in from there; under its overhang, the rim colour.</item>
    /// <item><b>Fascia</b> (height below 0, an overhang): a strip hanging <see cref="h"/> from the roof's edge, out past the
    /// wall, under the curb or parapet, which stay.</item>
    /// <item><b>Inset</b> (depth above 0): a ring standing on the roof set in from the wall's face (at least to the curb's
    /// inner face), the strip's width thick (or the wall's), cut where the parapet stops (party walls, bulkheads) and round
    /// the bulkheads and openings on the roof, from its outline offset with Clipper, so a concave outline and big insets
    /// come out right.</item>
    /// </list>
    /// </summary>
    public readonly struct RoofCap
    {
        public readonly bool raised, fascia, inset;
        /// <summary>Raised and inset: its height over the roof; fascia: how far it hangs under it.</summary>
        public readonly double h;
        /// <summary>Across: its inner and outer sides, and where the strip in its colour starts (out to <see cref="wOut"/>).</summary>
        public readonly double wIn, wOut, wStrip;

        RoofCap(bool raised, bool fascia, bool inset, double h, double wIn, double wOut, double wStrip)
        { this.raised = raised; this.fascia = fascia; this.inset = inset; this.h = h; this.wIn = wIn; this.wOut = wOut; this.wStrip = wStrip; }

        /// <summary>The building's cap: its top tier's style's, on a flat roof; null with none.</summary>
        public static RoofCap? Of(BuildingData b) => Roofs.IsPitched(b) ? (RoofCap?)null : Of(Derived.StyleAt(b, b.floors.Count));

        public static RoofCap? Of(FacadeStyle st)
        {
            if (!(st.capH is double h0) || st.roofType != RoofType.Flat) return null;
            double T = Dim.T_EXT, d = Math.Max(-2, Math.Min(10, st.capDepth ?? 0)), W = Math.Max(0, Math.Min(5, st.capStrip ?? 0)), h = Math.Max(-3, Math.Min(5, h0));
            if (d > 1e-3)
            {
                double outer = Math.Min(T - d, 0), thick = W > 0 ? Math.Max(0.02, W) : T;
                return new RoofCap(false, false, true, Math.Max(0.02, h), outer - thick, outer, W > 0 ? outer - thick : outer);
            }
            double wOut = T - d;
            if (h >= 0) return new RoofCap(true, false, false, Math.Max(0.02, h), Math.Min(0, wOut - W), wOut, W > 0 ? wOut - W : wOut);
            if (wOut < T + 0.01) return null;   // a fascia hangs from an overhang
            return new RoofCap(false, true, false, -h, T, wOut, W > 0 ? Math.Max(T, wOut - W) : T);
        }

        /// <summary>How far a raised cap lifts the parapet (0 for the others).</summary>
        public double Lift => raised ? h : 0;
    }
}
