#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Generate;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// Merged far-distance cells (docs/CITY.md §3; SPEC §6.3): every building's LOD2 massing in a square of the ground
    /// goes into one mesh, so the far city is one draw call a cell. Each vertex keeps its building's tag, so the shader
    /// hides a building inside the cell while it shows a finer LOD, and a cell never rebuilds for a LOD change, only when
    /// one of its buildings changes. A cell's mesh is split to stay under 65,535 vertices (16-bit indices), never
    /// through a building.
    /// </summary>
    public static class Cells
    {
        /// <summary>The side of a cell, in metres (the prototype's).</summary>
        public const double Size = 128;
        public const int MaxVerts = 65535;

        /// <summary>The cell a building belongs to, by the centre of its bounds.</summary>
        public static (int x, int z) KeyOf(double cx, double cz, double size = Size) => ((int)Math.Floor(cx / size), (int)Math.Floor(cz / size));

        /// <summary>A member of a cell: its LOD2 mesh, its tag (table index + 2 × LOD_TAG), and its rows' places in the table.</summary>
        public readonly struct Member
        {
            public readonly Lod2Mesh Mesh; public readonly int Tag; public readonly int[] Rows;
            public Member(Lod2Mesh mesh, int tag, int[] rows) { Mesh = mesh; Tag = tag; Rows = rows; }
        }

        /// <summary>The members merged into as few meshes as fit under <paramref name="maxVerts"/> each.</summary>
        public static List<Lod2Mesh> Merge(IEnumerable<Member> members, int maxVerts = MaxVerts)
        {
            var out_ = new List<Lod2Mesh>(); Lod2Mesh? cur = null;
            foreach (var m in members)
            {
                var src = m.Mesh; if (src.Verts == 0) continue;
                if (cur == null || (cur.Verts > 0 && cur.Verts + src.Verts > maxVerts)) { cur = new Lod2Mesh { Tags = new List<int>() }; out_.Add(cur); }
                int b = cur.Verts;
                cur.P.AddRange(src.P); cur.N.AddRange(src.N); cur.Fac.AddRange(src.Fac); cur.Fac2.AddRange(src.Fac2);
                foreach (int sl in src.Slot) cur.Slot.Add(m.Rows[sl]);
                for (int i = 0; i < src.Verts; i++) cur.Tags!.Add(m.Tag);
                foreach (int ix in src.I) cur.I.Add(b + ix);
            }
            return out_;
        }
    }
}
