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
            // made ready once (docs/PERFORMANCE.md §8.1): when the kind is registered, or first placed
            internal KindPart? ready;
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
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OpeningKind> all = new System.Collections.Concurrent.ConcurrentDictionary<string, OpeningKind>(StringComparer.Ordinal);   // read from the build threads too

        /// <summary>Goes up with every change, so sites know to build again.</summary>
        public static int Version { get; private set; }

        /// <summary>Make a kind available, its parts made ready for placing (a Storey Opening asset does). A kind whose mesh changes is registered again.</summary>
        public static void Register(OpeningKind k)
        {
            if (k.id.Length == 0) return;
            foreach (var m in new[] { k.lod0, k.lod1 }) if (m != null) foreach (var part in m.parts) KindPart.Of(m.P, m.N, part.tris, ref part.ready);
            all[k.id] = k; Version++;
        }
        public static void Remove(string id) { if (all.TryRemove(id, out _)) Version++; }
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
            var at = new KindPlace(F, (u0 + u1) / 2, y0, sx, sy);
            foreach (var whole in m.parts)
            {
                if (whole.leaf && !shell) continue;
                var part = KindPart.Of(m.P, m.N, whole.tris, ref whole.ready);
                if (whole.slot == ColorSlot.Glass)
                {
                    if (shell || gl == null)
                    {
                        int first = part.Put(op, at, C.glassDark);
                        if (!k.door) Facade.PaneUV(op, first, F, u0, u1, y0, y1);
                    }
                    else
                    {
                        part.Put(gl, at, C.glass); op.SeeThrough++;   // clear at any distance
                        part.Put(gl, at, C.glass, inside: true);
                    }
                    continue;
                }
                part.Put(op, at, new Swatch(C.style, whole.slot));
            }
        }
    }
}
