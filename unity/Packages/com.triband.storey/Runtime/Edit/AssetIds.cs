#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Triband.Storey.Edit
{
    /// <summary>
    /// Unique ids for the assets a layout names by id: the artist's windows and doors, and facade details (docs/EDITOR.md
    /// §6.9, §6.12). The editor keeps them by this rule. A copy of an asset (Ctrl+D, or its file copied in) holds its
    /// original's id, and without the rule the two would be one kind to every layout. Engine-free.
    /// </summary>
    public static class AssetIds
    {
        public struct Entry
        {
            /// <summary>The asset's path and GUID ("" while it isn't saved), and the id it holds ("" for none yet).</summary>
            public string path, guid, id;
            /// <summary>Imported since ids were last settled: a copy just made, or a file brought in.</summary>
            public bool fresh;
            /// <summary>One of the package's own, which keeps its id whatever else holds it.</summary>
            public bool package;
        }

        /// <summary>An asset that takes a new id: its GUID.</summary>
        public struct Change
        {
            public int asset;
            public string id;
            /// <summary>The asset that kept the id this one held; -1 when it held none, or one only the package's may hold.</summary>
            public int keeper;
            /// <summary>Whether the keeper is known to be the original (the package's, its GUID's, or not just imported) rather than the first by path.</summary>
            public bool sure;
        }

        /// <summary>
        /// The assets that take a new id, and the id. One with no id takes its GUID, as does one holding an id that only
        /// the package's may hold (<paramref name="reserved"/>). Where several hold one id, one keeps it and the others take
        /// their GUIDs. The one that keeps it is the package's, else the one whose GUID the id is (its copies hold it too),
        /// else one that wasn't just imported (the copy was), else the first by path. Every other id is kept as it is, so
        /// the layouts naming it still find it.
        /// </summary>
        public static List<Change> Settle(IReadOnlyList<Entry> assets, Func<string, bool>? reserved = null)
        {
            var changes = new List<Change>();
            var ids = new string[assets.Count];
            for (int i = 0; i < assets.Count; i++)
            {
                var a = assets[i]; ids[i] = a.id;
                if (a.guid.Length > 0 && a.guid != a.id && (a.id.Length == 0 || (!a.package && reserved != null && reserved(a.id))))
                {
                    ids[i] = a.guid;
                    changes.Add(new Change { asset = i, id = a.guid, keeper = -1, sure = true });
                }
            }
            int Rank(int i, string id) => assets[i].package ? 0 : assets[i].guid == id ? 1 : !assets[i].fresh ? 2 : 3;
            // a new id is a GUID, which only its own asset can hold already; a second pass settles any that still meet
            for (int pass = 0; pass < 4; pass++)
            {
                bool again = false;
                foreach (var g in Enumerable.Range(0, ids.Length).Where(i => ids[i].Length > 0).GroupBy(i => ids[i], StringComparer.Ordinal))
                {
                    if (g.Count() < 2) continue;
                    var order = g.OrderBy(i => Rank(i, g.Key)).ThenBy(i => assets[i].path, StringComparer.Ordinal).ToList();
                    int keep = order[0];
                    bool sure = order.Count(i => Rank(i, g.Key) == Rank(keep, g.Key)) == 1 && Rank(keep, g.Key) < 3;
                    foreach (int i in order.Skip(1))
                    {
                        if (assets[i].guid.Length == 0 || assets[i].guid == ids[i]) continue;   // not saved yet, or the package's holds its GUID
                        ids[i] = assets[i].guid; again = true;
                        changes.RemoveAll(c => c.asset == i);
                        changes.Add(new Change { asset = i, id = ids[i], keeper = keep, sure = sure });
                    }
                }
                if (!again) break;
            }
            return changes;
        }
    }
}
