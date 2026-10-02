#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// A window or street door an artist made (docs/EDITOR.md §6.9): a mesh in parts, each part in one of the style's
    /// colours. The mesh is the opening's own: x across it (centred), y up from its bottom, z out of the wall (0 at the
    /// wall's outer face), in metres, with front faces wound as the generator's. Storey's own.
    /// </summary>
    public sealed class OpeningKind
    {
        /// <summary>A part of the mesh: its triangles (into <see cref="Mesh.P"/>), its colour, and whether it is the door leaf.</summary>
        public sealed class Part
        {
            public ColorSlot slot;
            /// <summary>A door's leaf (the door itself): left out where the doorway stays open, on a walk-in storey.</summary>
            public bool leaf;
            public int[] tris = Array.Empty<int>();
        }

        public sealed class Mesh
        {
            public P3[] P = Array.Empty<P3>(), N = Array.Empty<P3>();
            public List<Part> parts = new List<Part>();
        }

        public string id = "", name = "";
        /// <summary>A street door; otherwise a window.</summary>
        public bool door;
        /// <summary>The hole in the wall the mesh fills: width and height, in metres.</summary>
        public double w = 1, h = 1.5;
        /// <summary>Stretch the mesh to each opening (true), or keep its size, centred across the opening on its bottom.</summary>
        public bool stretch = true;
        public Mesh lod0 = new Mesh();
        /// <summary>LOD1's mesh; null keeps LOD1's plain pane.</summary>
        public Mesh? lod1;
    }

    /// <summary>
    /// The artist-made windows and doors a layout can use, by id (the project's Storey Opening assets register here). A
    /// style naming a kind that isn't registered builds the standard window or door instead.
    /// </summary>
    public static class OpeningKinds
    {
        static readonly Dictionary<string, OpeningKind> all = new Dictionary<string, OpeningKind>(StringComparer.Ordinal);

        /// <summary>Goes up with every change, so sites know to build again.</summary>
        public static int Version { get; private set; }

        public static void Register(OpeningKind k) { if (k.id.Length == 0) return; all[k.id] = k; Version++; }
        public static void Remove(string id) { if (all.Remove(id)) Version++; }
        public static OpeningKind? Get(string? id) => id != null && all.TryGetValue(id, out var k) ? k : null;
        public static IEnumerable<OpeningKind> All => all.Values;

        /// <summary>The kind a style's windows (or street doors) take, if it names one that is registered and fits.</summary>
        public static OpeningKind? Of(FacadeStyle? st, bool door)
        {
            var k = Get(door ? st?.doorKind : st?.windowKind);
            return k != null && k.door == door ? k : null;
        }

        /// <summary>
        /// Put kind <paramref name="k"/>'s mesh into an opening (u0..u1 along the wall, y0..y1 up). Glass goes in the
        /// see-through glass <paramref name="gl"/> on a walk-in storey (both faces), opaque with the window shader's pane
        /// coordinates on a <paramref name="shell"/> one. A door's leaf is left out where the doorway stays open.
        /// </summary>
        public static void Place(MeshBuilder op, MeshBuilder? gl, Frame F, OpeningKind k, OpeningKind.Mesh m, double u0, double u1, double y0, double y1, Palette C, bool shell)
        {
            double sx = k.stretch ? (u1 - u0) / Math.Max(1e-3, k.w) : 1, sy = k.stretch ? (y1 - y0) / Math.Max(1e-3, k.h) : 1;
            double um = (u0 + u1) / 2, T = Dim.T_EXT;
            // the mesh's x runs along cross(up, outward), whichever way the wall's frame runs
            double flip = F.u.x * F.w.z - F.u.z * F.w.x < 0 ? -1 : 1;
            var pts = new P3[m.P.Length]; var nrm = new P3[m.N.Length];
            for (int i = 0; i < m.P.Length; i++)
            {
                var p = m.P[i];
                pts[i] = F.At(um + flip * p.x * sx, y0 + p.y * sy, T + p.z);
                var n = i < m.N.Length ? m.N[i] : new P3(0, 0, 1);
                double nx = n.x / sx, ny = n.y / sy, nz = n.z;   // a normal under a stretch: by the inverse scale
                var w = new P3(F.u.x * flip * nx + F.w.x * nz, ny, F.u.z * flip * nx + F.w.z * nz);
                nrm[i] = w.Normalized;
            }
            foreach (var whole in m.parts)
            {
                if (whole.leaf && !shell) continue;
                var (pp, pn, pt) = Compact(pts, nrm, whole.tris);
                var part = new OpeningKind.Part { slot = whole.slot, leaf = whole.leaf, tris = pt };
                if (part.slot == ColorSlot.Glass)
                {
                    if (shell || gl == null)
                    {
                        int first = op.Mesh(pp, pn, part.tris, C.glassDark);
                        if (!k.door) Facade.PaneUV(op, first, F, u0, u1, y0, y1);
                    }
                    else
                    {
                        gl.Mesh(pp, pn, part.tris, C.glass);
                        var back = new int[part.tris.Length];
                        for (int t = 0; t < back.Length; t += 3) { back[t] = part.tris[t]; back[t + 1] = part.tris[t + 2]; back[t + 2] = part.tris[t + 1]; }
                        var inv = new P3[pn.Length]; for (int i = 0; i < pn.Length; i++) inv[i] = pn[i] * -1;
                        gl.Mesh(pp, inv, back, C.glass);
                    }
                    continue;
                }
                op.Mesh(pp, pn, part.tris, new Swatch(C.style, part.slot));
            }
        }

        /// <summary>The points a part's triangles use, and its triangles into them.</summary>
        static (P3[] p, P3[] n, int[] t) Compact(P3[] pts, P3[] nrm, int[] tris)
        {
            var map = new Dictionary<int, int>(); var p = new List<P3>(); var n = new List<P3>(); var t = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                int v = tris[i];
                if (v < 0 || v >= pts.Length) { t[i] = 0; continue; }
                if (!map.TryGetValue(v, out int j)) { map[v] = j = p.Count; p.Add(pts[v]); n.Add(nrm[v]); }
                t[i] = j;
            }
            return (p.ToArray(), n.ToArray(), t);
        }
    }
}
