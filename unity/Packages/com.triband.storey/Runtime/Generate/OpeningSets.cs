#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// Which window or door goes in each opening (docs/OPENINGS.md §7.1): a style's sets of windows (the ground floor's its
    /// own, or the floors above's) and of street doors, each entry picked as often as its weight says. The pick is a hash of
    /// the building's id, the storey, the wall and the window's place along it: the same on every build and at every LOD,
    /// different from building to building. A style without a set has its one window and door in every opening, as before.
    /// Nothing here allocates. Storey's own.
    /// </summary>
    public static class OpeningSets
    {
        /// <summary>The window entries of a style's ground floor (<paramref name="ground"/>) or the floors above; null when it has no set.</summary>
        public static List<OpeningEntry>? Windows(FacadeStyle? st, bool ground)
        {
            if (st == null) return null;
            if (ground && st.groundSet is { Count: > 0 } g) return g;
            return st.windowSet is { Count: > 0 } w ? w : null;
        }

        /// <summary>The street door entries of a style; null when it has no set.</summary>
        public static List<OpeningEntry>? Doors(FacadeStyle? st) => st?.doorSet is { Count: > 0 } d ? d : null;

        /// <summary>An entry's weight as picked: its own, or 1 each when every weight in its set is 0 (as the old BuildingGenerator has it).</summary>
        public static double Weight(List<OpeningEntry> set, int i) => W(set[i], Weighted(set));

        // whether any entry has a weight; without, each counts 1
        static bool Weighted(List<OpeningEntry> set)
        {
            for (int i = 0; i < set.Count; i++) if (set[i].weight > 0) return true;
            return false;
        }

        static double W(OpeningEntry e, bool weighted) => weighted ? Math.Max(0, e.weight) : 1;

        /// <summary>The entry a hash picks: entry i is picked with odds its weight over the set's total.</summary>
        public static int Pick(List<OpeningEntry> set, uint hash)
        {
            bool weighted = Weighted(set); double total = 0;
            for (int i = 0; i < set.Count; i++) total += W(set[i], weighted);
            double r = Unit(hash) * total, acc = 0; int last = 0;
            for (int i = 0; i < set.Count; i++)
            {
                double w = W(set[i], weighted); if (w <= 0) continue;
                acc += w; last = i;
                if (r < acc) return i;
            }
            return last;
        }

        /// <summary>The kind an entry names, if it is registered and of its sort (a window, or a door); null = the generator's own.</summary>
        public static OpeningKind? KindOf(OpeningEntry e, bool door)
        {
            var k = OpeningKinds.Get(e.kind);
            return k != null && k.door == door ? k : null;
        }

        /// <summary>
        /// The window a hash picks for a ground-floor (<paramref name="ground"/>) or upper window of a style: false where the
        /// entry picked is none, else true with its kind (null: the generator's). A style without a set has its one window.
        /// </summary>
        public static bool Window(FacadeStyle? st, bool ground, uint hash, out OpeningKind? kind)
        {
            var set = Windows(st, ground);
            if (set == null) { kind = OpeningKinds.Of(st, false); return true; }
            var e = set[Pick(set, hash)];
            kind = e.none ? null : KindOf(e, false);
            return !e.none;
        }

        /// <summary>The street door a hash picks for a style (null: the plain one).</summary>
        public static OpeningKind? Door(FacadeStyle? st, uint hash)
        {
            var set = Doors(st);
            if (set == null) return OpeningKinds.Of(st, true);
            var e = set[Pick(set, hash)];
            return e.none ? null : KindOf(e, true);
        }

        /// <summary>
        /// The one window that stands for a style's windows where only one can (LOD2's painted windows, a dormer's): its set's
        /// heaviest window that is not none (the first of equals). False when every entry with a weight is none: no windows.
        /// </summary>
        public static bool Representative(FacadeStyle? st, bool ground, out OpeningKind? kind)
        {
            var set = Windows(st, ground);
            if (set == null) { kind = OpeningKinds.Of(st, false); return true; }
            int best = -1; double bw = 0; bool weighted = Weighted(set);
            for (int i = 0; i < set.Count; i++)
            {
                double w = W(set[i], weighted);
                if (!set[i].none && w > bw) { best = i; bw = w; }
            }
            kind = best >= 0 ? KindOf(set[best], false) : null;
            return best >= 0;
        }

        /// <summary>
        /// How far a style's windows reach past their holes, left and right: the most of any it can pick, so that whichever
        /// is picked, its dressing keeps clear of the neighbouring wall (<see cref="Facade.WindowSpan(FacadeStyle, bool, double, double)"/>).
        /// </summary>
        public static (double l, double r) Reach(FacadeStyle? st, bool ground)
        {
            var set = Windows(st, ground);
            if (set == null) { var k = OpeningKinds.Of(st, false); return (k?.reachL ?? 0, k?.reachR ?? 0); }
            double l = 0, r = 0; bool weighted = Weighted(set);
            for (int i = 0; i < set.Count; i++)
            {
                if (set[i].none || W(set[i], weighted) <= 0 || !(KindOf(set[i], false) is OpeningKind k)) continue;
                l = Math.Max(l, k.reachL); r = Math.Max(r, k.reachR);
            }
            return (l, r);
        }

        /// <summary>
        /// Whether any window a style can pick, on the ground floor or above, is the generator's own (its sills, heads, frames
        /// and glazing bars then show), or one of the artist's (<paramref name="artist"/>) that keeps its own size (<paramref name="ownSize"/>).
        /// </summary>
        public static void Uses(FacadeStyle st, out bool generated, out bool artist, out bool ownSize)
        {
            generated = artist = ownSize = false;
            for (int g = 0; g < 2; g++)
            {
                var set = Windows(st, g == 1);
                if (set == null) { Note(OpeningKinds.Of(st, false), ref generated, ref artist, ref ownSize); continue; }
                for (int i = 0; i < set.Count; i++)
                    if (!set[i].none && Weight(set, i) > 0) Note(KindOf(set[i], false), ref generated, ref artist, ref ownSize);
            }
            static void Note(OpeningKind? k, ref bool g, ref bool a, ref bool o) { if (k == null) g = true; else { a = true; o |= !k.stretch; } }
        }

        // ---- the pick's hash ----------------------------------------------------------------------

        /// <summary>
        /// The hash a wall's windows are picked from: the building's id, the storey (or, picking by column or by wall, the
        /// storey its tier starts at) and the wall's edge. FNV-1a, as <see cref="Facade.Seed"/>.
        /// </summary>
        public static uint WallSeed(string id, int k, int edge)
        {
            uint h = 2166136261;
            foreach (char ch in id) h = (h ^ ch) * 16777619;
            h = (h ^ (uint)k) * 16777619; h = (h ^ (uint)edge) * 16777619;
            return h;
        }

        /// <summary>The hash window <paramref name="j"/> along a wall is picked with: the wall's mixed with its place (a finaliser, so neighbours differ).</summary>
        public static uint At(uint wall, int j)
        {
            unchecked
            {
                uint h = wall ^ ((uint)j + 0x9e3779b9u) * 0x85ebca6bu;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return h;
            }
        }

        /// <summary>The hash a door on a wall is picked with: every door on one wall the same, as the old BuildingGenerator's.</summary>
        public static uint DoorSeed(string id, int k, int edge) => At(WallSeed(id, k, edge), -1);

        /// <summary>
        /// The hash for window <paramref name="j"/> on edge <paramref name="edge"/> of storey <paramref name="k"/> (its tier from
        /// <paramref name="k0"/>), as the style picks (<see cref="FacadeStyle.windowPick"/>).
        /// </summary>
        public static uint WindowSeed(string id, WindowPick pick, int k, int k0, int edge, int j) => pick switch
        {
            WindowPick.Column => At(WallSeed(id, k == 0 ? 0 : k0 == 0 ? 1 : k0, edge), j),   // the ground floor (its own set) is a column of its own
            WindowPick.Storey => At(WallSeed(id, k, edge), 0),
            WindowPick.Wall => At(WallSeed(id, k == 0 ? 0 : k0 == 0 ? 1 : k0, edge), 0),
            _ => At(WallSeed(id, k, edge), j),
        };

        // a hash's top 24 bits, 0 to 1
        static double Unit(uint h) => (h >> 8) / 16777216.0;
    }
}
