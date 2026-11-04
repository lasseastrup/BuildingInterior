#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey
{
    /// <summary>
    /// A layout's building ids, each building's its own, and the counter new ids come from (<see cref="StoreyDocument.seq"/>)
    /// past every id in use (docs/EDITOR.md §2). The renderer, the editor, the props and the handles know a building by its
    /// id. Two buildings with one id were one building to them, and the renderer kept only one: the other's LOD0 stayed in
    /// the scene where nothing could reach it, one more every time the layout was built again, so dragging such a building
    /// left a trail of its LOD0 behind it. Storey's tools keep the ids apart by themselves. A layout written by hand or by
    /// another tool, or put together from two, may share an id or have a counter behind its ids, whose next new building
    /// then took a building's id already. <see cref="PrototypeJson.Read"/> mends both as it reads a layout. Engine-free.
    /// Storey's own.
    /// </summary>
    public static class LayoutIds
    {
        /// <summary>
        /// Mend <paramref name="d"/> in place. First its counter goes past the number of every building's id of the form
        /// <c>b12</c> and every core's of the form <c>s7</c>, as the prototype moves its own past any layout it loads. Then each building whose id an
        /// earlier one has gets a new one, in order: the first keeps its id, so whatever names it still finds it. Returns the
        /// buildings given a new id: their index, the id they had and the one they have now. A layout that needs neither is
        /// left as it was.
        /// </summary>
        public static List<(int index, string was, string now)> Mend(StoreyDocument d)
        {
            var renamed = new List<(int, string, string)>();
            // a building's id can only be another building's, and a core's another core's: b for one, s for the other
            int next = d.seq;
            foreach (var b in d.buildings)
            {
                next = Math.Max(next, Past(b.id, 'b'));
                foreach (var s in b.shafts) next = Math.Max(next, Past(s.id, 's'));
            }
            d.seq = next;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < d.buildings.Count; i++)
            {
                var b = d.buildings[i];
                if (seen.Add(b.id)) continue;
                // the counter is past every b id now: b and the counter is free, and stays so as the counter goes on
                string was = b.id;
                b.id = "b" + d.seq++;
                seen.Add(b.id);
                renamed.Add((i, was, b.id));
            }
            return renamed;
        }

        /// <summary>
        /// The counter just past an id the tools could have made (<paramref name="prefix"/> and a number, which a new id
        /// would be again: <c>b</c> for a building's, <c>s</c> for a core's), or 0 for any other id.
        /// </summary>
        static int Past(string id, char prefix)
        {
            if (id.Length < 2 || id.Length > 10 || id[0] != prefix) return 0;
            long n = 0;
            for (int i = 1; i < id.Length; i++)
            {
                char c = id[i];
                if (c < '0' || c > '9') return 0;
                n = n * 10 + (c - '0');
            }
            return n < int.MaxValue ? (int)n + 1 : 0;
        }
    }
}
