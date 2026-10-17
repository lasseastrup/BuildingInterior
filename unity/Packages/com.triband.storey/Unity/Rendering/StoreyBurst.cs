#nullable enable
// STOREY_NO_BURST (Player Settings ▸ Scripting Define Symbols) leaves this out: the packer then runs its own managed pass
#if !STOREY_NO_BURST
using Triband.Storey.Generate;
using Unity.Burst;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// The packer's loops compiled by Burst (docs/PERFORMANCE.md §8.8): <see cref="PackKernels"/>' reference code, called
    /// straight from the build threads, as Burst's direct calls let a static method be (no job, no main thread). They are
    /// set at start when Burst is on. With Burst off, or a method not compiled yet, the same code runs managed: the meshes
    /// are the same either way. Floats as .NET has them (<c>FloatMode.Strict</c>). Define <c>STOREY_NO_BURST</c> to leave
    /// it out of a build.
    /// </summary>
    [BurstCompile]
    public static unsafe class StoreyBurst
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Use()
        {
            if (!BurstCompiler.IsEnabled) return;
            PackKernels.Vertices = Vertices; PackKernels.Cutaways = Cutaways; PackKernels.Indices16 = Indices16; PackKernels.Finish = Finish;
            PackKernels.Compiled = true;
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        public static void Vertices(P3* pos, int* ne, int* ce, PackKernels.Colour* colours, PackKernels.Normal* normals, float tag, PackedMesh.Vertex* dst, int n, float* bounds) =>
            PackKernels.VerticesManaged(pos, ne, ce, colours, normals, tag, dst, n, bounds);

        [BurstCompile(FloatMode = FloatMode.Strict)]
        public static void Cutaways(int* kind, int* wall, PackedMesh.Cutaway* dst, int n, int mateStart, int mateCount) =>
            PackKernels.CutawaysManaged(kind, wall, dst, n, mateStart, mateCount);

        [BurstCompile(FloatMode = FloatMode.Strict)]
        public static int Indices16(int* src, ushort* dst, int n, int verts) => PackKernels.Indices16Managed(src, dst, n, verts);

        [BurstCompile(FloatMode = FloatMode.Strict)]
        public static void Finish(PackedMesh.Vertex* v, PackedMesh.Cutaway* cut, int n, byte* lo, byte* hi, int wallBase) =>
            PackKernels.FinishManaged(v, cut, n, lo, hi, wallBase);
    }
}
#endif
