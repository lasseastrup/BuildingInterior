#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// A kind of facade detail (SPEC §4.4, docs/EDITOR.md §6.12): one of the generator's own five, which code draws, or
    /// one an artist made, a mesh in parts, each part in one of the style's colours. An artist's mesh is the detail's
    /// own: x along the wall (centred on the pivot), y up, z out of the wall (0 at the wall's outer face), in metres,
    /// with front faces wound as the generator's. Where the pivot goes depends on the <see cref="anchor"/>: on a wall,
    /// where it was clicked; up the wall, at each storey's floor; over an opening, at its middle,
    /// <see cref="Details.OverLift"/> above its top.
    /// </summary>
    public sealed class DetailKind
    {
        /// <summary>A part of an artist's mesh: its triangles (into <see cref="Mesh.P"/>) and its colour.</summary>
        public sealed class Part
        {
            public ColorSlot slot;
            public int[] tris = Array.Empty<int>();
        }

        public sealed class Mesh
        {
            public P3[] P = Array.Empty<P3>(), N = Array.Empty<P3>();
            public List<Part> parts = new List<Part>();
        }

        /// <summary>The id a layout stores: the generator's own are "ac", "vent", "dish", "escape" and "awning"; an artist's is its asset's.</summary>
        public string id = "", name = "";
        public Details.Anchor anchor;
        /// <summary>Where a style's rules put it, at the density the style gives it; <see cref="Details.Rule.None"/>: by hand only.</summary>
        public Details.Rule rule;
        /// <summary>
        /// Its size in metres: across, up and out of the wall. It is what placement keeps clear, clamps and shows as the
        /// ghost. 0 across: as wide as the opening it is over; 0 up: as high as the storeys it climbs.
        /// </summary>
        public double w, h, d;
        /// <summary>How far up from its pivot an artist's mesh starts (negative: it hangs below the pivot); 0 for the generator's own.</summary>
        public double bottom;
        /// <summary>An artist's mesh is fitted across the opening it is over, or to each storey's height up the wall; otherwise it keeps its size.</summary>
        public bool stretch;
        /// <summary>An artist's mesh; null for the generator's own, which code draws.</summary>
        public Mesh? lod0;
        /// <summary>LOD1's mesh; null takes <see cref="lod0"/>.</summary>
        public Mesh? lod1;
    }

    /// <summary>
    /// The facade details a layout can use, by id: the generator's own five, always there, and the artist-made ones (the
    /// project's Facade Detail assets register here). A placed detail of a kind that isn't registered isn't built, and is
    /// kept in the layout; a style's rule for one places nothing.
    /// </summary>
    public static class DetailKinds
    {
        /// <summary>The generator's own, by the ids layouts store (as the prototype writes them).</summary>
        public const string Ac = "ac", Vent = "vent", Dish = "dish", Escape = "escape", Awning = "awning";

        /// <summary>The generator's own five, in the order the Facade tab lists them.</summary>
        public static readonly IReadOnlyList<string> BuiltIn = new[] { Ac, Vent, Dish, Escape, Awning };

        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DetailKind> all = new System.Collections.Concurrent.ConcurrentDictionary<string, DetailKind>(StringComparer.Ordinal);   // read from the build threads too

        static DetailKinds()
        {
            foreach (var k in new[]
            {
                new DetailKind { id = Ac, name = "AC unit", w = 0.75, h = 0.55, d = 0.45, anchor = Details.Anchor.Wall, rule = Details.Rule.UnderWindows },
                new DetailKind { id = Vent, name = "Vent", w = 0.5, h = 0.5, d = 0.16, anchor = Details.Anchor.Wall, rule = Details.Rule.BareWall },
                new DetailKind { id = Dish, name = "Dish", w = 0.6, h = 0.6, d = 0.4, anchor = Details.Anchor.Wall },
                new DetailKind { id = Escape, name = "Fire escape", w = 2.6, h = 0, d = 1.1, anchor = Details.Anchor.Tier },
                new DetailKind { id = Awning, name = "Awning", w = 0, h = 0.5, d = 1.0, anchor = Details.Anchor.Opening, stretch = true },
            })
                all[k.id] = k;
        }

        /// <summary>Goes up with every change, so sites know to build again.</summary>
        public static int Version { get; private set; }

        /// <summary>One of the generator's own five: always registered, and never replaced.</summary>
        public static bool IsBuiltIn(string? id) => id == Ac || id == Vent || id == Dish || id == Escape || id == Awning;

        /// <summary>An artist's kind's id: 32 hex digits (the asset's GUID), as a style's rules key it.</summary>
        public static bool IsAssetId(string? id)
        {
            if (id == null || id.Length != 32) return false;
            foreach (char c in id) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        /// <summary>A layout's kind as the id it names: the generator's own in any case ("AC" is "ac"), any other as it is.</summary>
        public static string Id(string kind)
        {
            string lower = kind.ToLowerInvariant();
            return IsBuiltIn(lower) ? lower : kind;
        }

        /// <summary>Make an artist's kind available (a Facade Detail asset does, and sites do for the ones they use).</summary>
        public static void Register(DetailKind k) { if (k.id.Length == 0 || IsBuiltIn(k.id)) return; all[k.id] = k; Version++; }
        public static void Remove(string id) { if (!IsBuiltIn(id) && all.TryRemove(id, out _)) Version++; }
        public static DetailKind? Get(string? id) => id != null && all.TryGetValue(id, out var k) ? k : null;
        public static IEnumerable<DetailKind> All => all.Values;

        /// <summary>
        /// Put an artist's mesh on a wall: its pivot at <paramref name="u"/> along the wall and <paramref name="y0"/> up
        /// (site heights), on the wall's outer face; stretched by <paramref name="sx"/> across and <paramref name="sy"/>
        /// up. Each part takes its colour from the style.
        /// </summary>
        public static void Place(MeshBuilder op, Frame F, DetailKind.Mesh m, double u, double y0, double sx, double sy, Palette C)
        {
            if (op.ShapeOnly || m.P.Length == 0) return;
            double T = Dim.T_EXT;
            // the mesh's x runs along cross(up, outward), whichever way the wall's frame runs, so its front faces stay front
            double flip = F.u.x * F.w.z - F.u.z * F.w.x < 0 ? -1 : 1;
            var pts = new P3[m.P.Length]; var nrm = new P3[m.P.Length];
            for (int i = 0; i < m.P.Length; i++)
            {
                var p = m.P[i];
                pts[i] = F.At(u + flip * p.x * sx, y0 + p.y * sy, T + p.z);
                var n = i < m.N.Length ? m.N[i] : new P3(0, 0, 1);
                double nx = n.x / sx, ny = n.y / sy, nz = n.z;   // a normal under a stretch: by the inverse scale
                nrm[i] = new P3(F.u.x * flip * nx + F.w.x * nz, ny, F.u.z * flip * nx + F.w.z * nz).Normalized;
            }
            foreach (var part in m.parts)
            {
                var (pp, pn, pt) = OpeningKinds.Compact(pts, nrm, part.tris);
                if (pt.Length > 0) op.Mesh(pp, pn, pt, new Swatch(C.style, part.slot));
            }
        }
    }
}
