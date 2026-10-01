#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Edit
{
    /// <summary>
    /// Floor operations, the core workflow (SPEC §2.1): new floors copy the top floor's layout, and stairs, lifts,
    /// doors and details keep their storeys when floors come and go. Ported from the prototype.
    /// </summary>
    public static class Floors
    {
        public const int MaxFloors = 40;

        /// <summary>A floor with another floor's rooms. Never its setback outline or height: the copy inherits the outline below it.</summary>
        public static FloorData LayoutCopy(FloorData f) => new FloorData { walls = Tiers.Copy(f.walls) };

        /// <summary>A floor on top, with the top floor's rooms (or empty).</summary>
        public static void AddTop(BuildingData b, bool copyLayout = true) =>
            b.floors.Add(copyLayout ? LayoutCopy(b.floors[b.floors.Count - 1]) : new FloorData());

        /// <summary>Duplicate floor k: a copy of its rooms goes in above it, and everything above moves up.</summary>
        public static void InsertAbove(BuildingData b, int k)
        {
            b.floors.Insert(k + 1, LayoutCopy(b.floors[k]));
            foreach (var s in b.shafts) { if (s.bottom > k) s.bottom++; if (s.top >= 0 && s.top > k) s.top++; }
            foreach (var e in b.entrances) if (e.k > k) e.k++;
            foreach (var d in b.details) if (d.k > k) d.k++;
        }

        /// <summary>
        /// Delete floor k; the floors above move down. A setback starting at k moves to the floor above it; a setback
        /// that ends up on the ground floor becomes the footprint. A stair or lift serving only floor k goes; the others
        /// shrink. False when it is the only floor.
        /// </summary>
        public static bool Delete(BuildingData b, int k)
        {
            if (b.floors.Count <= 1) return false;
            var gone = b.floors[k]; var next = k + 1 < b.floors.Count ? b.floors[k + 1] : null;
            if (gone.HasShape && next != null && !next.HasShape) { next.shape = gone.shape; next.blank = gone.blank; }   // the floors above keep the setback (and its terrace doors)
            else if (k > 0 || (next != null && next.HasShape)) b.entrances.RemoveAll(e => e.k == k);
            b.floors.RemoveAt(k);
            foreach (var e in b.entrances) if (e.k > k) e.k--;
            b.details.RemoveAll(d => d.k == k);
            foreach (var d in b.details) if (d.k > k) d.k--;
            var g = b.floors[0];
            if (g.HasShape) { b.footprint = g.shape; b.blank = g.blank; g.shape = new List<Vec2>(); g.blank = new List<int>(); }   // a setback that became the ground floor is the new base
            b.shafts.RemoveAll(s => s.bottom == k && s.top == k);
            foreach (var s in b.shafts)
            {
                if (s.bottom > k) s.bottom--;
                if (s.top >= 0 && s.top >= k) s.top = Math.Max(s.bottom, s.top - 1);
                s.bottom = Math.Min(s.bottom, b.floors.Count - 1);
            }
            return true;
        }

        /// <summary>Type 10 to get 10 floors: added on top (copying the top floor) or removed from the top. Clamped to 1–40.</summary>
        public static void SetCount(BuildingData b, int n, bool copyLayout = true)
        {
            n = Math.Max(1, Math.Min(MaxFloors, n));
            while (b.floors.Count < n) AddTop(b, copyLayout);
            while (b.floors.Count > n) Delete(b, b.floors.Count - 1);
        }

        /// <summary>Give every floor above k floor k's rooms.</summary>
        public static void CopyLayoutUp(BuildingData b, int k)
        {
            for (int j = k + 1; j < b.floors.Count; j++) b.floors[j].walls = Tiers.Copy(b.floors[k].walls);
        }
    }
}
