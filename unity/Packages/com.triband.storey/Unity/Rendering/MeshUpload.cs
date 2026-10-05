#nullable enable
using System;
using System.Runtime.InteropServices;
using Triband.Storey.Generate;
using UnityEngine;
using UnityEngine.Rendering;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Turns generator output into Unity meshes in the prototype's compact vertex formats (Plan §6.5):
    /// float position, SNorm8 normal, UNorm8 × 4 colour reference (colour row, slot and shade; docs/COLOURS.md
    /// §3.4), a float building tag, and for LOD0 the cutaway
    /// data (float4 wall, float kind, float wall id). Sixteen-bit indices where they fit. The CPU copy
    /// is released after upload; collision uses the generator's 2D segments, never the mesh.
    /// </summary>
    public static class MeshUpload
    {
        /// <summary>LOD0/LOD1 vertex: 32 bytes (LOD0 adds a 24-byte second stream).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public Vector3 position;
            public sbyte nx, ny, nz, nw;
            /// <summary>Colour reference: row low byte, row high byte, slot, shade × 128 (not RGB).</summary>
            public byte r, g, b, a;
            public float tag;
        }

        /// <summary>LOD0's second stream: the cutaway kind and the global wall id. The wall's own data (start, normal scaled by 1 + length) is in the building table, by id.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Cutaway
        {
            public float kind, wid;
        }

        /// <summary>LOD2 vertex: position, SNorm8 normal, the facade parameters and the parameter row.</summary>
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

        static sbyte S8(double v) => (sbyte)Math.Round(Math.Max(-1, Math.Min(1, v)) * 127);

        /// <summary>
        /// A detail LOD mesh. <paramref name="wallBase"/> is the first global wall id of the building's
        /// block (LOD0 only; the mesh's 1-based local ids are offset onto it; -1 = no ids). <paramref name="rowOf"/>
        /// gives the colour row of each style the swatches name (party walls name a neighbour's).
        /// </summary>
        /// <param name="optimize">Unused (it called <c>Mesh.Optimize</c>, which crashed the editor); kept so callers compile.</param>
        /// <param name="windows">Opaque panes carry their place on the pane for the window shader (the opaque meshes; not the see-through glass).</param>
        /// <param name="mates">
        /// Where the site's buildings sit in the shared table (<see cref="BuildingTable.AllocBlock"/>): the first index, and
        /// how many. A party wall's vertex names its neighbour by layout index; it is moved into the block, and a neighbour
        /// past it (a building of a neighbouring district, there for its walls only) is dropped.
        /// </param>
        public static Mesh Upload(MeshBuilder gb, string name, Func<StyleRef, int> rowOf, int wallBase = -1, bool optimize = false, bool windows = false, (int start, int count)? mates = null)
        {
            if (!gb.Welded) gb.Weld();
            int nv = gb.Verts;
            var mesh = new Mesh { name = name, indexFormat = nv > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var layout = gb.Lean
                ? new[]
                {
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 1),
                }
                : new[]
                {
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 1),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, 1),   // kind, wall id: 28 bytes a vertex, not 44
                };
            mesh.SetVertexBufferParams(nv, layout);
            var verts = new Vertex[nv];
            var bounds = new Bounds(); bool first = true;
            for (int i = 0; i < nv; i++)
            {
                var p = gb.P[i]; var n = gb.N[i]; var s = gb.C[i];
                var (cr, cg, cb, ca) = ColorRows.EncodeVertex(rowOf(s.style), s.slot, s.tone);
                var pos = new Vector3((float)p.x, (float)p.y, (float)p.z);
                verts[i] = new Vertex { position = pos, nx = S8(n.x), ny = S8(n.y), nz = S8(n.z), r = cr, g = cg, b = cb, a = ca, tag = gb.Tag };
                if (windows && s.slot == ColorSlot.Glass && gb.PaneUV.TryGetValue(i, out var uv)) { var (pa, pw, pb) = ColorRows.EncodePane(uv.u, uv.v, uv.id); verts[i].a = pa; verts[i].nw = pw; verts[i].b = pb; }
                if (first) { bounds = new Bounds(pos, Vector3.zero); first = false; } else bounds.Encapsulate(pos);
            }
            const MeshUpdateFlags flags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers;
            mesh.SetVertexBufferData(verts, 0, 0, nv, 0, flags);
            if (!gb.Lean)
            {
                var cut = new Cutaway[nv];
                for (int i = 0; i < nv; i++)
                {
                    int wi = gb.WI[i];
                    int kind = mates is (int ms, int mc) ? Lod.MateIndex.Shift(gb.K[i], ms, mc) : gb.K[i];
                    cut[i] = new Cutaway { kind = kind, wid = wi > 0 && wallBase >= 0 ? wallBase + wi - 1 : 0 };
                }
                mesh.SetVertexBufferData(cut, 0, 0, nv, 1, flags);
            }
            SetIndices(mesh, gb.I, nv, flags);
            mesh.bounds = bounds;
            // no Mesh.Optimize: it crashed the editor natively in its vertex-cache pass on valid LOD0 meshes, and the generator's
            // own order is within a few percent of a vertex-cache optimiser's (measured with Forsyth's: 1.88 → 1.80 misses a triangle)
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>A wall's cutaway data as the shader reads it from the table: its start (x, z) and its normal scaled by 1 + its length.</summary>
        public static Vector4 WallData(double[] w) => new Vector4((float)w[0], (float)w[1], (float)w[2], (float)w[3]);

        /// <summary>The LOD2 massing mesh. <paramref name="rowBase"/> maps the mesh's local parameter rows onto table rows.</summary>
        public static Mesh Upload(Lod2Mesh m, string name, int[]? rowMap)
        {
            int nv = m.Verts;
            var mesh = new Mesh { name = name, indexFormat = nv > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertexBufferParams(nv, new[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 1),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 1),
            });
            var verts = new MassVertex[nv];
            var bounds = new Bounds(); bool first = true;
            for (int i = 0; i < nv; i++)
            {
                var p = m.P[i]; var n = m.N[i];   // already quantised × 127
                var pos = new Vector3((float)p.x, (float)p.y, (float)p.z);
                verts[i] = new MassVertex
                {
                    position = pos, nx = (sbyte)n.x, ny = (sbyte)n.y, nz = (sbyte)n.z, tag = m.Tags != null ? m.Tags[i] : m.Tag,
                    fac = new Vector4((float)m.Fac[i * 4], (float)m.Fac[i * 4 + 1], (float)m.Fac[i * 4 + 2], (float)m.Fac[i * 4 + 3]),
                    fac2 = new Vector2((float)m.Fac2[i * 2], (float)m.Fac2[i * 2 + 1]),
                    slot = rowMap != null ? rowMap[m.Slot[i]] : m.Slot[i],   // a merged cell's slots are the table's rows already
                };
                if (first) { bounds = new Bounds(pos, Vector3.zero); first = false; } else bounds.Encapsulate(pos);
            }
            const MeshUpdateFlags flags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers;
            mesh.SetVertexBufferData(verts, 0, 0, nv, 0, flags);
            SetIndices(mesh, m.I, nv, flags);
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
