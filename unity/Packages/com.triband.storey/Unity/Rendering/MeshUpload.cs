#nullable enable
using System;
using System.Runtime.InteropServices;
using Triband.Storey.Generate;
using UnityEngine;
using UnityEngine.Rendering;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Turns generator output into Unity meshes in the prototype's compact vertex formats (Plan §6.5): float position,
    /// SNorm8 normal, UNorm8 × 4 colour reference (colour row, slot and shade; docs/COLOURS.md §3.4) and a float building
    /// tag, 24 bytes; LOD0 adds a second stream of 8, the cutaway kind and the wall id. A detail mesh comes packed
    /// (<see cref="PackedMesh"/>, made on the worker that built it): the upload fills in the colour rows and wall ids and
    /// copies. Sixteen-bit indices (a detail mesh over 65,535 vertices comes in parts). The CPU copy is released after
    /// upload; collision uses the generator's 2D segments, never the mesh.
    /// </summary>
    public static class MeshUpload
    {
        static readonly VertexAttributeDescriptor[] LeanLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 1),
        };
        static readonly VertexAttributeDescriptor[] Lod0Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, 1),   // kind, wall id: 32 bytes a vertex in all
        };

        /// <summary>LOD2 vertex where the GPU can't read 16-bit floats: position, SNorm8 normal, the facade parameters and the parameter row. 48 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MassVertex
        {
            public Vector3 position;
            public sbyte nx, ny, nz, nw;
            public float tag;   // building index + 2 × 65536: without it the shader takes the massing for building 0's LOD0
            public Vector4 fac;
            public Vector2 fac2;
            public float slot;
        }

        /// <summary>
        /// LOD2 vertex: the facade parameters as 16-bit floats (the shader reads them as floats, as it did), 36 bytes. They
        /// are metres along a wall and up a run, and a bay's width: 3 cm off at most at 64 to 128 m, a tenth of a pixel
        /// at the distances a massing shows (docs/CITY.md §7). The tag and the row stay 32-bit: they are integers past 2,048.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MassVertexHalf
        {
            public Vector3 position;
            public sbyte nx, ny, nz, nw;
            public float tag;
            public ushort facX, facY, facZ, facW;
            public ushort fac2X, fac2Y;
            public float slot;
        }

        static readonly VertexAttributeDescriptor[] MassLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 1),
        };
        static readonly VertexAttributeDescriptor[] MassLayoutHalf =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float16, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 1),
        };
        static bool? halfMassing;
        /// <summary>Whether the GPU reads 16-bit float vertex attributes (all but the oldest; asked once).</summary>
        static bool HalfMassing => halfMassing ??= SystemInfo.SupportsVertexAttributeFormat(VertexAttributeFormat.Float16, 4) && SystemInfo.SupportsVertexAttributeFormat(VertexAttributeFormat.Float16, 2);

        const MeshUpdateFlags Quiet = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers;

        /// <summary>
        /// A packed detail mesh, finished and uploaded. <paramref name="rowOf"/> gives the colour row of each style its
        /// vertices name (party walls name a neighbour's); <paramref name="wallBase"/> is the first global wall id of the
        /// building's block (LOD0 only; -1 = no ids).
        /// </summary>
        public static Mesh Upload(PackedMesh pm, string name, Func<StyleRef, int> rowOf, int wallBase = -1)
        {
            var rows = new int[pm.Styles.Length];
            for (int s = 0; s < rows.Length; s++) rows[s] = rowOf(pm.Styles[s]);
            pm.Finish(rows, wallBase);
            int nv = pm.Verts, ni = pm.Indices;
            var mesh = new Mesh { name = name, indexFormat = pm.I16 != null ? IndexFormat.UInt16 : IndexFormat.UInt32 };
            mesh.SetVertexBufferParams(nv, pm.Cut != null ? Lod0Layout : LeanLayout);
            mesh.SetVertexBufferData(pm.V, 0, 0, nv, 0, Quiet);
            if (pm.Cut != null) mesh.SetVertexBufferData(pm.Cut, 0, 0, nv, 1, Quiet);
            if (pm.I16 != null) { mesh.SetIndexBufferParams(ni, IndexFormat.UInt16); mesh.SetIndexBufferData(pm.I16, 0, 0, ni, Quiet); }
            else { mesh.SetIndexBufferParams(ni, IndexFormat.UInt32); mesh.SetIndexBufferData(pm.I32!, 0, 0, ni, Quiet); }
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, ni), Quiet);
            mesh.bounds = new Bounds(new Vector3((pm.X0 + pm.X1) / 2, (pm.Y0 + pm.Y1) / 2, (pm.Z0 + pm.Z1) / 2), new Vector3(pm.X1 - pm.X0, pm.Y1 - pm.Y0, pm.Z1 - pm.Z0));
            // no Mesh.Optimize: it crashed the editor natively in its vertex-cache pass on valid LOD0 meshes, and the generator's
            // own order is within a few percent of a vertex-cache optimiser's (measured with Forsyth's: 1.88 → 1.80 misses a triangle)
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>
        /// A mesh the generator made, packed here and uploaded as one (32-bit indices past 65,535 vertices): for meshes
        /// made on the main thread anyway (Sink's footprints). <paramref name="wallBase"/> as <see cref="Upload(PackedMesh, string, Func{StyleRef, int}, int)"/>.
        /// </summary>
        /// <param name="optimize">Unused (it called <c>Mesh.Optimize</c>, which crashed the editor); kept so callers compile.</param>
        /// <param name="windows">Opaque panes carry their place on the pane for the window shader (the opaque meshes; not the see-through glass).</param>
        /// <param name="mates">
        /// Where the site's buildings sit in the shared table (<see cref="BuildingTable.AllocBlock"/>): the first index, and
        /// how many. A party wall's vertex names its neighbour by layout index; it is moved into the block at upload, and a
        /// neighbour past it (a building of a neighbouring district, there for its walls only) is dropped.
        /// </param>
        public static Mesh Upload(MeshBuilder gb, string name, Func<StyleRef, int> rowOf, int wallBase = -1, bool optimize = false, bool windows = false, (int start, int count)? mates = null)
        {
            var parts = MeshPack.Pack(gb, windows, mates, int.MaxValue);
            if (parts.Count == 0) return new Mesh { name = name };
            return Upload(parts[0], name, rowOf, wallBase);
        }

        /// <summary>A wall's cutaway data as the shader reads it from the table: its start (x, z) and its normal scaled by 1 + its length.</summary>
        public static Vector4 WallData(double[] w) => new Vector4((float)w[0], (float)w[1], (float)w[2], (float)w[3]);

        /// <summary>The LOD2 massing mesh (a building's, or a merged cell's). <paramref name="rowMap"/> maps the mesh's local parameter rows onto table rows.</summary>
        public static Mesh Upload(Lod2Mesh m, string name, int[]? rowMap)
        {
            int nv = m.Verts;
            var mesh = new Mesh { name = name, indexFormat = nv > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var bounds = new Bounds(); bool first = true;
            void Grow(Vector3 pos) { if (first) { bounds = new Bounds(pos, Vector3.zero); first = false; } else bounds.Encapsulate(pos); }
            if (HalfMassing)
            {
                mesh.SetVertexBufferParams(nv, MassLayoutHalf);
                var verts = new MassVertexHalf[nv];
                for (int i = 0; i < nv; i++)
                {
                    var p = m.P[i]; var n = m.N[i];   // already quantised × 127
                    var pos = new Vector3((float)p.x, (float)p.y, (float)p.z);
                    int f = i * 4, g = i * 2;
                    verts[i] = new MassVertexHalf
                    {
                        position = pos, nx = (sbyte)n.x, ny = (sbyte)n.y, nz = (sbyte)n.z, tag = m.Tags != null ? m.Tags[i] : m.Tag,
                        facX = MeshPack.Half(m.Fac[f]), facY = MeshPack.Half(m.Fac[f + 1]), facZ = MeshPack.Half(m.Fac[f + 2]), facW = MeshPack.Half(m.Fac[f + 3]),
                        fac2X = MeshPack.Half(m.Fac2[g]), fac2Y = MeshPack.Half(m.Fac2[g + 1]),
                        slot = rowMap != null ? rowMap[m.Slot[i]] : m.Slot[i],   // a merged cell's slots are the table's rows already
                    };
                    Grow(pos);
                }
                mesh.SetVertexBufferData(verts, 0, 0, nv, 0, Quiet);
            }
            else
            {
                mesh.SetVertexBufferParams(nv, MassLayout);
                var verts = new MassVertex[nv];
                for (int i = 0; i < nv; i++)
                {
                    var p = m.P[i]; var n = m.N[i];
                    var pos = new Vector3((float)p.x, (float)p.y, (float)p.z);
                    verts[i] = new MassVertex
                    {
                        position = pos, nx = (sbyte)n.x, ny = (sbyte)n.y, nz = (sbyte)n.z, tag = m.Tags != null ? m.Tags[i] : m.Tag,
                        fac = new Vector4((float)m.Fac[i * 4], (float)m.Fac[i * 4 + 1], (float)m.Fac[i * 4 + 2], (float)m.Fac[i * 4 + 3]),
                        fac2 = new Vector2((float)m.Fac2[i * 2], (float)m.Fac2[i * 2 + 1]),
                        slot = rowMap != null ? rowMap[m.Slot[i]] : m.Slot[i],
                    };
                    Grow(pos);
                }
                mesh.SetVertexBufferData(verts, 0, 0, nv, 0, Quiet);
            }
            SetIndices(mesh, m.I, nv, Quiet);
            mesh.bounds = bounds;
            mesh.UploadMeshData(true);
            return mesh;
        }

        static void SetIndices(Mesh mesh, System.Collections.Generic.List<int> I, int nv, MeshUpdateFlags flags)
        {
            int ni = I.Count;
            // the upload skips Unity's index check (DontValidateIndices), and a bad index crashes natively in Mesh.Optimize
            // and on the GPU: check here, where it is a managed exception naming the mesh
            if (ni % 3 != 0) throw new InvalidOperationException($"{mesh.name}: {ni} indices is not whole triangles");
            for (int i = 0; i < ni; i++) if ((uint)I[i] >= (uint)nv) throw new InvalidOperationException($"{mesh.name}: index {I[i]} of {nv} vertices");
            if (nv > 65535)
            {
                mesh.SetIndexBufferParams(ni, IndexFormat.UInt32);
                var idx = I.ToArray();
                mesh.SetIndexBufferData(idx, 0, 0, ni, flags);
            }
            else
            {
                mesh.SetIndexBufferParams(ni, IndexFormat.UInt16);
                var idx = new ushort[ni]; for (int i = 0; i < ni; i++) idx[i] = (ushort)I[i];
                mesh.SetIndexBufferData(idx, 0, 0, ni, flags);
            }
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, ni), flags);
        }
    }
}
