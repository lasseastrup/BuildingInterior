#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey
{
    /// <summary>
    /// Values derived from a building and never stored (SPEC §3): storey heights and bases,
    /// tiers, outlines and styles per floor, shaft tops. Ports of the prototype's functions of
    /// the same names; <c>unity/Fixtures/derived.json</c> holds the prototype's answers.
    /// </summary>
    public static class Derived
    {
        /// <summary>Height of storey <paramref name="k"/>: its override, or the ground/upper default.</summary>
        public static double FloorH(BuildingData b, int k)
        {
            if (k >= 0 && k < b.floors.Count && b.floors[k].h is double h) return h;
            return k == 0 ? b.groundHeight : b.floorHeight;
        }

        /// <summary>Height of the floor slab of storey <paramref name="k"/> above the ground; fractional k interpolates.</summary>
        public static double FloorBase(BuildingData b, double k)
        {
            if (k <= 0) return 0;
            int n = (int)Math.Floor(k);
            double y = 0;
            for (int j = 0; j < n; j++) y += FloorH(b, j);
            return y + (k - n) * FloorH(b, n);
        }

        public static double RoofY(BuildingData b) => FloorBase(b, b.floors.Count);

        /// <summary>The floor whose outline storey <paramref name="k"/> uses: the nearest setback at or below it, else 0.</summary>
        public static int TierStart(BuildingData b, int k)
        {
            for (int j = Math.Min(k, b.floors.Count - 1); j > 0; j--)
                if (b.floors[j].HasShape) return j;
            return 0;
        }

        /// <summary>Outline of storey <paramref name="k"/> (k = floors.Count gives the roof's).</summary>
        public static List<Vec2> OutlineAt(BuildingData b, int k)
        {
            int j = TierStart(b, k);
            return j > 0 ? b.floors[j].shape : b.footprint;
        }

        public static bool IsSetback(BuildingData b, int k) => k > 0 && k < b.floors.Count && b.floors[k].HasShape;

        public static bool HasTerrace(BuildingData b, int k) => IsSetback(b, k) && b.floors[k].terraceRoof == null;

        /// <summary>
        /// Exterior style of storey <paramref name="k"/>: the nearest tier at or below it with a
        /// style of its own, with the interior, floor, core and ground values and the indoor colours
        /// (rail, metal, ceiling, lift interior and button) always taken from the building's style (a
        /// setback changes the outside only).
        /// </summary>
        public static FacadeStyle StyleAt(BuildingData b, int k)
        {
            int j = TierStart(b, k);
            while (j > 0 && b.floors[j].style == null) j = TierStart(b, j - 1);
            var own = j > 0 ? b.floors[j].style : null;
            if (own == null) return b.style;
            var mixed = own.Clone();
            mixed.interior = b.style.interior;
            mixed.floor = b.style.floor;
            mixed.core = b.style.core;
            mixed.ground = b.style.ground;
            mixed.rail = b.style.rail;
            mixed.metal = b.style.metal;
            mixed.ceiling = b.style.ceiling;
            mixed.liftInterior = b.style.liftInterior;
            mixed.liftButton = b.style.liftButton;
            return mixed;
        }

        /// <summary>Top floor a core serves: its own top, or the building's top floor.</summary>
        public static int ShaftTop(BuildingData b, CoreData s) =>
            s.top < 0 ? b.floors.Count - 1 : Math.Min(s.top, b.floors.Count - 1);
    }
}
