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
        /// <summary>Storey k is filled: a walk-in building's storey with nothing inside (false outside 0..N−1).</summary>
        public static bool Filled(BuildingData b, int k) => b.interior && k >= 0 && k < b.floors.Count && b.floors[k].filled;

        /// <summary>Storey k has no inside: the whole building is shell-only, or the storey is filled. Below 0, true.</summary>
        public static bool ShellAt(BuildingData b, int k) => !b.interior || k < 0 || (k < b.floors.Count && b.floors[k].filled);

        /// <summary>Height of storey <paramref name="k"/>: its override, or the ground/upper default.</summary>
        public static double FloorH(BuildingData b, int k)
        {
            if (k >= 0 && k < b.floors.Count && b.floors[k].h is double h) return h;
            return k == 0 ? b.groundHeight : b.floorHeight;
        }

        /// <summary>
        /// Height of the floor slab of storey <paramref name="k"/> in the site: the building's elevation plus the storeys
        /// below; fractional k interpolates. Site heights, so they compare directly with a neighbour's, a bridge's, the
        /// player's and the camera's.
        /// </summary>
        public static double FloorBase(BuildingData b, double k)
        {
            if (k <= 0) return b.elevation;
            int n = (int)Math.Floor(k);
            double y = b.elevation;
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

        /// <summary>The style of the nearest tier at or below storey k that has one of its own, or null (the building's).</summary>
        static FacadeStyle? OwnStyleAt(BuildingData b, int k)
        {
            int j = TierStart(b, k);
            while (j > 0 && b.floors[j].style == null) j = TierStart(b, j - 1);
            return j > 0 ? b.floors[j].style : null;
        }

        /// <summary>
        /// <see cref="StyleAt"/> for reading an exterior field (the roof, the walls, the windows) without the copy it makes
        /// for a setback's own style: the same values for those, the indoor ones not mixed in. Never change what it returns.
        /// </summary>
        public static FacadeStyle ExteriorStyleAt(BuildingData b, int k) => OwnStyleAt(b, k) ?? b.style;

        /// <summary>
        /// Exterior style of storey <paramref name="k"/>: the nearest tier at or below it with a
        /// style of its own, with the interior, floor, core and ground values and the indoor colours
        /// (rail, metal, ceiling, lift interior and button) always taken from the building's style (a
        /// setback changes the outside only).
        /// </summary>
        public static FacadeStyle StyleAt(BuildingData b, int k)
        {
            var own = OwnStyleAt(b, k);
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
