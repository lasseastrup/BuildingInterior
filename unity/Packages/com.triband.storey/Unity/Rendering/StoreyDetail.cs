#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Generate;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// A facade detail (docs/EDITOR.md §6.12): one of Storey's own five (the package's AC unit, Vent, Dish, Fire escape
    /// and Awning, which the generator draws), or one an artist made, a mesh put on the walls by hand in the Facade tab or
    /// by a style's rules. Create one with <i>Assets › Create › Storey › Facade Detail</i> and give it a mesh.
    ///
    /// The mesh: +Y up, +Z out of the wall (Z 0 on the wall's outer face), metres, with its pivot where it meets the
    /// wall: on a wall, the bottom middle of its back; up the wall, the same, at each storey's floor; over an opening,
    /// the middle of its back, which goes 25 cm above the opening's top (clear of a window's head and a door's canopy;
    /// an awning can hang below it). Each submesh is a part in one of the style's colours (<see cref="parts"/>): its
    /// material isn't used, as the building's colours come from the palette. The mesh is copied in when it is set
    /// (<c>Bake</c>), so it needn't be readable, and the mesh references are the editor's: they don't ship in a build.
    /// </summary>
    [CreateAssetMenu(menuName = "Storey/Facade Detail", fileName = "Facade Detail", order = 31)]
    public sealed class StoreyDetail : ScriptableObject
    {
        [Serializable]
        public sealed class Part
        {
            [Tooltip("The style colour this part takes.")]
            public ColorSlot colour = ColorSlot.DetailMetal;
        }

        [Serializable]
        sealed class Baked
        {
            public Vector3[] p = Array.Empty<Vector3>(), n = Array.Empty<Vector3>();
            public int[] counts = Array.Empty<int>();   // triangles' indices per submesh, one after the other in tris
            public int[] tris = Array.Empty<int>();
        }

        [SerializeField, HideInInspector] string id = "";
        [Tooltip("Where it goes: on a wall, where it is clicked; up the wall, on every storey from the one clicked up to where the wall steps back; or over a ground-floor window or door.")]
        public Details.Anchor anchor = Details.Anchor.Wall;
        [Tooltip("Where a style's rules put it, at the density the style gives it (Facade tab › Details by rule): under upper-floor windows or in the wall between windows, for one on a wall; over ground-floor windows, for one over an opening.")]
        public Details.Rule rule = Details.Rule.None;
#if UNITY_EDITOR
        // the editor's alone: copied in when set (Bake), they don't ship in a build (docs/PERFORMANCE.md §11.2)
        [Tooltip("The mesh, as above: +Y up, +Z out of the wall, its pivot where it meets the wall.")]
        public Mesh? mesh;
        [Tooltip("LOD1's mesh (the shell seen from further away): something much simpler. None uses the mesh itself.")]
        public Mesh? lod1Mesh;
#endif
        [Tooltip("The mesh was made facing −Z: turn it round.")]
        public bool facesBack;
        [Tooltip("The colour of each submesh, in order.")]
        public List<Part> parts = new List<Part>();
        [Tooltip("Its size in metres: across, up, and out of the wall (from the mesh's bounds when it is set). Placement keeps it clear of windows and doors by this, and the ghost shows it.")]
        public Vector3 size = new Vector3(0.6f, 0.6f, 0.3f);
        [Tooltip("Over an opening: fit it across the opening and 25 cm past each side, as the awning is. Up the wall: fit it to each storey's height. Off, it keeps its size.")]
        public bool stretch;

        [SerializeField, HideInInspector] Baked lod0 = new Baked(), lod1 = new Baked();
#pragma warning disable CS0649   // a player's is never assigned in code: Bake (editor) sets it, the asset brings it
        [SerializeField, HideInInspector] bool hasLod1;
#pragma warning restore CS0649

        /// <summary>The id a layout stores: the generator's own for the package's five, the asset's GUID for the project's.</summary>
        public string Id => id;

        /// <summary>One of Storey's own five: the generator draws it, and its settings are the generator's (<see cref="DetailKinds"/>).</summary>
        public bool BuiltIn => DetailKinds.IsBuiltIn(id);

        /// <summary>
        /// Give it its id (the editor gives each of the project's its asset's GUID, so a copy gets its own). The kind under
        /// its old id stays registered: it is the one this was copied from.
        /// </summary>
        public void SetId(string value)
        {
            if (value == id) return;
            id = value;
            Register();
        }

        /// <summary>The submeshes named after what they look like: the colour each likely takes.</summary>
        public static Part Guess(string material)
        {
            string m = material.ToLowerInvariant();
            ColorSlot c =
                m.Contains("glass") || m.Contains("pane") ? ColorSlot.Glass :
                m.Contains("grille") || m.Contains("grill") || m.Contains("louvre") || m.Contains("louver") || m.Contains("mesh") ? ColorSlot.Grille :
                m.Contains("dish") ? ColorSlot.Dish :
                m.Contains("rail") ? ColorSlot.Rail :
                m.Contains("canvas") || m.Contains("fabric") || m.Contains("cloth") || m.Contains("awning") || m.Contains("trim") ? ColorSlot.Trim :
                m.Contains("dark") || m.Contains("black") || m.Contains("rubber") || m.Contains("shadow") ? ColorSlot.DetailDark :
                m.Contains("wall") || m.Contains("brick") ? ColorSlot.Wall :
                m.Contains("frame") ? ColorSlot.Frame :
                m.Contains("door") ? ColorSlot.Door :
                ColorSlot.DetailMetal;
            return new Part { colour = c };
        }

#if UNITY_EDITOR
        /// <summary>
        /// Copy the meshes in (editor: the meshes are readable there) and register the kind. The editor calls it when the
        /// asset changes.
        /// </summary>
        public void Bake()
        {
            lod0 = Copy(mesh);
            hasLod1 = lod1Mesh != null;
            lod1 = hasLod1 ? Copy(lod1Mesh) : new Baked();
            Register();
        }

        static Baked Copy(Mesh? m)
        {
            var b = new Baked();
            if (m == null) return b;
            b.p = m.vertices; b.n = m.normals;
            if (b.n.Length != b.p.Length) { m.RecalculateNormals(); b.n = m.normals; }
            var all = new List<int>(); var counts = new List<int>();
            for (int s = 0; s < m.subMeshCount; s++) { var t = m.GetTriangles(s); all.AddRange(t); counts.Add(t.Length); }
            b.tris = all.ToArray(); b.counts = counts.ToArray();
            return b;
        }
#endif

        /// <summary>The engine-free kind the generator places: for one of Storey's own five, the generator's.</summary>
        public DetailKind ToKind() => BuiltIn ? DetailKinds.Get(id)! : new DetailKind
        {
            id = id, name = name, anchor = anchor, rule = rule, stretch = stretch,
            w = Mathf.Max(0.05f, size.x), h = Mathf.Max(0.05f, size.y), d = Mathf.Max(0.02f, size.z), bottom = Bottom(lod0),
            lod0 = ToMesh(lod0), lod1 = hasLod1 ? ToMesh(lod1) : null,
        };

        /// <summary>
        /// Whether the mesh has a face flat on its top or its bottom: up the wall and fitted to each storey's height, such a
        /// face would lie on the next storey's floor line, on the floor band's top.
        /// </summary>
        public bool FlatEnds()
        {
            var b = lod0; if (b.p.Length == 0) return false;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in b.p) { if (p.y < lo) lo = p.y; if (p.y > hi) hi = p.y; }
            bool At(int i, float y) => i >= 0 && i < b.p.Length && Mathf.Abs(b.p[i].y - y) < 1e-4f;
            for (int t = 0; t + 2 < b.tris.Length; t += 3)
                foreach (var y in new[] { lo, hi })
                    if (At(b.tris[t], y) && At(b.tris[t + 1], y) && At(b.tris[t + 2], y)) return true;
            return false;
        }

        static double Bottom(Baked b)
        {
            if (b.p.Length == 0) return 0;
            float low = float.MaxValue; foreach (var p in b.p) low = Mathf.Min(low, p.y);
            return low;
        }

        DetailKind.Mesh ToMesh(Baked b)
        {
            var m = new DetailKind.Mesh { P = new P3[b.p.Length], N = new P3[b.p.Length] };
            float turn = facesBack ? -1 : 1;   // half a turn about +Y: x and z change sign, winding kept
            for (int i = 0; i < b.p.Length; i++)
            {
                var p = b.p[i]; m.P[i] = new P3(p.x * turn, p.y, p.z * turn);
                var n = i < b.n.Length ? b.n[i] : Vector3.forward; m.N[i] = new P3(n.x * turn, n.y, n.z * turn);
            }
            int at = 0;
            for (int s = 0; s < b.counts.Length; s++)
            {
                var t = new int[b.counts[s]]; Array.Copy(b.tris, at, t, 0, t.Length); at += t.Length;
                m.parts.Add(new DetailKind.Part { slot = s < parts.Count ? parts[s].colour : ColorSlot.DetailMetal, tris = t });
            }
            return m;
        }

        /// <summary>Make an artist's kind available to the generator (sites do it for the details they use); Storey's own five always are.</summary>
        public void Register() { if (!string.IsNullOrEmpty(id) && !BuiltIn) DetailKinds.Register(ToKind()); }

        void OnEnable() => Register();
    }
}
