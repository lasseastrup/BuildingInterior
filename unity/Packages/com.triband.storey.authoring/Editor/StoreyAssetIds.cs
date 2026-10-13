#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// Keeps the ids of the assets a layout names by id unique (<see cref="AssetIds"/>): the artist's windows and doors and
    /// the facade details. A new one takes its asset's GUID; a copy takes its own once it's imported, so it is a kind of its
    /// own; the ids layouts already name are kept. Says in the Console what it changes.
    /// </summary>
    internal static class StoreyAssetIds
    {
        // what was imported since the last import was dealt with, and of it the .asset files: a copy is told from its
        // original by having just come in
        static readonly HashSet<string> imported = new HashSet<string>(StringComparer.Ordinal), fresh = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Note what an import brought in, and deal with it once the import is done (<see cref="AfterImport"/>).</summary>
        public static void Imported(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                imported.Add(p);
                if (p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) fresh.Add(p);
            }
            EditorApplication.delayCall -= AfterImport;
            EditorApplication.delayCall += AfterImport;
        }

        /// <summary>
        /// Once an import is done (not during it: saving an asset imports it again): the windows, doors and details are
        /// searched again and their ids settled, and those made from a reimported model are baked again.
        /// </summary>
        static void AfterImport()
        {
            StoreyOpenings.Changed(); StoreyDetails.Changed();
            StoreyOpenings.All(); StoreyDetails.All();
            StoreyOpenings.Rebake(imported); StoreyDetails.Rebake(imported);
            imported.Clear(); fresh.Clear();
        }

        /// <summary>
        /// Settle the ids of every asset of one kind (all of them, as the rule compares them), saving those that change.
        /// <paramref name="package"/>: the package's own, which keep their ids; <paramref name="reserved"/>: ids only those may
        /// hold; <paramref name="what"/>: what the assets are, for the Console ("window or door").
        /// </summary>
        public static void Settle<T>(IList<T> assets, Func<T, string> id, Action<T, string> setId, Func<T, bool> package, Func<string, bool>? reserved, string what) where T : ScriptableObject
        {
            var entries = assets.Select(a =>
            {
                string p = AssetDatabase.GetAssetPath(a);
                return new AssetIds.Entry { path = p, guid = AssetDatabase.AssetPathToGUID(p) ?? "", id = id(a), fresh = fresh.Contains(p), package = package(a) };
            }).ToList();
            foreach (var c in AssetIds.Settle(entries, reserved))
            {
                var a = assets[c.asset];
                setId(a, c.id);
                EditorUtility.SetDirty(a);
                AssetDatabase.SaveAssetIfDirty(a);
                if (c.keeper < 0) continue;   // a new one, or a copy of one of the package's: nothing to say
                string kept = assets[c.keeper].name;
                if (c.sure) Debug.Log($"Storey: “{a.name}” held the same id as “{kept}”, its original, so the two were one {what}. “{a.name}” is a {what} of its own now.", a);
                else Debug.LogWarning($"Storey: “{a.name}” and “{kept}” held the same id, so the two were one {what}, and nothing shows which was copied from which. “{a.name}” is a {what} of its own now: a style that picked it may show “{kept}”, so pick it again.", a);
            }
        }
    }

    /// <summary>
    /// Storey's assets after an import: new and copied windows, doors and facade details get ids of their own, and those
    /// made from a reimported model are baked again (<see cref="StoreyAssetIds"/>).
    /// </summary>
    internal sealed class StoreyAssetImport : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Length > 0) StoreyAssetIds.Imported(imported);
        }
    }
}
