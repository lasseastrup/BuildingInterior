using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The packer's kernels (docs/PERFORMANCE.md §8.8), which Burst compiles in Unity: run here as the managed code they are,
    /// they pack every mesh as the packer's own single pass does, to the byte, and finish it the same. In the timing
    /// collection: the switch between the two is global.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class PackKernelsTests
    {
        static List<MeshBuilder> Builds()
        {
            var all = new List<MeshBuilder>();
            foreach (var f in new[] { "demo.json", "variants.json" })
            {
                var d = PrototypeJson.Read(Fixtures.Text(f)).Document; var site = new Site(d.buildings);
                foreach (var b in d.buildings) { var l0 = Lod0.Build(site, b); all.Add(l0.Op); all.Add(l0.Glass); all.Add(Lod1.Build(site, b)); }
            }
            var w = new StoreyDocument(); TestCity.Generate(w, 200, walkIn: true); var ws = new Site(w.buildings);
            foreach (var b in w.buildings.Where((x, i) => i % 10 == 0)) { var l0 = Lod0.Build(ws, b); all.Add(l0.Op); all.Add(l0.Glass); }
            return all;
        }

        static List<string> Packed(List<MeshBuilder> builds, bool kernels)
        {
            bool was = PackKernels.Compiled; PackKernels.Compiled = kernels;
            try
            {
                var out_ = new List<string>();
                foreach (var gb in builds)
                    foreach (var p in MeshPack.Pack(gb, windows: true, mates: (40, 9000), weld: false))
                    {
                        var bytes = new List<byte>();
                        for (int v = 0; v < p.Verts; v++)
                        {
                            var x = p.V[v];
                            bytes.AddRange(System.BitConverter.GetBytes(x.x)); bytes.AddRange(System.BitConverter.GetBytes(x.y)); bytes.AddRange(System.BitConverter.GetBytes(x.z));
                            bytes.AddRange(new[] { (byte)x.nx, (byte)x.ny, (byte)x.nz, (byte)x.nw, x.r, x.g, x.b, x.a }); bytes.AddRange(System.BitConverter.GetBytes(x.tag));
                            if (p.Cut != null) { bytes.AddRange(System.BitConverter.GetBytes(p.Cut[v].kind)); bytes.AddRange(System.BitConverter.GetBytes(p.Cut[v].wid)); }
                        }
                        for (int i = 0; i < p.Indices; i++) bytes.AddRange(System.BitConverter.GetBytes(p.I16 != null ? p.I16[i] : p.I32![i]));
                        string before = System.Convert.ToBase64String(bytes.ToArray());
                        p.Finish(Enumerable.Range(7, p.Styles.Length).ToArray(), 1234);
                        var fin = new List<byte>();
                        for (int v = 0; v < p.Verts; v++) { fin.Add(p.V[v].r); fin.Add(p.V[v].g); if (p.Cut != null) fin.AddRange(System.BitConverter.GetBytes(p.Cut[v].wid)); }
                        out_.Add($"{before} {string.Join(",", p.Styles.Select(s => $"{s.building}.{s.tier}"))} {string.Join(",", p.Mates)} {p.X0} {p.Y0} {p.Z0} {p.X1} {p.Y1} {p.Z1} {System.Convert.ToBase64String(fin.ToArray())}");
                        p.Release();
                    }
                return out_;
            }
            finally { PackKernels.Compiled = was; }
        }

        [Fact]
        public void TheKernelsPackAsTheSinglePassDoes()
        {
            var builds = Builds();
            var single = Packed(builds, false); var kernels = Packed(builds, true);
            Assert.True(single.Count > 100, $"{single.Count} meshes");
            Assert.Equal(single.Count, kernels.Count);
            for (int i = 0; i < single.Count; i++) Assert.True(single[i] == kernels[i], $"mesh {i} differs");
        }

        [Fact]
        public unsafe void TheBoundsAreDotNetsMinAndMax()
        {
            // −0 under +0, a NaN carried: the kernels' bounds are Math.Min and Math.Max's, whoever compiles them
            var pos = new[] { new P3(0, -0.0, 1), new P3(-0.0, 0, 1), new P3(2, 3, double.NaN) };
            var ne = new int[3]; var ce = new int[3];
            var colours = new PackKernels.Colour[1]; var normals = new PackKernels.Normal[1];
            var dst = new PackedMesh.Vertex[3];
            float x0 = float.MaxValue, y0 = x0, z0 = x0, x1 = float.MinValue, y1 = x1, z1 = x1;
            foreach (var p in pos) { float x = (float)p.x, y = (float)p.y, z = (float)p.z; x0 = System.Math.Min(x0, x); y0 = System.Math.Min(y0, y); z0 = System.Math.Min(z0, z); x1 = System.Math.Max(x1, x); y1 = System.Math.Max(y1, y); z1 = System.Math.Max(z1, z); }
            var b = stackalloc float[6] { float.MaxValue, float.MaxValue, float.MaxValue, float.MinValue, float.MinValue, float.MinValue };
            fixed (P3* P = pos) fixed (int* N = ne) fixed (int* C = ce) fixed (PackKernels.Colour* cs = colours) fixed (PackKernels.Normal* ns = normals) fixed (PackedMesh.Vertex* V = dst)
                PackKernels.VerticesManaged(P, N, C, cs, ns, 0, V, 3, b);
            float[] want = { x0, y0, z0, x1, y1, z1 };
            for (int i = 0; i < 6; i++) Assert.Equal(System.BitConverter.SingleToInt32Bits(want[i]), System.BitConverter.SingleToInt32Bits(b[i]));
        }
    }
}
