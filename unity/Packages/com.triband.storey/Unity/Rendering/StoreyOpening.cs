#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Generate;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// An artist-made window or street door (docs/EDITOR.md §6.9): a mesh that fills the openings of every building whose
    /// style picks it, in place of the generator's own windows or doors. Create one with
    /// <i>Assets › Create › Storey › Window or Door</i> and give it a mesh.
    ///
    /// The mesh: pivot at the bottom middle of the hole in the wall, +Y up, +Z out of the wall (Z 0 on the wall's outer
    /// face, negative into the reveal), metres. Each submesh is a part in one of the style's colours (<see cref="parts"/>):
    /// its material isn't used, as the building's colours come from the palette. The mesh is copied in when it is set
    /// (<see cref="Bake"/>), so it needn't be readable in a build.
    /// </summary>
    [CreateAssetMenu(menuName = "Storey/Window or Door", fileName = "Window", order = 30)]
    public sealed class StoreyOpening : ScriptableObject
    {
        [Serializable]
        public sealed class Part
        {
            [Tooltip("The style colour this part takes.")]
            public ColorSlot colour = ColorSlot.Frame;
            [Tooltip("Part of the door itself: left out where the doorway stays open (walk-in storeys, which the player walks through).")]
            public bool leaf;
        }

        [Serializable]
        sealed class Baked
        {
            public Vector3[] p = Array.Empty<Vector3>(), n = Array.Empty<Vector3>();
            public int[] counts = Array.Empty<int>();   // triangles' indices per submesh, one after the other in tris
            public int[] tris = Array.Empty<int>();
        }

        [SerializeField, HideInInspector] string id = "";
        [Tooltip("A street door; otherwise a window.")]
        public bool door;
        [Tooltip("The mesh, as above: pivot at the bottom middle of the hole, +Z out of the wall.")]
        public Mesh? mesh;
        [Tooltip("LOD1's mesh (the shell seen from further away): something much simpler, or none for LOD1's plain pane.")]
        public Mesh? lod1Mesh;
        [Tooltip("The mesh was made facing −Z: turn it round.")]
        public bool facesBack;
        [Tooltip("The colour of each submesh, in order. Glass shows the window shader's rooms on shell storeys and is see-through on walk-in ones.")]
        public List<Part> parts = new List<Part>();
        [Tooltip("The hole in the wall the mesh fills, in metres (from the mesh's bounds when it is set).")]
        public Vector2 size = new Vector2(1.2f, 1.6f);
        [Tooltip("Stretch the mesh to each opening; off, it keeps its size, centred on the opening's bottom (set the style's window width to match).")]
        public bool stretch = true;

        [SerializeField, HideInInspector] Baked lod0 = new Baked(), lod1 = new Baked();
        [SerializeField, HideInInspector] bool hasLod1;

        /// <summary>The id a style stores; made once, when the asset is first set up, and kept through renames.</summary>
        public string Id => id;

        /// <summary>Give the asset its id, if it has none yet.</summary>
        public void EnsureId() { if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N"); }

        /// <summary>The submeshes named after what they look like: the part each likely is (Glass, Door, Trim, Frame).</summary>
        public static Part Guess(string material)
        {
            string m = material.ToLowerInvariant();
            if (m.Contains("glass") || m.Contains("pane")) return new Part { colour = ColorSlot.Glass, leaf = m.Contains("leaf") || m.Contains("door") };
            if (m.Contains("leaf") || m.Contains("door")) return new Part { colour = ColorSlot.Door, leaf = true };
            if (m.Contains("handle") || m.Contains("metal")) return new Part { colour = ColorSlot.Metal, leaf = m.Contains("leaf") || m.Contains("door") };
            if (m.Contains("sill") || m.Contains("trim")) return new Part { colour = ColorSlot.Trim };
            if (m.Contains("wall") || m.Contains("brick")) return new Part { colour = ColorSlot.Wall };
            return new Part { colour = ColorSlot.Frame };
        }

        /// <summary>
        /// Copy the meshes in (editor: the meshes are readable there) and register the kind. The editor calls it when the
        /// asset changes.
        /// </summary>
        public void Bake()
        {
            EnsureId();
            lod0 = Copy(mesh);
            hasLod1 = lod1Mesh != null;
            lod1 = hasLod1 ? Copy(lod1Mesh) : new Baked();
            Register();
        }

        Baked Copy(Mesh? m)
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

        /// <summary>The engine-free kind the generator places.</summary>
        public OpeningKind ToKind() => new OpeningKind
        {
            id = id, name = name, door = door, w = Mathf.Max(0.05f, size.x), h = Mathf.Max(0.05f, size.y), stretch = stretch,
            lod0 = ToMesh(lod0), lod1 = hasLod1 ? ToMesh(lod1) : null,
        };

        OpeningKind.Mesh ToMesh(Baked b)
        {
            var m = new OpeningKind.Mesh { P = new P3[b.p.Length], N = new P3[b.p.Length] };
            float turn = facesBack ? -1 : 1;   // half a turn about +Y: x and z change sign, winding kept
            for (int i = 0; i < b.p.Length; i++)
            {
                var p = b.p[i]; m.P[i] = new P3(p.x * turn, p.y, p.z * turn);
                var n = i < b.n.Length ? b.n[i] : Vector3.forward; m.N[i] = new P3(n.x * turn, n.y, n.z * turn);
            }
            int at = 0;
            for (int s = 0; s < b.counts.Length; s++)
            {
                var part = s < parts.Count ? parts[s] : new Part();
                var t = new int[b.counts[s]]; Array.Copy(b.tris, at, t, 0, t.Length); at += t.Length;
                m.parts.Add(new OpeningKind.Part { slot = part.colour, leaf = part.leaf && door, tris = t });
            }
            return m;
        }

        /// <summary>Make the kind available to the generator (sites do it for the openings they use).</summary>
        public void Register() { if (!string.IsNullOrEmpty(id)) OpeningKinds.Register(ToKind()); }

        void OnEnable() => Register();
    }
}
