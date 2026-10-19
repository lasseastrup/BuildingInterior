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
    /// A Storey Opening's bake (docs/OPENINGS.md §3): its prefab's objects (or its mesh) gathered with their matrices and
    /// materials, the hole read from the prefab's Storey Cutout (or a BuildingCutoutBox, or the meshes' bounds), and
    /// <see cref="KindBake"/> run over them; its parts list kept to the materials found. Installed as the asset's baker as
    /// the editor loads.
    /// </summary>
    [InitializeOnLoad]
    internal static class StoreyOpeningBake
    {
        static StoreyOpeningBake()
        {
            StoreyOpening.Baker = Bake;
            StoreyCutout.Fit = StoreyCutoutEditor.FitToMeshes;
        }

        /// <summary>
        /// What a prefab or a mesh gives the bake: its objects' meshes, its parts' keys in order with their names (and the
        /// palette colour a Model Remapper part keeps), and what was left out.
        /// </summary>
        internal sealed class Gathered
        {
            public readonly List<KindBake.Source> sources = new List<KindBake.Source>();
            public readonly List<string> keys = new List<string>();
            public readonly Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> owns = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly List<string> notes = new List<string>();
            public int objects;
            readonly Dictionary<(string material, int index), string> palette = new Dictionary<(string, int), string>();

            public void Key(string key, string label)
            {
                if (labels.ContainsKey(key)) return;
                labels[key] = label; keys.Add(key);
            }

            /// <summary>
            /// The key of a material's triangles of one palette colour (the Model Remapper's), named after the palette entry,
            /// which it keeps as its own colour; by its index when there is no palette to name it.
            /// </summary>
            public string Palette(string material, string materialName, int index)
            {
                if (palette.TryGetValue((material, index), out var key)) return key;
                var entry = StoreyColorField.PaletteEntryAt?.Invoke(index);
                key = material + "|" + (entry?.id ?? "#" + index);
                Key(key, entry?.name ?? $"{materialName}, palette colour {index}");
                if (entry != null) owns[key] = entry.Value.id;
                return palette[(material, index)] = key;
            }
        }

        /// <summary>The UV channel Color Pipeline's Model Remapper writes a vertex's palette index into (as index + 0.1), and its lit shader reads (uv3.x).</summary>
        const int RemapperUV = 3;

        /// <summary>The components a prefab of a window may have without a note: what it is drawn with, and the cutouts.</summary>
        static bool Quiet(Component c) => c is Transform || c is MeshFilter || c is MeshRenderer || c is StoreyCutout || c is LODGroup || c.GetType().Name == LegacyCutout;

        /// <summary>Flamingo's BuildingGenerator's cutout component, read by name: Storey can't reference its assembly.</summary>
        const string LegacyCutout = "BuildingCutoutBox";

        static void Bake(StoreyOpening o)
        {
            Gathered g; Gathered? g1 = null; KindBake.Hole hole, hole1; string from;
            if (o.prefab != null)
            {
                g = FromPrefab(o.prefab, 0);
                (hole, from) = HoleOf(o.prefab) ?? (KindBake.Bounds(g.sources) is { } b ? KindBake.HoleOf(b) : new KindBake.Hole(0, 0.8, 1.2, 1.6), "the meshes' bounds");
                hole1 = hole;
                if (o.lod1Prefab != null) { g1 = FromPrefab(o.lod1Prefab, 0); hole1 = HoleOf(o.lod1Prefab)?.hole ?? hole; }
                else if (Levels(o.prefab) > 1) g1 = FromPrefab(o.prefab, 1);
            }
            else if (o.mesh != null)
            {
                g = FromMesh(o.mesh);
                hole = hole1 = new KindBake.Hole(0, o.size.y / 2, o.size.x, o.size.y);   // the pivot at the hole's bottom middle
                from = "Size";
                if (o.lod1Mesh != null) g1 = FromMesh(o.lod1Mesh);
            }
            else { g = new Gathered(); hole = hole1 = new KindBake.Hole(0, o.size.y / 2, o.size.x, o.size.y); from = "Size"; }
            var made = KindBake.Bake(g.sources, hole, o.facesBack);
            var lod1Made = g1 != null ? KindBake.Bake(g1.sources, hole1, o.facesBack) : null;
            var labels = new Dictionary<string, string>(g.labels, StringComparer.Ordinal);
            var owns = new Dictionary<string, string>(g.owns, StringComparer.Ordinal);
            var keys = new List<string>(g.keys);
            if (g1 != null)
            {
                foreach (var k in g1.keys) if (!labels.ContainsKey(k)) { labels[k] = g1.labels[k]; keys.Add(k); }
                foreach (var kv in g1.owns) owns[kv.Key] = kv.Value;
            }
            Rows(o, keys, labels, owns);
            o.Store(made, lod1Made, new Vector2((float)hole.w, (float)hole.h));
            o.holeFrom = from; o.objects = g.objects; o.hiddenFaces = made.hidden; o.wallFaces = made.onWall;
            o.notes = string.Join("\n", g.notes.Concat(g1?.notes ?? Enumerable.Empty<string>()).Distinct());
        }

        /// <summary>
        /// The parts list kept to the parts there are, in their order: a row per key, those already there kept with their
        /// colours, new ones guessed from their names, the rest dropped. A Model Remapper part keeps its palette colour, unless
        /// its name says glass; its leaf comes from the leaf objects alone. A row from before rows had keys is a mesh's
        /// submesh's, by its place.
        /// </summary>
        static void Rows(StoreyOpening o, List<string> keys, Dictionary<string, string> labels, Dictionary<string, string> owns)
        {
            for (int i = 0; i < o.parts.Count; i++) if (string.IsNullOrEmpty(o.parts[i].key)) o.parts[i].key = "#" + i;
            var rows = new List<StoreyOpening.Part>();
            foreach (var k in keys)
            {
                string label = labels[k];
                var row = o.parts.FirstOrDefault(p => p.key == k);
                if (row == null)
                {
                    row = StoreyOpening.Guess(label); row.key = k;
                    if (owns.TryGetValue(k, out var id)) { row.leaf = false; if (row.colour != ColorSlot.Glass) row.own = id; }
                }
                row.label = label;
                rows.Add(row);
            }
            o.parts = rows;
        }

        /// <summary>
        /// A prefab's objects with meshes, at LOD Group level <paramref name="lod"/> (renderers in none of its levels at
        /// every level), each with its matrix into the root's space as Unity instantiates it (the root's position and
        /// rotation don't count, its scale does) and its leaf flag (under one of the Storey Cutout's leaf objects).
        /// </summary>
        internal static Gathered FromPrefab(GameObject root, int lod)
        {
            var g = new Gathered();
            var cutout = root.GetComponent<StoreyCutout>();
            var leaves = new HashSet<Transform>(cutout != null ? cutout.leaf.Where(t => t != null) : Enumerable.Empty<Transform>());
            HashSet<Renderer>? here = null, elsewhere = null;
            var group = root.GetComponent<LODGroup>();
            if (group != null)
            {
                var lods = group.GetLODs();
                here = new HashSet<Renderer>(); elsewhere = new HashSet<Renderer>();
                for (int i = 0; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) (i == lod ? here : elsewhere).Add(r);
            }
            var toRoot = Matrix4x4.Scale(root.transform.localScale) * root.transform.worldToLocalMatrix;
            int inactive = 0;
            void Walk(Transform t, bool leaf)
            {
                if (!t.gameObject.activeSelf) { inactive++; return; }
                if (t.gameObject.tag == "EditorOnly") return;
                leaf |= leaves.Contains(t);
                var f = t.GetComponent<MeshFilter>(); var r = t.GetComponent<MeshRenderer>();
                if (f != null && r != null && f.sharedMesh != null && r.enabled && (here == null || here.Contains(r) || !elsewhere!.Contains(r)))
                    Add(g, f.sharedMesh, r.sharedMaterials, toRoot * t.localToWorldMatrix, leaf, t.name);
                foreach (var c in t.GetComponents<Component>())
                    if (c != null && !Quiet(c)) g.notes.Add($"{c.GetType().Name} on {t.name} isn't carried over: Storey builds only the meshes.");
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), leaf);
            }
            Walk(root.transform, false);
            if (inactive > 0) g.notes.Add($"{inactive} inactive object{(inactive == 1 ? " is" : "s are")} left out, with what is under {(inactive == 1 ? "it" : "them")}.");
            return g;
        }

        /// <summary>One mesh as the whole window, its parts by submesh, named after the model's materials where it finds them.</summary>
        static Gathered FromMesh(Mesh mesh)
        {
            var g = new Gathered();
            var names = new string[mesh.subMeshCount];
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(mesh));
            if (model != null)
                foreach (var f in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh != mesh) continue;
                    var r = f.GetComponent<MeshRenderer>(); if (r == null) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < names.Length && i < mats.Length; i++) names[i] = mats[i] != null ? mats[i].name : "";
                }
            var s = Source(mesh, Matrix4x4.identity, false);
            for (int k = 0; k < mesh.subMeshCount; k++)
            {
                string key = "#" + k;
                g.Key(key, string.IsNullOrEmpty(names[k]) ? $"Submesh {k}" : names[k]);
                s.subs.Add(new KindBake.Sub { tris = mesh.GetTriangles(k), key = key });
            }
            g.sources.Add(s); g.objects = 1;
            return g;
        }

        static void Add(Gathered g, Mesh mesh, Material[] mats, Matrix4x4 m, bool leaf, string objectName)
        {
            var s = Source(mesh, m, leaf);
            var colours = PaletteColours(mesh);
            for (int k = 0; k < mesh.subMeshCount; k++)
            {
                if (k >= mats.Length || mats[k] == null) { g.notes.Add($"A submesh of {objectName} has no material, so Unity doesn't draw it: it is left out."); continue; }
                string key = Key(mats[k]);
                var sub = new KindBake.Sub { tris = mesh.GetTriangles(k), key = key };
                if (colours == null) g.Key(key, mats[k].name);
                else
                {
                    // the Model Remapper's: a part for each palette colour, taken from each triangle's first vertex
                    sub.keys = new string[sub.tris.Length / 3];
                    for (int t = 0; t < sub.keys.Length; t++)
                    {
                        int v = sub.tris[t * 3];
                        sub.keys[t] = g.Palette(key, mats[k].name, (uint)v < (uint)colours.Length ? colours[v] : 0);
                    }
                }
                s.subs.Add(sub);
            }
            if (mats.Length > mesh.subMeshCount) g.notes.Add($"{objectName} has more materials than its mesh has submeshes: the extra ones are left out.");
            if (!mesh.isReadable) g.notes.Add($"{mesh.name} isn't readable: if Unity says so, tick Read/Write on its model.");
            g.sources.Add(s); g.objects++;
        }

        /// <summary>
        /// The palette index each vertex of a mesh coloured by Color Pipeline's Model Remapper names (in UV channel 3, as the
        /// index + 0.1); null for a mesh it didn't colour: none there, or values that aren't such indices.
        /// </summary>
        static int[]? PaletteColours(Mesh mesh)
        {
            var uv = new List<Vector4>();
            mesh.GetUVs(RemapperUV, uv);
            if (uv.Count == 0 || uv.Count != mesh.vertexCount) return null;
            var index = new int[uv.Count];
            for (int i = 0; i < uv.Count; i++)
            {
                float x = uv[i].x, f = x - Mathf.Floor(x);
                if (x < 0 || Mathf.Abs(f - 0.1f) > 0.05f) return null;
                index[i] = (int)Mathf.Floor(x);
            }
            return index;
        }

        static KindBake.Source Source(Mesh mesh, Matrix4x4 m, bool leaf)
        {
            var v = mesh.vertices; var n = mesh.normals;
            var s = new KindBake.Source { P = new P3[v.Length], N = new P3[n.Length == v.Length ? n.Length : 0], leaf = leaf };
            for (int i = 0; i < v.Length; i++) s.P[i] = new P3(v[i].x, v[i].y, v[i].z);
            for (int i = 0; i < s.N.Length; i++) s.N[i] = new P3(n[i].x, n[i].y, n[i].z);
            s.M = new double[] { m[0, 0], m[0, 1], m[0, 2], m[0, 3], m[1, 0], m[1, 1], m[1, 2], m[1, 3], m[2, 0], m[2, 1], m[2, 2], m[2, 3] };
            return s;
        }

        /// <summary>A material's key: its asset's GUID and local id, so a renamed material keeps its colour; its name when it isn't an asset.</summary>
        internal static string Key(Material m) =>
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string guid, out long local) ? guid + ":" + local : "name:" + m.name;

        /// <summary>How many levels a prefab's LOD Group has (0 without one).</summary>
        internal static int Levels(GameObject root)
        {
            var g = root.GetComponent<LODGroup>();   // Unity's own null: the editor hands out a stand-in for a missing component
            return g != null ? g.GetLODs().Length : 0;
        }

        /// <summary>The meshes under an object, every submesh, in another object's space (a cutout's): for fitting a hole to them.</summary>
        internal static List<KindBake.Source> Meshes(Transform space, Transform under)
        {
            var list = new List<KindBake.Source>();
            var toSpace = space.worldToLocalMatrix;
            void Walk(Transform t)
            {
                if (!t.gameObject.activeSelf) return;
                var f = t.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null)
                {
                    var s = Source(f.sharedMesh, toSpace * t.localToWorldMatrix, false);
                    for (int k = 0; k < f.sharedMesh.subMeshCount; k++) s.subs.Add(new KindBake.Sub { tris = f.sharedMesh.GetTriangles(k) });
                    list.Add(s);
                }
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            Walk(under);
            return list;
        }

        /// <summary>
        /// A prefab's hole in its root's space, the root's scale applied: its Storey Cutout's, or Flamingo's
        /// BuildingCutoutBox's (its <c>_size</c> and <c>_center</c>, the middle of the hole, as its own gizmo draws it, its
        /// Z 0 on the wall's face), with where it came from; null without either.
        /// </summary>
        internal static (KindBake.Hole hole, string from)? HoleOf(GameObject root)
        {
            var sc = root.transform.localScale;
            var c = root.GetComponent<StoreyCutout>();
            if (c != null) return (new KindBake.Hole(c.center.x * sc.x, c.center.y * sc.y, Mathf.Abs(c.size.x * sc.x), Mathf.Abs(c.size.y * sc.y), c.wallFace * sc.z), "its Storey Cutout");
            if (Legacy(root) is { } old) return (new KindBake.Hole(old.center.x * sc.x, old.center.y * sc.y, Mathf.Abs(old.size.x * sc.x), Mathf.Abs(old.size.y * sc.y)), "its " + LegacyCutout);
            return null;
        }

        /// <summary>Flamingo's BuildingCutoutBox on a prefab's root: its size and middle; null when it has none.</summary>
        internal static (Vector2 size, Vector2 center)? Legacy(GameObject root)
        {
            foreach (var mb in root.transform.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != LegacyCutout) continue;   // a missing script's is null
                using var so = new SerializedObject(mb);
                var size = so.FindProperty("_size"); var center = so.FindProperty("_center");
                if (size == null || center == null) continue;
                return (size.vector2Value, center.vector2Value);
            }
            return null;
        }
    }
}
