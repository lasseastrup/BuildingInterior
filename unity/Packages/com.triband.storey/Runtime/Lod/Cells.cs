#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

        /// <summary>
        /// A merged cell's vertex as the GPU takes it, its facade numbers as 16-bit floats: 36 bytes (MeshUpload's massing
        /// layout, docs/CITY.md §7).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct HalfVertex
        {
            public float x, y, z;
            public sbyte nx, ny, nz, nw;
            public float tag;
            public ushort facX, facY, facZ, facW, fac2X, fac2Y;
            public float slot;
        }

        /// <summary>The same with 32-bit floats, 48 bytes: for a GPU that can't read 16-bit floats in a vertex.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FullVertex
        {
            public float x, y, z;
            public sbyte nx, ny, nz, nw;
            public float tag;
            public float facX, facY, facZ, facW, fac2X, fac2Y;
            public float slot;
        }

        /// <summary>
        /// A piece of a merged cell packed for upload, in <see cref="Chunks"/>: its vertices in one of the two layouts, its
        /// indices 16-bit (32-bit only for a single massing past 65,535 vertices), and its bounds. <see cref="Release"/>
        /// gives the chunks back once uploaded.
        /// </summary>
        public sealed class Packed
        {
            public ChunkList<HalfVertex>? Half { get; internal set; }
            public ChunkList<FullVertex>? Full { get; internal set; }
            public ChunkList<ushort>? I16 { get; internal set; }
            public ChunkList<int>? I32 { get; internal set; }
            public int Verts { get; internal set; }
            public int Indices => I16?.Count ?? I32!.Count;
            public float X0 = float.MaxValue, Y0 = float.MaxValue, Z0 = float.MaxValue, X1 = float.MinValue, Y1 = float.MinValue, Z1 = float.MinValue;

            /// <summary>The chunks given back for the next cell or build. Nothing may read it after.</summary>
            public void Release() { Half?.Clear(); Full?.Clear(); I16?.Clear(); I32?.Clear(); }
        }

        /// <summary>
        /// The members merged as <see cref="Merge"/> merges them, into the same pieces, and packed straight into the upload
        /// layout (<see cref="HalfVertex"/> when <paramref name="half"/>, else <see cref="FullVertex"/>): the same vertices
        /// as the merged meshes uploaded, with no merged lists made in between. Those were garbage the moment the upload's
        /// arrays were made, 78 MB loading the 3,000-building test city; the chunks here go back once uploaded.
        /// </summary>
        public static List<Packed> Pack(IReadOnlyList<Member> members, bool half, int maxVerts = MaxVerts)
        {
            var out_ = new List<Packed>();
            for (int first = 0; first < members.Count;)
            {
                // the members of this piece, as Merge would take them: one at least, as many more as fit
                int last = first, verts = 0;
                for (; last < members.Count; last++)
                {
                    int v = members[last].Mesh.Verts; if (v == 0) continue;
                    if (verts > 0 && verts + v > maxVerts) break;
                    verts += v;
                }
                if (verts > 0) out_.Add(PackPiece(members, first, last, verts, half));
                first = last;
            }
            return out_;
        }

        static Packed PackPiece(IReadOnlyList<Member> members, int first, int last, int verts, bool half)
        {
            var pc = new Packed { Verts = verts };
            if (half) pc.Half = new ChunkList<HalfVertex>(); else pc.Full = new ChunkList<FullVertex>();
            if (verts <= MaxVerts) pc.I16 = new ChunkList<ushort>(); else pc.I32 = new ChunkList<int>();
            int b = 0;
            for (int k = first; k < last; k++)
            {
                var m = members[k]; var src = m.Mesh; int nv = src.Verts; if (nv == 0) continue;
                var P = src.P; var N = src.N; var F = src.Fac; var F2 = src.Fac2; var S = src.Slot;
                for (int i = 0; i < nv; i++)
                {
                    var p = P[i]; var n = N[i];   // the normals quantised × 127 already
                    float x = (float)p.x, y = (float)p.y, z = (float)p.z; int f = i * 4, g = i * 2;
                    if (half)
                        pc.Half!.Add(new HalfVertex
                        {
                            x = x, y = y, z = z, nx = (sbyte)n.x, ny = (sbyte)n.y, nz = (sbyte)n.z, tag = m.Tag,
                            facX = MeshPack.Half(F[f]), facY = MeshPack.Half(F[f + 1]), facZ = MeshPack.Half(F[f + 2]), facW = MeshPack.Half(F[f + 3]),
                            fac2X = MeshPack.Half(F2[g]), fac2Y = MeshPack.Half(F2[g + 1]), slot = m.Rows[S[i]],
                        });
                    else
                        pc.Full!.Add(new FullVertex
                        {
                            x = x, y = y, z = z, nx = (sbyte)n.x, ny = (sbyte)n.y, nz = (sbyte)n.z, tag = m.Tag,
                            facX = (float)F[f], facY = (float)F[f + 1], facZ = (float)F[f + 2], facW = (float)F[f + 3],
                            fac2X = (float)F2[g], fac2Y = (float)F2[g + 1], slot = m.Rows[S[i]],
                        });
                    if (x < pc.X0) pc.X0 = x; if (y < pc.Y0) pc.Y0 = y; if (z < pc.Z0) pc.Z0 = z;
                    if (x > pc.X1) pc.X1 = x; if (y > pc.Y1) pc.Y1 = y; if (z > pc.Z1) pc.Z1 = z;
                }
                var I = src.I;
                for (int i = 0; i < I.Count; i++)
                {
                    int ix = I[i];
                    if ((uint)ix >= (uint)nv) throw new InvalidOperationException($"a massing's index {ix} of {nv} vertices");
                    if (pc.I16 != null) pc.I16.Add((ushort)(b + ix)); else pc.I32!.Add(b + ix);
                }
                b += nv;
            }
            return pc;
        }
    }
}
