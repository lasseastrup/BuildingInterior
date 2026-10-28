#nullable enable
using System.Collections.Generic;

namespace Triband.Storey.Edit
{
    /// <summary>Which of a style's sets (docs/OPENINGS.md §7.1): its windows, its ground floor's own, its street doors.</summary>
    public enum SetOf { Windows, Ground, Doors }

    /// <summary>
    /// The Facade tab's edits to a style's sets of windows and doors (docs/EDITOR.md §6.9, docs/OPENINGS.md §7.1). A style
    /// with one window keeps it in <see cref="FacadeStyle.windowKind"/>, as before sets: the first entry added beside it
    /// makes a set of the two, and a set left with one window goes back to being the style's one. So a layout of one
    /// window a style reads and writes as it always has. Storey's own.
    /// </summary>
    public static class WindowSets
    {
        /// <summary>
        /// The entries the tab lists: the style's set, or its one window or door as a set of one; null for the ground floor
        /// when it has the floors above's windows.
        /// </summary>
        public static List<OpeningEntry>? Shown(FacadeStyle st, SetOf which)
        {
            switch (which)
            {
                case SetOf.Ground: return st.groundSet is { Count: > 0 } g ? g : null;
                case SetOf.Doors: return st.doorSet is { Count: > 0 } d ? d : new List<OpeningEntry> { new OpeningEntry { kind = st.doorKind } };
                default: return st.windowSet is { Count: > 0 } w ? w : new List<OpeningEntry> { new OpeningEntry { kind = st.windowKind } };
            }
        }

        /// <summary>
        /// The set to edit, made the style's own: from its one window or door (which it then no longer names), and the ground
        /// floor's from the floors above's.
        /// </summary>
        public static List<OpeningEntry> Own(FacadeStyle st, SetOf which)
        {
            switch (which)
            {
                case SetOf.Ground:
                    if (st.groundSet is { Count: > 0 } g) return g;
                    return st.groundSet = OpeningEntry.Copy(Shown(st, SetOf.Windows))!;
                case SetOf.Doors:
                    if (st.doorSet is { Count: > 0 } d) return d;
                    st.doorSet = new List<OpeningEntry> { new OpeningEntry { kind = st.doorKind } }; st.doorKind = null;
                    return st.doorSet;
                default:
                    if (st.windowSet is { Count: > 0 } w) return w;
                    st.windowSet = new List<OpeningEntry> { new OpeningEntry { kind = st.windowKind } }; st.windowKind = null;
                    return st.windowSet;
            }
        }

        /// <summary>Add an entry: an artist's window or door (<paramref name="kind"/>, null: the generator's), or none (no window there).</summary>
        public static void Add(FacadeStyle st, SetOf which, string? kind, bool none = false, double weight = 1)
        {
            Own(st, which).Add(new OpeningEntry { kind = none ? null : kind, none = none && which != SetOf.Doors, weight = weight });
            Tidy(st, which);
        }

        /// <summary>Remove entry i; the last one of the ground floor's set gives it the floors above's windows again.</summary>
        public static void Remove(FacadeStyle st, SetOf which, int i)
        {
            var set = Own(st, which);
            if (i >= 0 && i < set.Count) set.RemoveAt(i);
            Tidy(st, which);
        }

        /// <summary>What entry i is: an artist's window or door (<paramref name="kind"/>, null: the generator's), or none.</summary>
        public static void Set(FacadeStyle st, SetOf which, int i, string? kind, bool none = false)
        {
            var set = Own(st, which);
            if (i < 0 || i >= set.Count) return;
            set[i].none = none && which != SetOf.Doors; set[i].kind = set[i].none ? null : kind;
            Tidy(st, which);
        }

        /// <summary>Entry i's weight (0 or more: 0 leaves it out).</summary>
        public static void Weigh(FacadeStyle st, SetOf which, int i, double weight)
        {
            var set = Own(st, which);
            if (i >= 0 && i < set.Count) set[i].weight = System.Math.Max(0, weight);
            Tidy(st, which);
        }

        /// <summary>The ground floor's windows the floors above's again.</summary>
        public static void FollowAbove(FacadeStyle st) => st.groundSet = null;

        /// <summary>
        /// A window or door set left with one entry, a window or door, goes back to being the style's one; an empty one to
        /// none (the ground floor's: to the floors above's).
        /// </summary>
        static void Tidy(FacadeStyle st, SetOf which)
        {
            switch (which)
            {
                case SetOf.Ground:
                    if (st.groundSet is { Count: 0 }) st.groundSet = null;
                    break;
                case SetOf.Doors:
                    if (st.doorSet is { Count: <= 1 } d) { st.doorKind = d.Count == 1 ? d[0].kind : null; st.doorSet = null; }
                    break;
                default:
                    if (st.windowSet is { Count: 0 }) st.windowSet = null;
                    else if (st.windowSet is { Count: 1 } w && !w[0].none) { st.windowKind = w[0].kind; st.windowSet = null; }
                    break;
            }
        }
    }
}
