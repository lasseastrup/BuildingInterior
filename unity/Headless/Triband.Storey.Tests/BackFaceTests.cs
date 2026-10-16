using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What culling back faces shows from outside (docs/PERFORMANCE.md §7.1). The steady copies of Opaque and Massing cull
    /// back faces, so wherever a back face is the first thing an eye outside a building meets, culling shows what lies behind
    /// it: a hole. Rays from eyes round every building of whole layouts (a party wall is drawn by one of its two buildings),
    /// for each LOD as it is drawn: LOD0 near, its rooms in and its glass clear; LOD0 far, its rooms out and its panes
    /// painted over (opaque), where no ray may meet a room either; LOD1; LOD2. Not counted, as no wider than a sliver: a ray
    /// within 6° of the face's plane; a back face with a front face up to 30 cm behind it, which culling shows in its place
    /// (a soffit poking up through a shed roof's high eave); and one with a front face within 5 cm, its edge seen aslant past
    /// the face it backs (Unity's backing faces sit a centimetre or so off). Storey's own.
    /// </summary>
    public class BackFaceTests
    {
        readonly ITestOutputHelper output;
        public BackFaceTests(ITestOutputHelper o) { output = o; }

        public enum View { Near, Far, Lod1, Lod2 }

        const int Rays = 4000;

        sealed class Layout
        {
            public StoreyDocument Doc = null!;
            public List<Lod0Result> L0 = null!;
            public List<MeshBuilder> L1 = null!;
            public List<Lod2Mesh> L2 = null!;
        }

        static readonly Dictionary<string, Lazy<Layout>> layouts = new Dictionary<string, Lazy<Layout>>
        {
            ["demo"] = new Lazy<Layout>(() => Make(PrototypeJson.Read(Fixtures.Text("demo.json")).Document)),
            ["variants"] = new Lazy<Layout>(() => Make(PrototypeJson.Read(Fixtures.Text("variants.json")).Document)),
            ["city"] = new Lazy<Layout>(() => { var d = new StoreyDocument(); TestCity.Generate(d, 40, walkIn: true); return Make(d); }),
        };

        static Layout Make(StoreyDocument d)
        {
            var site = new Site(d.buildings);
            return new Layout { Doc = d, L0 = d.buildings.Select(b => Lod0.Build(site, b)).ToList(), L1 = d.buildings.Select(b => Lod1.Build(site, b)).ToList(), L2 = d.buildings.Select(b => Lod2.Build(site, b)).ToList() };
        }

        static bool[] Marked(int tris, List<(int start, int end)> ranges)
        {
            var o = new bool[tris];
            foreach (var (s, e) in ranges) for (int t = s; t < e; t++) o[t] = true;
            return o;
        }

        /// <summary>The layout's buildings in one scene as <paramref name="view"/> draws them; a triangle's tag: 1 a room, 2 a pane, 0 the rest.</summary>
        static RayScene Scene(Layout l, View view)
        {
            var sc = new RayScene();
            for (int i = 0; i < l.Doc.buildings.Count; i++)
            {
                if (view == View.Lod1) { var m = l.L1[i]; sc.Add(m.P, m.I, t => 0); continue; }
                if (view == View.Lod2) { var m = l.L2[i]; sc.Add(m.P, m.I, t => 0); continue; }
                var op = l.L0[i].Op; var rooms = Marked(op.Tris, op.InsideTris);
                sc.Add(op.P, op.I, t => rooms[t] ? 1 : 0, 1.0, view == View.Far ? Enumerable.Range(0, op.Tris).Where(t => !rooms[t]).ToList() : null);
            }
            if (view == View.Far)
                foreach (var r in l.L0)
                {
                    var gl = r.Glass;   // the panes, painted over (opaque); any other glass stays clear
                    sc.Add(gl.P, gl.I, t => 2, 1.0, Enumerable.Range(0, gl.Tris).Where(t => gl.PaneUV.ContainsKey(gl.I[3 * t]) && gl.PaneUV.ContainsKey(gl.I[3 * t + 1]) && gl.PaneUV.ContainsKey(gl.I[3 * t + 2])).ToList());
                }
            return sc;
        }

        /// <summary>The holes culling makes from outside: rays whose first hit is a back face with no front face close behind; and rays meeting a room first.</summary>
        static (int holes, int rooms, int rays, List<string> where) Look(Layout l, View view)
        {
            var sc = Scene(l, view); var rng = new Random(5); int holes = 0, rooms = 0, rays = 0; var where = new List<string>();
            int n = l.Doc.buildings.Count;
            var boxes = Enumerable.Range(0, n).Select(sc.Box).ToList();
            bool far = view == View.Far;
            for (int i = 0; i < n; i++)
            {
                var box = sc.Box(i); if (box.x0 > box.x1) continue;
                foreach (var (ex, ey, ez, dx, dy, dz) in RayScene.Eyes(box, boxes, rng, Rays, far ? 50 : 2, far ? 150 : 60, far ? 80 : 40))
                {
                    int h = sc.Hit(ex, ey, ez, dx, dy, dz, out double th); if (h < 0) continue;
                    rays++;
                    ref var q = ref sc[h];
                    if (q.tag == 2) continue;   // a pane: glass is never culled
                    if (q.tag == 1 && far) { rooms++; if (where.Count < 12) where.Add($"{l.Doc.buildings[q.mesh].name}: a room at ({ex + dx * th:0.00}, {ey + dy * th:0.00}, {ez + dz * th:0.00})"); continue; }
                    if (!sc.Back(h, dx, dy, dz)) continue;
                    double len = Math.Sqrt(q.nx * q.nx + q.ny * q.ny + q.nz * q.nz);
                    if (Math.Abs((q.nx * dx + q.ny * dy + q.nz * dz) / len) < 0.1) continue;   // grazing
                    // the first front face behind it, which culling shows instead
                    var passed = new HashSet<int> { h }; double front = double.MaxValue;
                    for (int k = 0; k < 16; k++)
                    {
                        int h2 = sc.Hit(ex, ey, ez, dx, dy, dz, out double t2, null, passed); if (h2 < 0) break;
                        if (!sc.Back(h2, dx, dy, dz)) { front = t2; break; }
                        passed.Add(h2);
                    }
                    if (front - th <= 0.3) continue;
                    double hx = ex + dx * th, hy = ey + dy * th, hz = ez + dz * th;
                    if (sc.FrontNear(hx, hy, hz, 0.05, dx, dy, dz)) continue;   // its edge, seen aslant past the face it backs
                    holes++;
                    if (where.Count < 12) where.Add($"{l.Doc.buildings[sc[h].mesh].name}: a back face at ({hx:0.00}, {hy:0.00}, {hz:0.00}), the ray {ex:R} {ey:R} {ez:R} {dx:R} {dy:R} {dz:R}");
                }
            }
            return (holes, rooms, rays, where);
        }

        [Theory]
        [InlineData("demo", View.Near)]
        [InlineData("demo", View.Far)]
        [InlineData("demo", View.Lod1)]
        [InlineData("demo", View.Lod2)]
        [InlineData("city", View.Near)]
        [InlineData("city", View.Far)]
        [InlineData("city", View.Lod1)]
        [InlineData("city", View.Lod2)]
        [InlineData("variants", View.Near)]
        [InlineData("variants", View.Far)]
        [InlineData("variants", View.Lod1)]
        [InlineData("variants", View.Lod2)]
        public void CullingMakesNoHoleFromOutside(string layout, View view)
        {
            var (holes, rooms, rays, where) = Look(layouts[layout].Value, view);
            output.WriteLine($"{layout}, {view}: {rays:N0} rays, {holes} meet a back face with nothing close behind, {rooms} a room");
            foreach (var w in where) output.WriteLine("  " + w);
            Assert.True(rays > 4000);
            Assert.True(holes + rooms == 0, $"{layout}, {view}: {holes} holes from outside, {rooms} rays meeting a room:\n  " + string.Join("\n  ", where));
        }
    }
}
