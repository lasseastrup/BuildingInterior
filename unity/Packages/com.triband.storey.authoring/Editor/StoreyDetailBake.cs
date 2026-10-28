#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// A Facade Detail's bake from a prefab (docs/EDITOR.md §6.12), as a window's is (<see cref="StoreyOpeningBake"/>,
    /// docs/OPENINGS.md §3): its objects gathered with their matrices and materials, the Model Remapper's palette colours
    /// read from UV channel 3, and <see cref="KindBake"/> run over them against a whole wall (no hole), the prefab root's
    /// origin the pivot. Its parts list is kept to the parts found; a Model Remapper part keeps its palette colour. LOD1
    /// is its LOD1 prefab, or its LOD Group's LOD 1. Installed as the asset's baker as the editor loads. Storey's own.
    /// </summary>
    [InitializeOnLoad]
    internal static class StoreyDetailBake
    {
        static StoreyDetailBake() => StoreyDetail.Baker = Bake;

        static void Bake(StoreyDetail d)
        {
            if (d.prefab == null) return;
            var g = StoreyOpeningBake.FromPrefab(d.prefab, 0);
            StoreyOpeningBake.Gathered? g1 = d.lod1Prefab != null ? StoreyOpeningBake.FromPrefab(d.lod1Prefab, 0)
                : StoreyOpeningBake.Levels(d.prefab) > 1 ? StoreyOpeningBake.FromPrefab(d.prefab, 1) : null;
            var made = KindBake.Bake(g.sources, KindBake.Hole.None, d.facesBack);
            var lod1Made = g1 != null ? KindBake.Bake(g1.sources, KindBake.Hole.None, d.facesBack) : null;
            var labels = new Dictionary<string, string>(g.labels, StringComparer.Ordinal);
            var owns = new Dictionary<string, string>(g.owns, StringComparer.Ordinal);
            var keys = new List<string>(g.keys);
            if (g1 != null)
            {
                foreach (var k in g1.keys) if (!labels.ContainsKey(k)) { labels[k] = g1.labels[k]; keys.Add(k); }
                foreach (var kv in g1.owns) owns[kv.Key] = kv.Value;
            }
            Rows(d, keys, labels, owns);
            d.Store(made, lod1Made);
            d.objects = g.objects; d.hiddenFaces = made.hidden; d.wallFaces = made.onWall;
            d.notes = string.Join("\n", g.notes.Concat(g1?.notes ?? Enumerable.Empty<string>()).Distinct());
        }

        /// <summary>
        /// The parts list kept to the parts there are, in their order: a row per key, those already there kept with their
        /// colours, new ones guessed from their names; a Model Remapper part keeps its palette colour. Rows of a mesh's
        /// submeshes (no key) are dropped, as the prefab replaces the mesh.
        /// </summary>
        static void Rows(StoreyDetail d, List<string> keys, Dictionary<string, string> labels, Dictionary<string, string> owns)
        {
            var rows = new List<StoreyDetail.Part>();
            foreach (var k in keys)
            {
                string label = labels[k];
                var row = d.parts.FirstOrDefault(p => p.key == k);
                if (row == null)
                {
                    row = StoreyDetail.Guess(label); row.key = k;
                    if (owns.TryGetValue(k, out var id)) row.own = id;
                }
                row.label = label;
                rows.Add(row);
            }
            d.parts = rows;
        }

        /// <summary>
        /// A small, shallow detail (a brick patch, a plate): no deeper than 10 cm out of the wall and no bigger than half a
        /// square metre across its face. LOD1's distance can't show it, so one made from a prefab starts left out of LOD1.
        /// </summary>
        internal static bool Small(Vector3 size) => size.z <= 0.1f && size.x * size.y <= 0.5f;

        /// <summary>
        /// A Facade Detail for each prefab selected, beside it: on a wall, by hand until it is given a rule; its parts from
        /// its materials (a Model Remapper model's palette colours kept as its own), its size from what it bakes to, and a
        /// small one left out of LOD1.
        /// </summary>
        [MenuItem("Assets/Create/Storey/Facade Detail from Prefab", false, 32)]
        static void FromPrefabs()
        {
            var made = new List<StoreyDetail>();
            foreach (var go in Selection.objects.OfType<GameObject>())
            {
                string path = AssetDatabase.GetAssetPath(go);
                if (string.IsNullOrEmpty(path)) continue;
                var d = ScriptableObject.CreateInstance<StoreyDetail>();
                d.prefab = go;
                string dir = System.IO.Path.GetDirectoryName(path)!.Replace('\\', '/');
                AssetDatabase.CreateAsset(d, AssetDatabase.GenerateUniqueAssetPath($"{dir}/{go.name} (detail).asset"));
                made.Add(d);
            }
            if (made.Count == 0) return;
            StoreyDetails.Changed(); StoreyDetails.All();   // each takes its asset's GUID as its id
            foreach (var d in made) { d.Bake(); d.leaveOutOfLod1 = Small(d.size); d.Bake(); EditorUtility.SetDirty(d); }
            AssetDatabase.SaveAssets();
            Selection.objects = made.ToArray();
        }

        [MenuItem("Assets/Create/Storey/Facade Detail from Prefab", true)]
        static bool CanMakeFromPrefabs() => Selection.objects.Any(x => x is GameObject && AssetDatabase.Contains(x));
    }
}
