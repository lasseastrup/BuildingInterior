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
    /// The project's facade details (<see cref="StoreyDetail"/>, docs/EDITOR.md §6.12): Storey's own five, which the
    /// package ships as Facade Detail assets, and the artist's. Found and registered when the editor loads, each of the
    /// project's given its asset's GUID as its id (so a copy is a kind of its own), baked again when its models are
    /// reimported, and kept in each site's list of the ones its layout uses, so builds include them.
    /// </summary>
    [InitializeOnLoad]
    internal static class StoreyDetails
    {
        /// <summary>Where the package keeps its five.</summary>
        internal const string Folder = "Packages/com.triband.storey/Facade Details/";

        static StoreyDetails()
        {
            EditorApplication.delayCall += () => { foreach (var d in All()) d.Register(); };
            EditorApplication.projectChanged += () => all = null;
        }

        /// <summary>
        /// Every Facade Detail asset: the package's five first, in the generator's order, then the project's by name.
        /// Searched once and kept until the project changes: the inspector asks on every redraw, and a search of a big
        /// project's AssetDatabase is slow.
        /// </summary>
        public static List<StoreyDetail> All()
        {
            if (all != null && all.All(d => d != null)) return all;
            var found = new List<StoreyDetail>();
            foreach (var path in AssetDatabase.FindAssets("t:" + nameof(StoreyDetail)).Select(AssetDatabase.GUIDToAssetPath).Distinct())
            {
                var d = AssetDatabase.LoadAssetAtPath<StoreyDetail>(path);
                if (d == null) continue;
                Own(d, path);
                found.Add(d);
            }
            // one not yet given its own id (not saved yet) is left out till it has one
            return all = found.Where(d => d.Id.Length > 0 && (!d.BuiltIn || Ours(d)))
                .OrderBy(d => d.BuiltIn ? BuiltInIndex(d.Id) : DetailKinds.BuiltIn.Count).ThenBy(d => d.name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        static List<StoreyDetail>? all;

        static int BuiltInIndex(string id) { for (int i = 0; i < DetailKinds.BuiltIn.Count; i++) if (DetailKinds.BuiltIn[i] == id) return i; return -1; }

        /// <summary>One of the package's five.</summary>
        internal static bool Ours(StoreyDetail d) => d.BuiltIn && AssetDatabase.GetAssetPath(d).StartsWith(Folder, StringComparison.Ordinal);

        /// <summary>
        /// The project's asset takes its GUID as its id: one made by copying another (or one of the package's five) would
        /// otherwise share the original's, and be the same kind. The package's keep the generator's ids.
        /// </summary>
        internal static void Own(StoreyDetail d, string path)
        {
            if (path.StartsWith(Folder, StringComparison.Ordinal)) return;
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid) || d.Id == guid) return;
            d.SetId(guid);
            EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssetIfDirty(d);
        }

        /// <summary>The palette's details: every one in <see cref="All"/>, or the generator's five if the package's assets are missing.</summary>
        public static List<(string id, string name)> Palette()
        {
            var o = All().Select(d => (d.Id, d.name)).ToList();
            foreach (var id in DetailKinds.BuiltIn)
                if (!o.Any(x => x.Item1 == id)) o.Insert(Math.Min(o.Count, BuiltInIndex(id)), (id, DetailKinds.Get(id)!.name));
            return o;
        }

        /// <summary>An artist's detail's name, or a short form of its id when it isn't in the project.</summary>
        public static string Name(string id) => DetailKinds.Get(id)?.name is string n && n.Length > 0 ? n : "Missing detail " + id.Substring(0, Math.Min(8, id.Length));

        /// <summary>Add the artist's detail with this id to a site's list, if it isn't there (Storey's own five need no keeping).</summary>
        public static void Keep(StoreySite? site, string id)
        {
            if (site == null || DetailKinds.IsBuiltIn(id)) return;
            var d = All().FirstOrDefault(x => x.Id == id);
            if (d == null || site.facadeDetails.Contains(d)) return;
            Undo.RecordObject(site, "Keep " + d.name);
            site.facadeDetails.Add(d);
            EditorUtility.SetDirty(site);
        }

        /// <summary>A site's list made to match the artist's details its layout uses, placed by hand or by a style's rules (on save).</summary>
        public static void Sync(StoreySite site, StoreyDocument doc)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in doc.buildings)
            {
                foreach (var d in b.details) used.Add(d.kind);
                foreach (var s in new[] { b.style }.Concat(b.floors.Select(f => f.style)))
                    if (s?.details?.kinds != null)
                        foreach (var kv in s.details.kinds) if (kv.Value > 0) used.Add(kv.Key);
            }
            var want = All().Where(d => !d.BuiltIn && used.Contains(d.Id)).ToList();
            if (want.Count == site.facadeDetails.Count && want.All(site.facadeDetails.Contains)) return;
            site.facadeDetails = want;
            EditorUtility.SetDirty(site);
        }

        /// <summary>A style's chance (percent) for an artist's detail; 0 takes it out of the style's rules.</summary>
        public static void SetDensity(FacadeStyle s, string id, double percent)
        {
            if (percent > 0) ((s.details ??= new DetailRules()).kinds ??= new SortedDictionary<string, double>(StringComparer.Ordinal))[id] = percent;
            else if (s.details?.kinds != null)
            {
                s.details.kinds.Remove(id);
                if (s.details.kinds.Count == 0) s.details.kinds = null;
            }
        }

        /// <summary>Where a detail goes, in words.</summary>
        public static readonly GUIContent[] Anchors =
        {
            new GUIContent("On a wall", "Where it is clicked, at the height clicked; kept clear of windows and doors"),
            new GUIContent("Up the wall", "On every storey from the one clicked up to where the wall steps back, as the fire escape"),
            new GUIContent("Over an opening", "Over a ground-floor window or door, as the awning"),
        };

        /// <summary>The rules a detail can have where it goes, and their names.</summary>
        public static (Details.Rule rule, GUIContent label)[] Rules(Details.Anchor a) => a switch
        {
            Details.Anchor.Wall => new[]
            {
                (Details.Rule.None, new GUIContent("By hand only")),
                (Details.Rule.UnderWindows, new GUIContent("Under upper-floor windows", "As AC units: under a share of the upper floors' windows, never on glass curtain walls")),
                (Details.Rule.BareWall, new GUIContent("Between windows", "As vents: in the stretches of wall between windows, near the ceiling")),
            },
            Details.Anchor.Opening => new[]
            {
                (Details.Rule.None, new GUIContent("By hand only")),
                (Details.Rule.OverOpenings, new GUIContent("Over ground-floor windows", "Over a share of the ground floor's windows (put one over a door by hand)")),
            },
            _ => new[] { (Details.Rule.None, new GUIContent("By hand only")) },
        };
    }

    /// <summary>
    /// A new or copied Facade Detail asset takes its own id at once, and a reimported model bakes the details made from
    /// its meshes again.
    /// </summary>
    internal sealed class StoreyDetailReimport : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Length == 0) return;
            var assets = imported.Where(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)).ToList();
            if (assets.Count > 0)
                EditorApplication.delayCall += () =>   // not while importing: saving the asset imports it again
                {
                    foreach (var p in assets)
                        if (AssetDatabase.LoadAssetAtPath<StoreyDetail>(p) is StoreyDetail d) StoreyDetails.Own(d, p);
                };
            var set = new HashSet<string>(imported);
            foreach (var d in StoreyDetails.All())
            {
                if (d.BuiltIn) continue;
                bool hit = (d.mesh != null && set.Contains(AssetDatabase.GetAssetPath(d.mesh))) || (d.lod1Mesh != null && set.Contains(AssetDatabase.GetAssetPath(d.lod1Mesh)));
                if (!hit) continue;
                d.Bake(); EditorUtility.SetDirty(d);
            }
        }
    }

    /// <summary>
    /// A Facade Detail's inspector. One of Storey's own five says what it is. An artist's: where it goes and its rule, in
    /// words; setting a mesh names its parts from the model's materials and takes the size from its bounds; any change
    /// bakes it again. Says what it found, and what looks wrong with the pivot.
    /// </summary>
    [CustomEditor(typeof(StoreyDetail))]
    internal sealed class StoreyDetailEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var d = (StoreyDetail)target;
            StoreyDetails.Own(d, AssetDatabase.GetAssetPath(d));
            if (d.BuiltIn && StoreyDetails.Ours(d))
            {
                var k = DetailKinds.Get(d.Id)!;
                string rule = StoreyDetails.Rules(k.anchor).FirstOrDefault(r => r.rule == k.rule).label?.text ?? "By hand only";
                EditorGUILayout.HelpBox($"One of Storey's own details: the generator draws it. {StoreyDetails.Anchors[(int)k.anchor].text}; {rule.ToLowerInvariant()}.", MessageType.Info);
                EditorGUILayout.LabelField("Make your own with Assets ▸ Create ▸ Storey ▸ Facade Detail, or duplicate this one and give the copy a mesh.", StoreyInspectorUI.Caption);
                return;
            }
            var before = d.mesh;
            bool changed = false;
            EditorGUI.BeginChangeCheck();
            int a = EditorGUILayout.Popup(new GUIContent("Goes", "Where it goes on a building"), (int)d.anchor, StoreyDetails.Anchors);
            if (a != (int)d.anchor) { Undo.RecordObject(d, "Where the detail goes"); d.anchor = (Details.Anchor)a; changed = true; }
            var rules = StoreyDetails.Rules(d.anchor);
            int at = Math.Max(0, Array.FindIndex(rules, r => r.rule == d.rule));
            if (rules[at].rule != d.rule) { d.rule = rules[at].rule; changed = true; }   // a rule that doesn't fit where it goes now
            int r = EditorGUILayout.Popup(new GUIContent("Placed by rule", "Where a style's rules put it, at the density the style gives it (Facade tab ▸ Details by rule)"), at, rules.Select(x => x.label).ToArray());
            if (r != at) { Undo.RecordObject(d, "The detail's rule"); d.rule = rules[r].rule; changed = true; }
            serializedObject.Update();
            foreach (var f in new[] { "mesh", "lod1Mesh", "facesBack", "parts", "size" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(f), true);
            if (d.anchor != Details.Anchor.Wall) EditorGUILayout.PropertyField(serializedObject.FindProperty("stretch"), true);
            serializedObject.ApplyModifiedProperties();
            changed |= EditorGUI.EndChangeCheck();
            if (d.mesh != before && d.mesh != null) Fill(d);
            if (changed || (d.Id.Length > 0 && DetailKinds.Get(d.Id) == null)) { d.Bake(); EditorUtility.SetDirty(d); }
            if (d.mesh == null) { EditorGUILayout.HelpBox(Pivot(d.anchor) + " Each submesh is one part.", MessageType.Info); return; }
            var b = d.mesh.bounds;
            int tris = 0; for (int s = 0; s < d.mesh.subMeshCount; s++) tris += d.mesh.GetTriangles(s).Length / 3;
            EditorGUILayout.HelpBox($"{tris} triangles in {d.mesh.subMeshCount} part{(d.mesh.subMeshCount == 1 ? "" : "s")}; {b.size.x:0.00} × {b.size.y:0.00} m, {b.size.z:0.00} m out of the wall. Pick it in the Facade tab's Place on walls.", MessageType.None);
            if (Mathf.Abs(b.center.x) > 0.05f || (d.anchor != Details.Anchor.Opening && Mathf.Abs(b.min.y) > 0.05f))
                EditorGUILayout.HelpBox("The pivot looks off. " + Pivot(d.anchor), MessageType.Warning);
            if (b.min.z < -0.05f)
                EditorGUILayout.HelpBox("The mesh goes into the wall: keep it at Z 0 and out (+Z), or it shows through the wall inside.", MessageType.Warning);
            if (d.anchor == Details.Anchor.Tier && d.stretch && d.FlatEnds())
                EditorGUILayout.HelpBox("Fitted to each storey, its flat top and bottom lie on the floor line, where the floor band's top is, and flicker: leave them out (a pipe needs no caps), or turn off Stretch.", MessageType.Warning);
            if (d.mesh.subMeshCount > d.parts.Count)
                EditorGUILayout.HelpBox("Some parts have no colour set: they take Detail metal.", MessageType.Warning);
            if (d.lod1Mesh == null && tris > 300)
                EditorGUILayout.HelpBox("LOD1 draws the full mesh: give it a much simpler LOD1 mesh, as many buildings are seen in LOD1 at once.", MessageType.Warning);
            if (GUILayout.Button(new GUIContent("Name the parts from the model again", "Guess each part's colour from its material's name, and the size from the mesh"))) { Undo.RecordObject(d, "Name the parts"); Fill(d); d.Bake(); EditorUtility.SetDirty(d); }
        }

        static string Pivot(Details.Anchor a) => a == Details.Anchor.Opening
            ? "Give it a mesh with its pivot at the middle of its back, where it meets the wall: +Y up, +Z out of the wall, in metres. The pivot goes 25 cm above the opening's top, clear of a window's head and a door's canopy; an awning can hang below it."
            : "Give it a mesh with its pivot at the bottom middle of its back, where it meets the wall: +Y up, +Z out of the wall, in metres.";

        /// <summary>The parts from the model's materials' names, the size from the mesh's bounds.</summary>
        static void Fill(StoreyDetail d)
        {
            var m = d.mesh!; var names = new string[m.subMeshCount];
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(m));
            if (model != null)
                foreach (var f in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh != m) continue;
                    var r = f.GetComponent<MeshRenderer>(); if (r == null) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < names.Length && i < mats.Length; i++) names[i] = mats[i] != null ? mats[i].name : "";
                }
            d.parts = names.Select(n => StoreyDetail.Guess(n ?? "")).ToList();
            var b = m.bounds;
            d.size = new Vector3(Mathf.Max(0.05f, b.size.x), Mathf.Max(0.05f, b.size.y), Mathf.Max(0.02f, b.max.z));
        }
    }
}
