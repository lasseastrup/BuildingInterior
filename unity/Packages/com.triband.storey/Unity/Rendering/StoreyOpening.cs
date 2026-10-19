#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Generate;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// An artist-made window or street door (docs/EDITOR.md §6.9): fills the openings of every building whose style picks
    /// it, in place of the generator's own windows or doors. Make one from prefabs with <i>Assets › Create › Storey ›
    /// Window or Door from Prefab</i>, or create one with <i>Assets › Create › Storey › Window or Door</i> and give it a
    /// prefab or a mesh.
    ///
    /// A prefab: every object under it with a mesh, where it is in the prefab (docs/OPENINGS.md §3); its Storey Cutout (or
    /// a BuildingCutoutBox) says where the hole is, so its pivot can be anywhere. A mesh: its pivot at the bottom middle of
    /// the hole. Either way +Y up, +Z out of the wall (Z 0 on the wall's outer face, negative into the reveal), metres.
    /// Each part (a prefab's material, a mesh's submesh) takes one of the style's colours (<see cref="parts"/>): the
    /// materials themselves aren't used, as the building's colours come from the palette. The geometry is baked in when it
    /// is set (<c>Bake</c>), and the prefab and mesh references are the editor's: they don't ship in a build.
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
            [Tooltip("A palette colour it keeps whatever the style (a palette id, or a CSS colour without Color Pipeline); empty takes the style colour above. Glass always takes the style's.")]
            public string own = "";
            /// <summary>The part of the source this row colours: a prefab's material (its asset's GUID and local id), or a mesh's submesh ("#" and its number).</summary>
            [HideInInspector] public string key = "";
            /// <summary>What the artist calls it: the material's name.</summary>
            [HideInInspector] public string label = "";
        }

        [Serializable]
        sealed class Baked
        {
            public Vector3[] p = Array.Empty<Vector3>(), n = Array.Empty<Vector3>();
            public int[] counts = Array.Empty<int>();   // triangles' indices per part, one after the other in tris
            public int[] tris = Array.Empty<int>();
            // KindBake's (baked 1): each part's row key; 1 where it is a leaf object's, 2 where it keeps its shape, round its anchor
            public string[] keys = Array.Empty<string>();
            public int[] flags = Array.Empty<int>();
            public Vector3[] anchors = Array.Empty<Vector3>();
        }

        [SerializeField, HideInInspector] string id = "";
        [Tooltip("A street door; otherwise a window.")]
        public bool door;
#if UNITY_EDITOR
        // the editor's alone: baked in when set (Bake), they don't ship in a build (docs/PERFORMANCE.md §11.2)
        [Tooltip("The window or door as a prefab: every object under it with a mesh, where it is in the prefab. Its Storey Cutout says where the hole is. It wins over Mesh.")]
        public GameObject? prefab;
        [Tooltip("LOD1's prefab (the shell seen from further away): something much simpler. None takes the prefab's LOD Group's LOD 1 if it has one, or else LOD1's plain pane.")]
        public GameObject? lod1Prefab;
        [Tooltip("The window or door as one mesh: pivot at the bottom middle of the hole, +Z out of the wall.")]
        public Mesh? mesh;
        [Tooltip("LOD1's mesh (the shell seen from further away): something much simpler, or none for LOD1's plain pane.")]
        public Mesh? lod1Mesh;
        // what the last bake found, for the inspector
        [SerializeField, HideInInspector] internal string holeFrom = "", notes = "";
        [SerializeField, HideInInspector] internal int objects, hiddenFaces, wallFaces;
#endif
        [Tooltip("It was made facing −Z: turn it round.")]
        public bool facesBack;
        [Tooltip("The colour of each part: a prefab's materials, a mesh's submeshes in order. Glass shows the window shader's rooms on shell storeys and is see-through on walk-in ones.")]
        public List<Part> parts = new List<Part>();
        [Tooltip("The hole in the wall it fills, in metres: a prefab's from its Storey Cutout, a mesh's from its bounds when it is set.")]
        public Vector2 size = new Vector2(1.2f, 1.6f);
        [Tooltip("Fit it to each opening. Off (Keep size), the hole takes its own size: the style still decides where windows go and their sill's height. A door keeps the standard doorway's width (1.6 m) and takes its own height.")]
        public bool stretch = true;
        [Tooltip("Fitted by slicing, not stretching: what lies between the slice lines stretches; what lies outside them (shutters, a sill, a frame's members) keeps its size and moves with the hole's sides.")]
        public bool slice;
        [Tooltip("The slice lines, in from the hole's left, right, bottom and top (x, y, z, w), metres: a prefab's from its Storey Cutout.")]
        public Vector4 borders;

        [SerializeField, HideInInspector] Baked lod0 = new Baked(), lod1 = new Baked();
#pragma warning disable CS0649   // a player's are never assigned in code: Bake (editor) sets them, the asset brings them
        [SerializeField, HideInInspector] bool hasLod1;
        // how the geometry was baked: 0, the mesh copied as it is (turned round at ToMesh); 1, by KindBake, in the opening's space
        [SerializeField, HideInInspector] int baked;
#pragma warning restore CS0649

        /// <summary>
        /// The id a style stores, kept through renames. The editor gives a new asset its GUID, and a copy its own (one
        /// made before kept its original's: <see cref="SetId"/>).
        /// </summary>
        public string Id => id;

        /// <summary>Give the asset an id, if it has none yet (one the editor hasn't seen: a new GUID).</summary>
        public void EnsureId() { if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N"); }

        /// <summary>
        /// Give it its id (the editor does, so that no two hold one). The kind under its old id stays registered: it is the
        /// one this was copied from.
        /// </summary>
        public void SetId(string value)
        {
            if (value == id) return;
            id = value;
            Register();
        }

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

#if UNITY_EDITOR
        /// <summary>
        /// Set by the editor as it loads: bakes the asset through <see cref="KindBake"/>, its prefab or its mesh, with what
        /// only the editor can read (the materials' ids), and keeps <see cref="parts"/> to the parts it finds.
        /// </summary>
        internal static Action<StoreyOpening>? Baker;

        /// <summary>
        /// Bake the prefab or the mesh in (editor: they are readable there) and register the kind. The editor calls it when
        /// the asset changes, and when what it is made from is reimported.
        /// </summary>
        public void Bake()
        {
            EnsureId();
            if (Baker != null) Baker(this);
            else
            {
                // before the editor's baker is installed: the mesh copied as it is
                lod0 = Copy(mesh); hasLod1 = lod1Mesh != null; lod1 = hasLod1 ? Copy(lod1Mesh) : new Baked(); baked = 0;
            }
            Register();
        }

        /// <summary>What the editor's bake made: LOD0's parts, LOD1's (null: none), and the hole they fill.</summary>
        internal void Store(KindBake.Result made, KindBake.Result? lod1Made, Vector2 hole)
        {
            lod0 = From(made); hasLod1 = lod1Made != null; lod1 = lod1Made != null ? From(lod1Made) : new Baked();
            size = hole; baked = 1;
        }

        static Baked From(KindBake.Result r)
        {
            int n = r.parts.Count;
            var b = new Baked { p = new Vector3[r.P.Count], n = new Vector3[r.P.Count], counts = new int[n], keys = new string[n], flags = new int[n], anchors = new Vector3[n] };
            for (int i = 0; i < r.P.Count; i++) { b.p[i] = V(r.P[i]); b.n[i] = V(r.N[i]); }
            var all = new List<int>();
            for (int s = 0; s < n; s++)
            {
                var part = r.parts[s];
                b.counts[s] = part.tris.Count; b.keys[s] = part.key; all.AddRange(part.tris);
                b.flags[s] = (part.leaf ? 1 : 0) | (part.anchor != null ? 2 : 0);
                if (part.anchor is P3 a) b.anchors[s] = V(a);
            }
            b.tris = all.ToArray();
            return b;
        }

        static Vector3 V(P3 p) => new Vector3((float)p.x, (float)p.y, (float)p.z);

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
#endif

        /// <summary>The engine-free kind the generator places.</summary>
        public OpeningKind ToKind() => new OpeningKind
        {
            id = id, name = name, door = door, w = Mathf.Max(0.05f, size.x), h = Mathf.Max(0.05f, size.y), stretch = stretch, slice = slice,
            borderL = Mathf.Max(0, borders.x), borderR = Mathf.Max(0, borders.y), borderB = Mathf.Max(0, borders.z), borderT = Mathf.Max(0, borders.w),
            lod0 = ToMesh(lod0), lod1 = hasLod1 ? ToMesh(lod1) : null,
        };

        OpeningKind.Mesh ToMesh(Baked b)
        {
            var m = new OpeningKind.Mesh { P = new P3[b.p.Length], N = new P3[b.p.Length] };
            // a mesh copied as it is is turned round here (KindBake turns it as it bakes): half a turn about +Y, x and z
            // change sign, winding kept
            float turn = facesBack && baked == 0 ? -1 : 1;
            for (int i = 0; i < b.p.Length; i++)
            {
                var p = b.p[i]; m.P[i] = new P3(p.x * turn, p.y, p.z * turn);
                var n = i < b.n.Length ? b.n[i] : Vector3.forward; m.N[i] = new P3(n.x * turn, n.y, n.z * turn);
            }
            int at = 0;
            for (int s = 0; s < b.counts.Length; s++)
            {
                var row = Row(b, s);
                var t = new int[b.counts[s]]; Array.Copy(b.tris, at, t, 0, t.Length); at += t.Length;
                bool leafObject = s < b.flags.Length && (b.flags[s] & 1) != 0;
                P3? anchor = s < b.flags.Length && (b.flags[s] & 2) != 0 && s < b.anchors.Length ? new P3(b.anchors[s].x, b.anchors[s].y, b.anchors[s].z) : (P3?)null;
                m.parts.Add(new OpeningKind.Part { slot = row?.colour ?? ColorSlot.Frame, own = string.IsNullOrEmpty(row?.own) ? null : row!.own, leaf = door && (leafObject || row?.leaf == true), anchor = anchor, tris = t });
            }
            return m;
        }

        /// <summary>The row that colours baked part <paramref name="s"/>: by its key (KindBake's), or by its place (a mesh copied as it is).</summary>
        Part? Row(Baked b, int s)
        {
            if (s >= b.keys.Length) return s < parts.Count ? parts[s] : null;
            foreach (var p in parts) if (p.key == b.keys[s]) return p;
            return null;
        }

        /// <summary>Make the kind available to the generator (sites do it for the openings they use).</summary>
        public void Register() { if (!string.IsNullOrEmpty(id)) OpeningKinds.Register(ToKind()); }

        void OnEnable() => Register();
    }
}
