#nullable enable
using System;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
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

        // the massing layouts, matching Cells.FullVertex (48 bytes) and Cells.HalfVertex (36 bytes)
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
        internal static bool HalfMassing => halfMassing ??= SystemInfo.SupportsVertexAttributeFormat(VertexAttributeFormat.Float16, 4) && SystemInfo.SupportsVertexAttributeFormat(VertexAttributeFormat.Float16, 2);

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
            // a chunk at a time (Chunks): each fills its part of the buffer
            for (int c = 0, at = 0; at < nv; c++, at += Chunks.Size) mesh.SetVertexBufferData(pm.V.Chunk(c), 0, at, Math.Min(Chunks.Size, nv - at), 0, Quiet);
            if (pm.Cut != null) for (int c = 0, at = 0; at < nv; c++, at += Chunks.Size) mesh.SetVertexBufferData(pm.Cut.Chunk(c), 0, at, Math.Min(Chunks.Size, nv - at), 1, Quiet);
            if (pm.I16 != null)
            {
                mesh.SetIndexBufferParams(ni, IndexFormat.UInt16);
                for (int c = 0, at = 0; at < ni; c++, at += Chunks.Size) mesh.SetIndexBufferData(pm.I16.Chunk(c), 0, at, Math.Min(Chunks.Size, ni - at), Quiet);
            }
            else { mesh.SetIndexBufferParams(ni, IndexFormat.UInt32); mesh.SetIndexBufferData(pm.I32!, 0, 0, ni, Quiet); }
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, ni), Quiet);
            mesh.bounds = new Bounds(new Vector3((pm.X0 + pm.X1) / 2, (pm.Y0 + pm.Y1) / 2, (pm.Z0 + pm.Z1) / 2), new Vector3(pm.X1 - pm.X0, pm.Y1 - pm.Y0, pm.Z1 - pm.Z0));
            // no Mesh.Optimize: it crashed the editor natively in its vertex-cache pass on valid LOD0 meshes, and the generator's
            // own order is within a few percent of a vertex-cache optimiser's (measured with Forsyth's: 1.88 → 1.80 misses a triangle)
            mesh.UploadMeshData(true);
            pm.Release();   // its chunks are copied: they go to the next build
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

        /// <summary>
        /// One building's LOD2 massing, packed as a cell's piece is (<see cref="Cells.Pack"/>). <paramref name="rowMap"/> maps
        /// the mesh's local parameter rows onto table rows (none: the rows are the table's already).
        /// </summary>
        public static Mesh Upload(Lod2Mesh m, string name, int[]? rowMap)
        {
            if (rowMap == null) { int n = 0; foreach (int s in m.Slot) n = Math.Max(n, s + 1); rowMap = new int[n]; for (int i = 0; i < n; i++) rowMap[i] = i; }
            var parts = Cells.Pack(new[] { new Cells.Member(m, m.Tag, rowMap) }, HalfMassing);
            return parts.Count > 0 ? Upload(parts[0], name) : new Mesh { name = name };
        }

        /// <summary>
        /// A packed massing (a merged cell's piece, or one building's), uploaded a chunk at a time, as a detail mesh is: its
        /// chunks go back after, for the next cell.
        /// </summary>
        public static Mesh Upload(Cells.Packed pc, string name)
        {
            int nv = pc.Verts, ni = pc.Indices;
            var mesh = new Mesh { name = name, indexFormat = pc.I16 != null ? IndexFormat.UInt16 : IndexFormat.UInt32 };
            mesh.SetVertexBufferParams(nv, pc.Half != null ? MassLayoutHalf : MassLayout);
            for (int c = 0, at = 0; at < nv; c++, at += Chunks.Size)
            {
                int k = Math.Min(Chunks.Size, nv - at);
                if (pc.Half != null) mesh.SetVertexBufferData(pc.Half.Chunk(c), 0, at, k, 0, Quiet); else mesh.SetVertexBufferData(pc.Full!.Chunk(c), 0, at, k, 0, Quiet);
            }
            mesh.SetIndexBufferParams(ni, pc.I16 != null ? IndexFormat.UInt16 : IndexFormat.UInt32);
            for (int c = 0, at = 0; at < ni; c++, at += Chunks.Size)
            {
                int k = Math.Min(Chunks.Size, ni - at);
                if (pc.I16 != null) mesh.SetIndexBufferData(pc.I16.Chunk(c), 0, at, k, Quiet); else mesh.SetIndexBufferData(pc.I32!.Chunk(c), 0, at, k, Quiet);
            }
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, ni), Quiet);
            mesh.bounds = new Bounds(new Vector3((pc.X0 + pc.X1) / 2, (pc.Y0 + pc.Y1) / 2, (pc.Z0 + pc.Z1) / 2), new Vector3(pc.X1 - pc.X0, pc.Y1 - pc.Y0, pc.Z1 - pc.Z0));
            mesh.UploadMeshData(true);
            pc.Release();
            return mesh;
        }
    }
}
