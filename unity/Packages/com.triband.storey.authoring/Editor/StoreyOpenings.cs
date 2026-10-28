#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Unity;
using Triband.Storey.Validate;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The project's artist-made windows and doors (<see cref="StoreyOpening"/>, docs/EDITOR.md §6.9): found and
    /// registered when the editor loads, each with an id of its own (<see cref="StoreyAssetIds"/>: a copy is a window of
    /// its own), baked again when what they are made from is reimported (their prefabs, and whatever those use), made from
    /// prefabs in one go (docs/OPENINGS.md §3.5), and kept in each site's list of the ones its layout uses, so builds include
    /// them.
    /// </summary>
    [InitializeOnLoad]
    internal static class StoreyOpenings
    {
        static StoreyOpenings()
        {
            EditorApplication.delayCall += () => { foreach (var o in All()) o.Register(); };
            EditorApplication.projectChanged += () => all = null;
        }

        /// <summary>
        /// Every Storey Opening asset in the project, by name, each with an id of its own. Searched once and kept until the
        /// project changes: the inspector asks on every redraw, and a search of a big project's AssetDatabase is slow.
        /// </summary>
        public static List<StoreyOpening> All()
        {
            if (all != null && all.All(o => o != null)) return all;
            var found = AssetDatabase.FindAssets("t:" + nameof(StoreyOpening)).Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .Select(p => AssetDatabase.LoadAssetAtPath<StoreyOpening>(p)).Where(o => o != null).Select(o => o!).ToList();
            StoreyAssetIds.Settle(found, o => o.Id, (o, id) => o.SetId(id), o => false, null, "window or door");
            return all = found.Where(o => o.Id.Length > 0).OrderBy(o => o.name).ToList();   // one not saved yet waits for its id
        }
        static List<StoreyOpening>? all;

        /// <summary>Search again next time (the project changed).</summary>
        public static void Changed() { all = null; dependencies.Clear(); }

        /// <summary>The openings made from what was just imported, baked again: a model, a prefab, or anything a prefab uses.</summary>
        public static void Rebake(ISet<string> imported)
        {
            foreach (var p in imported) dependencies.Remove(p);   // a prefab saved may use other things now
            foreach (var o in All())
            {
                if (!MadeFrom(o, imported)) continue;
                o.Bake(); EditorUtility.SetDirty(o);
            }
        }

        static bool MadeFrom(StoreyOpening o, ISet<string> imported)
        {
            foreach (var src in new UnityEngine.Object?[] { o.prefab, o.lod1Prefab, o.mesh, o.lod1Mesh })
            {
                if (src == null) continue;
                string path = AssetDatabase.GetAssetPath(src);
                if (imported.Contains(path)) return true;
                if (src is GameObject && Dependencies(path).Any(imported.Contains)) return true;
            }
            return false;
        }

        // what each prefab uses (its meshes, materials, nested prefabs and a variant's base), asked once
        static readonly Dictionary<string, string[]> dependencies = new Dictionary<string, string[]>(StringComparer.Ordinal);
        static string[] Dependencies(string path)
        {
            if (!dependencies.TryGetValue(path, out var d)) dependencies[path] = d = AssetDatabase.GetDependencies(new[] { path }, true);
            return d;
        }

        /// <summary>Add an opening to a site's list, if it isn't there.</summary>
        public static void Keep(StoreySite? site, StoreyOpening o)
        {
            if (site == null || site.openings.Contains(o)) return;
            Undo.RecordObject(site, "Keep " + o.name);
            site.openings.Add(o);
            EditorUtility.SetDirty(site);
        }

        /// <summary>A site's list made to match what its layout's styles use (on save).</summary>
        public static void Sync(StoreySite site, StoreyDocument doc)
        {
            var used = new HashSet<string>();
            foreach (var b in doc.buildings)
            {
                foreach (var s in new[] { b.style }.Concat(b.floors.Select(f => f.style)))
                {
                    if (s == null) continue;
                    if (s.windowKind != null) used.Add(s.windowKind);
                    if (s.doorKind != null) used.Add(s.doorKind);
                    foreach (var set in new[] { s.windowSet, s.groundSet, s.doorSet })
                        if (set != null) foreach (var en in set) if (en.kind != null && !en.none) used.Add(en.kind);
                }
            }
            var want = All().Where(o => used.Contains(o.Id)).ToList();
            if (want.Count == site.openings.Count && want.All(site.openings.Contains)) return;
            site.openings = want;
            EditorUtility.SetDirty(site);
        }

        /// <summary>
        /// A window or door for each prefab selected, beside it: its door guessed from its name and its leaf objects, its
        /// parts from its materials, its hole from its Storey Cutout (or its meshes' bounds).
        /// </summary>
        [MenuItem("Assets/Create/Storey/Window or Door from Prefab", false, 31)]
        static void FromPrefabs()
        {
            var made = new List<StoreyOpening>();
            foreach (var go in Selection.objects.OfType<GameObject>())
            {
                string path = AssetDatabase.GetAssetPath(go);
                if (string.IsNullOrEmpty(path)) continue;
                var o = ScriptableObject.CreateInstance<StoreyOpening>();
                o.prefab = go;
                o.door = Door(go);
                o.slice = true;   // what is round its hole keeps its size
                string dir = System.IO.Path.GetDirectoryName(path)!.Replace('\\', '/');
                AssetDatabase.CreateAsset(o, AssetDatabase.GenerateUniqueAssetPath($"{dir}/{go.name} ({(o.door ? "door" : "window")}).asset"));
                made.Add(o);
            }
            if (made.Count == 0) return;
            Changed(); All();   // each takes its asset's GUID as its id
            foreach (var o in made) { o.Bake(); EditorUtility.SetDirty(o); }
            AssetDatabase.SaveAssets();
            Selection.objects = made.ToArray();
        }

        [MenuItem("Assets/Create/Storey/Window or Door from Prefab", true)]
        static bool CanMakeFromPrefabs() => Selection.objects.Any(x => x is GameObject && AssetDatabase.Contains(x));

        /// <summary>A prefab that is likely a door: named so, or its Storey Cutout names leaf objects.</summary>
        internal static bool Door(GameObject go)
        {
            var c = go.GetComponent<StoreyCutout>();
            return go.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0 || (c != null && c.leaf.Count > 0);
        }
    }


    /// <summary>
    /// A Storey Opening's inspector: its prefab (or mesh), its parts' colours, and what it found: its hole, its triangles, and
    /// what will look wrong or cost too much (docs/OPENINGS.md §3.5). Any change bakes it again. Check on a building tries it
    /// on a block and lists faces that would flicker.
    /// </summary>
    [CustomEditor(typeof(StoreyOpening))]
    internal sealed class StoreyOpeningEditor : UnityEditor.Editor
    {
        // the last Check on a building of each asset, until it is baked again
        static readonly Dictionary<StoreyOpening, KindTrial.Result> trials = new Dictionary<StoreyOpening, KindTrial.Result>();

        public override void OnInspectorGUI()
        {
            var o = (StoreyOpening)target;
            StoreyOpenings.All();   // a new or copied one takes an id of its own first
            var mesh0 = o.mesh; var prefab0 = o.prefab;
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            Field("door");
            Field("prefab");
            if (o.prefab == null) Field("mesh");
            Field(o.prefab != null ? "lod1Prefab" : "lod1Mesh");
            Field("facesBack");
            serializedObject.ApplyModifiedProperties();
            bool changed = EditorGUI.EndChangeCheck();
            changed |= Fit(o);
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            if (o.prefab == null)
            {
                Field("parts"); Field("size");
                if (o.slice || !o.stretch) Field("borders");
            }
            serializedObject.ApplyModifiedProperties();
            changed |= EditorGUI.EndChangeCheck();
            if (o.prefab != prefab0 && o.prefab != null && !o.door && StoreyOpenings.Door(o.prefab)) o.door = true;
            if (o.prefab == null && o.mesh != mesh0 && o.mesh != null) Fill(o);
            if (o.prefab != null) changed |= Parts(o);
            bool made = o.prefab != null || o.mesh != null;
            if (changed || (made && OpeningKinds.Get(o.Id) == null)) { o.Bake(); EditorUtility.SetDirty(o); trials.Remove(o); }
            if (!made)
            {
                EditorGUILayout.HelpBox("Give it a prefab: every object under it with a mesh, where it is in the prefab, with a Storey Cutout on its root saying where the hole is. Or one mesh, its pivot at the bottom middle of the hole. Either way +Y up, +Z out of the wall, in metres.", MessageType.Info);
                return;
            }
            var k = OpeningKinds.Get(o.Id) ?? o.ToKind();
            var c = OpeningKinds.Check(k);
            string what = o.prefab != null ? $"{o.objects} object{(o.objects == 1 ? "" : "s")}, {o.parts.Count} part{(o.parts.Count == 1 ? "" : "s")}" : $"{o.parts.Count} part{(o.parts.Count == 1 ? "" : "s")}";
            string lod1 = k.lod1 == null ? "; LOD1 shows the plain pane"
                : $"; LOD1 {c.lod1Tris}{(o.door ? "" : k.lod1OnWall ? ", on the flat wall" : ", in a hole cut in the wall")}{(k.lod1Covers ? "" : $" with the plain {(o.door ? "door" : "pane")} where it leaves the hole open")}";
            string slices = (o.slice || !o.stretch) && o.borders != Vector4.zero ? $" Slice lines {o.borders.x:0.00}, {o.borders.y:0.00}, {o.borders.z:0.00} and {o.borders.w:0.00} m in from its sides." : "";
            EditorGUILayout.HelpBox($"{c.tris} triangles ({what}){lod1}. The hole is {o.size.x:0.00} × {o.size.y:0.00} m, from {o.holeFrom}.{slices} Every {(o.door ? "street door" : "window")} of a style that picks it gets one.", MessageType.None);
            Warnings(o, k, c);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (o.prefab != null && GUILayout.Button(new GUIContent("Open Prefab", "Edit the prefab and its Storey Cutout; saving it bakes this again"))) AssetDatabase.OpenAsset(o.prefab);
                if (GUILayout.Button(new GUIContent("Check on a building", "Build a 20 × 12 m block of six storeys with it, count its triangles against the generator's own, and look for faces that flicker"))) trials[o] = KindTrial.Run(k);
                if (o.prefab == null && GUILayout.Button(new GUIContent("Name the parts again", "Guess each part's colour from its material's name, and the size from the mesh"))) { Undo.RecordObject(o, "Name the parts"); Fill(o); o.Bake(); EditorUtility.SetDirty(o); trials.Remove(o); }
            }
            if (trials.TryGetValue(o, out var t))
            {
                string flicker = t.overlaps.Count == 0 ? "No faces overlap." : $"{t.overlaps.Count} pair{(t.overlaps.Count == 1 ? "" : "s")} of faces overlap and flicker, the first at {t.overlaps[0].At}.";
                EditorGUILayout.HelpBox($"On a 20 × 12 m block of six storeys: LOD0 {t.lod0:N0} triangles ({t.plainLod0:N0} with the generator's own), LOD1 {t.lod1:N0} ({t.plainLod1:N0}). {flicker}", t.overlaps.Count == 0 ? MessageType.Info : MessageType.Warning);
            }
        }

        void Field(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), true);

        static readonly GUIContent[] Fits =
        {
            new GUIContent("Stretch", "Stretched to each opening, all of it: for a window that is only a window"),
            new GUIContent("Slice", "Sliced into each opening: what lies between the slice lines stretches; what lies outside them (shutters, a sill, a frame's members) keeps its size and moves with the hole's sides"),
            new GUIContent("Keep size", "The hole takes its own size; the style decides where windows go and their sill's height. Sliced where an opening has to give (a narrow bay, a band)"),
        };

        /// <summary>How it is fitted to each opening: stretched, sliced, or kept to its size. Returns whether it changed.</summary>
        static bool Fit(StoreyOpening o)
        {
            int at = !o.stretch ? 2 : o.slice ? 1 : 0;
            int pick = EditorGUILayout.Popup(new GUIContent("Fit", "How it is fitted to each opening"), at, Fits);
            if (pick == at) return false;
            Undo.RecordObject(o, "How it fits");
            o.stretch = pick != 2; o.slice = pick == 1;
            return true;
        }

        /// <summary>
        /// A prefab's parts, one a material (or a Model Remapper colour): a style's colour or its own, and on a door whether
        /// it is the leaf. Returns whether one changed.
        /// </summary>
        static bool Parts(StoreyOpening o)
        {
            bool changed = false;
            EditorGUILayout.LabelField(new GUIContent("Parts", "Each of the prefab's materials, or each palette colour of a model the Model Remapper coloured, takes one of the style's colours or keeps its own: the materials themselves aren't used. Glass is always the style's: it shows the window shader's rooms on shell storeys and is see-through on walk-in ones."), EditorStyles.boldLabel);
            foreach (var p in o.parts)
            {
                if (o.door)
                {
                    bool leaf = GUILayout.Toggle(p.leaf, new GUIContent("Leaf: " + p.label, "Part of the door itself, wherever it is: left out where the doorway stays open. The Storey Cutout's leaf objects are, too."), EditorStyles.miniButton);
                    if (leaf != p.leaf) { Undo.RecordObject(o, "The part's leaf"); p.leaf = leaf; changed = true; }
                }
                changed |= StoreyPartColours.Row(o, p.key, p.label, ref p.colour, ref p.own, true, StoreyPartColours.PaletteOf(p.key));
            }
            return changed;
        }

        static void Warnings(StoreyOpening o, OpeningKind k, OpeningKinds.Checked c)
        {
            foreach (var line in o.notes.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) EditorGUILayout.HelpBox(line, MessageType.Info);
            if (o.prefab != null && o.holeFrom == "the meshes' bounds")
                EditorGUILayout.HelpBox("The hole is the meshes' bounds. Add a Storey Cutout to the prefab's root to say where it is, so what goes round it (shutters, a sill) stays outside it.", MessageType.Info);
            if (o.wallFaces > 0)
                EditorGUILayout.HelpBox($"{o.wallFaces} face{(o.wallFaces == 1 ? " lies" : "s lie")} on the wall's outer face round the hole, facing out: {(o.wallFaces == 1 ? "it flickers" : "they flicker")} against the wall. Move {(o.wallFaces == 1 ? "it" : "them")} a centimetre out.", MessageType.Warning);
            if (c.behind)
                EditorGUILayout.HelpBox("Something reaches behind the wall's back, 0.3 m in from its face: it shows inside the rooms.", MessageType.Warning);
            if (c.uncovered > 0.01)
                EditorGUILayout.HelpBox($"{c.uncovered:P0} of the hole is open: on a shell or filled storey that shows into the building. Fill it (glass, a panel), or fit the cutout to the window.", MessageType.Warning);
            if (c.deepGlass)
                EditorGUILayout.HelpBox($"Some glass sits deeper than {OpeningKinds.ShellReveal * 100:0.#} cm into the wall: on a shell or filled storey the reveal stops there, and the hole shows beside the glass.", MessageType.Warning);
            if (!o.door && !c.glass)
                EditorGUILayout.HelpBox("No part is Glass: its windows show no rooms behind them (docs/EDITOR.md §6.8).", MessageType.Info);
            if (o.door && !c.leaf)
                EditorGUILayout.HelpBox("No part is the leaf: on a walk-in storey the player walks through the closed door. Name the leaf objects in the prefab's Storey Cutout, or tick Leaf on a part.", MessageType.Warning);
            if (c.tris > 140)
                EditorGUILayout.HelpBox($"{c.tris} triangles: more than the generator's most dressed window (140), and a block of six storeys has about 156 windows.", MessageType.Warning);
            if (!o.door && k.lod1 != null && !k.lod1OnWall)
                EditorGUILayout.HelpBox("LOD1 reaches into the wall, so LOD1 cuts the wall round every window: about 12 triangles a window more, and most buildings on screen are at LOD1. A LOD1 that stays in front of the wall's face (shutters, a sill, a pediment) lies on the flat wall with the plain pane, whose recess the shader draws.", MessageType.Info);
            if (k.lod1 != null && c.lod1Tris > 12)
                EditorGUILayout.HelpBox($"LOD1 has {c.lod1Tris} triangles a window: most buildings on screen are at LOD1, where the plain pane is 2.", MessageType.Warning);
        }

        /// <summary>A mesh's parts from the model's materials' names, the size from the mesh's bounds.</summary>
        static void Fill(StoreyOpening o)
        {
            var m = o.mesh!; var names = new string[m.subMeshCount];
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(m));
            if (model != null)
                foreach (var f in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh != m) continue;
                    var r = f.GetComponent<MeshRenderer>(); if (r == null) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < names.Length && i < mats.Length; i++) names[i] = mats[i] != null ? mats[i].name : "";
                }
            o.parts = names.Select((n, i) => { var p = StoreyOpening.Guess(n ?? ""); p.key = "#" + i; return p; }).ToList();
            o.size = new Vector2(Mathf.Max(0.1f, m.bounds.size.x), Mathf.Max(0.1f, m.bounds.max.y));
            o.door = o.door || names.Any(n => n != null && n.ToLowerInvariant().Contains("door"));
        }
    }
}
