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

        /// <summary>How deep a shell or filled storey's reveal goes into the wall, from its outer face: where the generator's pane is.</summary>
        public const double ShellReveal = Dim.T_EXT * 0.55;

        /// <summary>What the inspector says of a kind (docs/OPENINGS.md §3.5).</summary>
        public sealed class Checked
        {
            public int tris, lod1Tris;
            /// <summary>The share of the hole nothing covers as seen from the street, 0 to 1: on a shell storey that shows into the building.</summary>
            public double uncovered;
            /// <summary>Something reaches behind the wall's back, into the rooms.</summary>
            public bool behind;
            /// <summary>Glass set deeper than a shell storey's reveal (<see cref="ShellReveal"/>): the hole shows past its side.</summary>
            public bool deepGlass;
            public bool glass, leaf;
        }

        /// <summary>
        /// What looks wrong with a kind, or costs: its triangles, the share of its hole it leaves open (drawn over a grid of
        /// about 2 cm, its faces turned to the street), whether it reaches behind the wall, and its glass and leaf.
        /// </summary>
        public static Checked Check(OpeningKind k)
        {
            var c = new Checked();
            var m = k.lod0; var P = m.P;
            foreach (var part in m.parts) c.tris += part.tris.Length / 3;
            if (k.lod1 != null) foreach (var part in k.lod1.parts) c.lod1Tris += part.tris.Length / 3;
            double w = Math.Max(0.05, k.w), h = Math.Max(0.05, k.h);
            int nx = Math.Max(8, Math.Min(200, (int)Math.Ceiling(w / 0.02))), ny = Math.Max(8, Math.Min(200, (int)Math.Ceiling(h / 0.02)));
            var covered = new bool[nx * ny];
            foreach (var part in m.parts)
            {
                if (part.slot == ColorSlot.Glass) c.glass = true;
                if (part.leaf) c.leaf = true;
                var t = part.tris;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    if ((uint)t[i] >= (uint)P.Length || (uint)t[i + 1] >= (uint)P.Length || (uint)t[i + 2] >= (uint)P.Length) continue;
                    P3 a = P[t[i]], b = P[t[i + 1]], q = P[t[i + 2]];
                    foreach (var p in new[] { a, b, q })
                    {
                        if (p.z < -Dim.T_EXT - 0.005) c.behind = true;
                        if (part.slot == ColorSlot.Glass && p.z < -ShellReveal - 0.005) c.deepGlass = true;
                    }
                    var n = P3.Cross(b - a, q - a); double len = n.Length;
                    if (len < 1e-12 || n.z / len < 0.05) continue;   // edge on, or turned away from the street
                    double x0 = Math.Min(a.x, Math.Min(b.x, q.x)), x1 = Math.Max(a.x, Math.Max(b.x, q.x)), y0 = Math.Min(a.y, Math.Min(b.y, q.y)), y1 = Math.Max(a.y, Math.Max(b.y, q.y));
                    int i0 = Math.Max(0, (int)Math.Floor((x0 + w / 2) / w * nx)), i1 = Math.Min(nx - 1, (int)Math.Ceiling((x1 + w / 2) / w * nx));
                    int j0 = Math.Max(0, (int)Math.Floor(y0 / h * ny)), j1 = Math.Min(ny - 1, (int)Math.Ceiling(y1 / h * ny));
                    for (int j = j0; j <= j1; j++)
                        for (int ii = i0; ii <= i1; ii++)
                        {
                            double px = -w / 2 + (ii + 0.5) * w / nx, py = (j + 0.5) * h / ny;
                            if (Inside2(a, b, q, px, py)) covered[j * nx + ii] = true;
                        }
                }
            }
            int n0 = 0; foreach (bool v in covered) if (!v) n0++;
            c.uncovered = (double)n0 / covered.Length;
            return c;
        }

        // the point (x, y) within triangle abc seen along Z, either winding (its edges count)
        static bool Inside2(P3 a, P3 b, P3 c, double x, double y)
        {
            double d1 = (x - b.x) * (a.y - b.y) - (a.x - b.x) * (y - b.y);
            double d2 = (x - c.x) * (b.y - c.y) - (b.x - c.x) * (y - c.y);
            double d3 = (x - a.x) * (c.y - a.y) - (c.x - a.x) * (y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
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
