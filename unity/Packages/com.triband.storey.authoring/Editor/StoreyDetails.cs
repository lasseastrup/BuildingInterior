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
    /// The project's facade details (<see cref="StoreyDetail"/>, docs/EDITOR.md §6.12): Storey's own five, which the
    /// package ships as Facade Detail assets, and the artist's. Found and registered when the editor loads, each with an
    /// id of its own (<see cref="StoreyAssetIds"/>: a copy is a detail of its own), baked again when its models are
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
            var found = AssetDatabase.FindAssets("t:" + nameof(StoreyDetail)).Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .Select(p => AssetDatabase.LoadAssetAtPath<StoreyDetail>(p)).Where(d => d != null).Select(d => d!).ToList();
            StoreyAssetIds.Settle(found, d => d.Id, (d, id) => d.SetId(id), d => AssetDatabase.GetAssetPath(d).StartsWith(Folder, StringComparison.Ordinal), DetailKinds.IsBuiltIn, "facade detail");
            // one not given its own id yet (not saved yet) is left out till it has one
            return all = found.Where(d => d.Id.Length > 0 && (!d.BuiltIn || Ours(d)))
                .OrderBy(d => d.BuiltIn ? BuiltInIndex(d.Id) : DetailKinds.BuiltIn.Count).ThenBy(d => d.name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        static List<StoreyDetail>? all;

        /// <summary>Search again next time (the project changed).</summary>
        public static void Changed() => all = null;

        /// <summary>The artist's details made from what was just imported, baked again: a model, a prefab, or anything a prefab uses.</summary>
        public static void Rebake(ISet<string> imported)
        {
            StoreyOpenings.Forget(imported);
            foreach (var d in All())
            {
                if (d.BuiltIn || !StoreyOpenings.MadeFrom(imported, d.prefab, d.lod1Prefab, d.mesh, d.lod1Mesh)) continue;
                d.Bake(); EditorUtility.SetDirty(d);
            }
        }

        static int BuiltInIndex(string id) { for (int i = 0; i < DetailKinds.BuiltIn.Count; i++) if (DetailKinds.BuiltIn[i] == id) return i; return -1; }

        /// <summary>One of the package's five.</summary>
        internal static bool Ours(StoreyDetail d) => d.BuiltIn && AssetDatabase.GetAssetPath(d).StartsWith(Folder, StringComparison.Ordinal);

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

        /// <summary>
        /// Change a style's settings for an artist's detail beside its chance (<see cref="KindRule"/>): made when first set,
        /// and taken out again when they are all back to their defaults, so the layout writes the chance alone as before.
        /// </summary>
        public static void EditRule(FacadeStyle s, string id, Action<KindRule> change)
        {
            var rules = (s.details ??= new DetailRules()).rules ??= new SortedDictionary<string, KindRule>(StringComparer.Ordinal);
            if (!rules.TryGetValue(id, out var r)) rules[id] = r = new KindRule();
            change(r);
            if (!r.IsEmpty) return;
            rules.Remove(id);
            if (rules.Count == 0) s.details.rules = null;
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
                (Details.Rule.Scatter, new GUIContent("Scattered on the walls", "As brick patches: spread evenly over every wall, so many to the square metre, clear of the openings, the band and the plinth. Its size places it, wherever its pivot is")),
                (Details.Rule.AlongTop, new GUIContent("Along the top of the wall", "In a row along the top of each tier's wall, so many to a bay; its pivot at the top of the wall, so it hangs below it")),
                (Details.Rule.AlongFoot, new GUIContent("Along the foot of the wall", "In a row along the foot of the ground floor's wall, so many to a bay, never across a doorway; its pivot on the ground")),
                (Details.Rule.Corners, new GUIContent("Up the corners", "Stacked up the building's corners, so far apart, facing out along each corner's bisector; its pivot where the corner's walls meet")),
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
    /// A Facade Detail's inspector. One of Storey's own five says what it is. An artist's: where it goes and its rule, in
    /// words; setting a mesh names its parts from the model's materials and takes the size from its bounds; any change
    /// bakes it again. Says what it found, and what looks wrong with the pivot.
    /// </summary>
    [CustomEditor(typeof(StoreyDetail))]
    internal sealed class StoreyDetailEditor : UnityEditor.Editor
    {
        // the last Check on a building of each asset, until it is baked again
        static readonly Dictionary<StoreyDetail, KindTrial.Result> trials = new Dictionary<StoreyDetail, KindTrial.Result>();

        public override void OnInspectorGUI()
        {
            var d = (StoreyDetail)target;
            StoreyDetails.All();   // a new or copied one takes an id of its own first
            if (d.BuiltIn && StoreyDetails.Ours(d))
            {
                var k = DetailKinds.Get(d.Id)!;
                string rule = StoreyDetails.Rules(k.anchor).FirstOrDefault(r => r.rule == k.rule).label?.text ?? "By hand only";
                EditorGUILayout.HelpBox($"One of Storey's built-in details, made by Storey itself. {StoreyDetails.Anchors[(int)k.anchor].text}; {rule.ToLowerInvariant()}.", MessageType.Info);
                EditorGUILayout.LabelField("Make your own with Assets ▸ Create ▸ Storey ▸ Facade Detail (or Facade Detail from Prefab), or duplicate this one and give the copy a mesh or a prefab.", StoreyInspectorUI.Caption);
                return;
            }
            var before = d.mesh;
            bool changed = false;
            if (d.prefab != null) d.mesh = null;   // the prefab wins
            EditorGUI.BeginChangeCheck();
            int a = EditorGUILayout.Popup(new GUIContent("Goes", "Where it goes on a building"), (int)d.anchor, StoreyDetails.Anchors);
            if (a != (int)d.anchor) { Undo.RecordObject(d, "Where the detail goes"); d.anchor = (Details.Anchor)a; changed = true; }
            var rules = StoreyDetails.Rules(d.anchor);
            int at = Math.Max(0, Array.FindIndex(rules, r => r.rule == d.rule));
            if (rules[at].rule != d.rule) { d.rule = rules[at].rule; changed = true; }   // a rule that doesn't fit where it goes now
            int r = EditorGUILayout.Popup(new GUIContent("Placed by rule", "Where a style's rules put it, at the density the style gives it (Facade tab ▸ Details by rule)"), at, rules.Select(x => x.label).ToArray());
            if (r != at) { Undo.RecordObject(d, "The detail's rule"); d.rule = rules[r].rule; changed = true; }
            serializedObject.Update();
            var fields = d.prefab != null ? new[] { "prefab", "lod1Prefab", "facesBack" } : new[] { "prefab", "mesh", "lod1Mesh", "facesBack", "size" };
            foreach (var f in fields) EditorGUILayout.PropertyField(serializedObject.FindProperty(f), true);
            if (d.anchor != Details.Anchor.Wall) EditorGUILayout.PropertyField(serializedObject.FindProperty("stretch"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leaveOutOfLod1"), new GUIContent("Leave out of LOD1", "For a small detail (a brick patch) that LOD1's distance can't show: many would cost the lean shell more than its walls do"));
            if (d.rule == Details.Rule.Corners)
            {
                // the corners it goes on, and bent to fit them (docs/EDITOR.md §6.12)
                float lo = d.cornerMin, hi = d.cornerMax;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(new GUIContent("Corner angles", "The corners it goes on, by the angle between their walls (90° is a rectangle's corner; going in, the notch's angle). Outside them it would float off a sharp corner or sink into a blunt one."));
                    lo = Mathf.Round(EditorGUILayout.FloatField(lo, GUILayout.Width(36)));
                    EditorGUILayout.MinMaxSlider(ref lo, ref hi, 1, 179);
                    hi = Mathf.Round(EditorGUILayout.FloatField(hi, GUILayout.Width(36)));
                }
                lo = Mathf.Clamp(Mathf.Round(lo), 1, 179); hi = Mathf.Clamp(Mathf.Round(hi), lo, 179);
                if (lo != d.cornerMin || hi != d.cornerMax) { Undo.RecordObject(d, "Corner angles"); d.cornerMin = lo; d.cornerMax = hi; changed = true; }
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bendToCorner"), new GUIContent("Bend to the corner", "Made for a 90° corner: bent to each corner's angle, each half turned onto its own wall. Off, it keeps its shape."));
            }
            serializedObject.ApplyModifiedProperties();
            changed |= EditorGUI.EndChangeCheck();
            if (d.parts.Count > 0) EditorGUILayout.LabelField(new GUIContent("Parts", d.prefab != null
                ? "Each of the prefab's materials, or each palette colour of a model the Model Remapper coloured, takes one of the style's colours or keeps its own: the materials themselves aren't used. A style can give its main colour another (Facade tab ▸ Details by rule)."
                : "Each submesh takes one of the style's colours, or keeps a palette colour of its own whatever the style: the model's materials only name them."), EditorStyles.boldLabel);
            for (int i = 0; i < d.parts.Count; i++)
            {
                var p = d.parts[i];
                changed |= StoreyPartColours.Row(d, p.key.Length > 0 ? p.key : "#" + i, p.label.Length > 0 ? p.label : "Part " + (i + 1), ref p.colour, ref p.own, false, StoreyPartColours.PaletteOf(p.key));
            }
            if (d.prefab == null && d.mesh != before && d.mesh != null) Fill(d);
            if (changed || (d.Id.Length > 0 && DetailKinds.Get(d.Id) == null)) { d.Bake(); EditorUtility.SetDirty(d); trials.Remove(d); }
            if (d.mesh == null && d.prefab == null) { EditorGUILayout.HelpBox(Pivot(d.anchor) + " Each submesh is one part. Or give it a prefab: every object under it with a mesh, its root's origin the pivot.", MessageType.Info); return; }
            var (tris, b) = d.Made();
            string what = d.prefab != null ? $"{d.objects} object{(d.objects == 1 ? "" : "s")}, {d.parts.Count} part{(d.parts.Count == 1 ? "" : "s")}" : $"{d.parts.Count} part{(d.parts.Count == 1 ? "" : "s")}";
            EditorGUILayout.HelpBox($"{tris} triangles ({what}); {b.size.x:0.00} × {b.size.y:0.00} m, {b.max.z:0.00} m out of the wall{(d.leaveOutOfLod1 ? "; left out of LOD1" : "")}. Pick it in the Facade tab's Place on walls.", MessageType.None);
            if (d.prefab != null)
            {
                foreach (var line in d.notes.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) EditorGUILayout.HelpBox(line, MessageType.Info);
                if (d.hiddenFaces > 0) EditorGUILayout.LabelField($"{d.hiddenFaces} face{(d.hiddenFaces == 1 ? "" : "s")} in the wall, or on its face turned into it, left out.", StoreyInspectorUI.Caption);
                if (d.wallFaces > 0) EditorGUILayout.HelpBox($"{d.wallFaces} face{(d.wallFaces == 1 ? " lies" : "s lie")} on the wall's outer face, facing out: {(d.wallFaces == 1 ? "it flickers" : "they flicker")} against the wall. Move {(d.wallFaces == 1 ? "it" : "them")} a centimetre out.", MessageType.Warning);
            }
            bool placedBySize = d.rule == Details.Rule.Scatter || d.rule == Details.Rule.Corners;   // its bounds place it, wherever its pivot is
            if (!placedBySize && (Mathf.Abs(b.center.x) > 0.05f || (d.anchor != Details.Anchor.Opening && d.rule != Details.Rule.AlongTop && Mathf.Abs(b.min.y) > 0.05f)))
                EditorGUILayout.HelpBox("The pivot looks off. " + Pivot(d.anchor), MessageType.Warning);
            if (!d.leaveOutOfLod1 && StoreyDetailBake.Small(d.size) && (d.rule == Details.Rule.Scatter || d.rule == Details.Rule.Corners))
                EditorGUILayout.HelpBox("A small detail many go on: LOD1's distance can't show it, and it costs every building at LOD1. Tick Leave out of LOD1.", MessageType.Info);
            // up the corners, its halves run back along the walls from where their faces meet: below Z 0 by design
            if (b.min.z < -0.05f && d.rule != Details.Rule.Corners)
                EditorGUILayout.HelpBox("The mesh goes into the wall: keep it at Z 0 and out (+Z), or it shows through the wall inside.", MessageType.Warning);
            if (d.anchor == Details.Anchor.Tier && d.stretch && d.FlatEnds())
                EditorGUILayout.HelpBox("Fitted to each storey, its flat top and bottom lie on the floor line, where the floor band's top is, and flicker: leave them out (a pipe needs no caps), or turn off Stretch.", MessageType.Warning);
            if (d.mesh != null && d.mesh.subMeshCount > d.parts.Count)
                EditorGUILayout.HelpBox("Some parts have no colour set: they take Detail metal.", MessageType.Warning);
            if (d.lod1Mesh == null && d.lod1Prefab == null && !d.leaveOutOfLod1 && tris > 300)
                EditorGUILayout.HelpBox("LOD1 draws the full mesh: give it a much simpler LOD1, or leave it out of LOD1, as many buildings are seen in LOD1 at once.", MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (d.prefab != null && GUILayout.Button(new GUIContent("Open Prefab", "Edit the prefab; saving it bakes this again"))) AssetDatabase.OpenAsset(d.prefab);
                if (GUILayout.Button(new GUIContent("Check on a building", "Build a 20 × 12 m block of six storeys with it (by its rule at 100 %, or put by hand), count its triangles against the block's own, and look for faces that flicker")))
                    trials[d] = KindTrial.Run(DetailKinds.Get(d.Id) ?? d.ToKind());
                if (d.prefab == null && GUILayout.Button(new GUIContent("Name the parts again", "Guess each part's colour from its material's name, and the size from the mesh"))) { Undo.RecordObject(d, "Name the parts"); Fill(d); d.Bake(); EditorUtility.SetDirty(d); trials.Remove(d); }
            }
            if (trials.TryGetValue(d, out var t))
            {
                string flicker = t.overlaps.Count == 0 ? "No faces overlap." : $"{t.overlaps.Count} pair{(t.overlaps.Count == 1 ? "" : "s")} of faces overlap and flicker, the first at {t.overlaps[0].At}.";
                EditorGUILayout.HelpBox($"On a 20 × 12 m block of six storeys, {t.placed} of it: LOD0 {t.lod0:N0} triangles ({t.plainLod0:N0} without), LOD1 {t.lod1:N0} ({t.plainLod1:N0}). {flicker}", t.overlaps.Count == 0 ? MessageType.Info : MessageType.Warning);
            }
        }

        static string Pivot(Details.Anchor a) => a == Details.Anchor.Opening
            ? "Give it a mesh with its pivot at the middle of its back, where it meets the wall: +Y up, +Z out of the wall, in metres. The pivot goes 25 cm above the opening's top, clear of a window's head; an awning can hang below it."
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
            d.parts = names.Select(n => { var p = StoreyDetail.Guess(n ?? ""); p.label = n ?? ""; return p; }).ToList();
            var b = m.bounds;
            d.size = new Vector3(Mathf.Max(0.05f, b.size.x), Mathf.Max(0.05f, b.size.y), Mathf.Max(0.02f, b.max.z));
        }
    }
}
