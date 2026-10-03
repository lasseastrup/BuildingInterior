using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The automatic LOD builds detail on worker threads (docs/CITY.md §2): building many buildings of one site at once
    /// must give exactly what building them one after another does.
    /// </summary>
    public class ParallelBuildTests
    {
        static string Print(MeshBuilder m)
        {
            var h = new HashCode();
            foreach (var p in m.P) { h.Add(p.x); h.Add(p.y); h.Add(p.z); }
            foreach (int i in m.I) h.Add(i);
            return $"{m.Verts}/{m.Tris}/{h.ToHashCode()}";
        }

        static string Print(Lod2Mesh m)
        {
            var h = new HashCode();
            foreach (var p in m.P) { h.Add(p.x); h.Add(p.y); h.Add(p.z); }
            foreach (int i in m.I) h.Add(i);
            return $"{m.Verts}/{m.Tris}/{h.ToHashCode()}";
        }

        static string Each(Site site, BuildingData b, int lod) => lod switch
        {
            0 => Print(Lod0.Build(site, b).Op) + " " + Print(Lod0.Build(site, b).Glass),
            1 => Print(Lod1.Build(site, b)),
            _ => Print(Lod2.Build(site, b)),
        };

        [Fact]
        public void BuildingOnManyThreadsGivesTheSameMeshes()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            TestCity.Generate(d, 400);
            var jobs = new List<(BuildingData b, int lod)>();
            foreach (var b in d.buildings) for (int l = 0; l < 3; l++) if (l > 0 || !b.gen || b.interior) jobs.Add((b, l));
            var one = new Site(d.buildings);
            var want = jobs.Select(j => Each(one, j.b, j.lod)).ToArray();
            for (int round = 0; round < 2; round++)
            {
                var many = new Site(d.buildings); var got = new string[jobs.Count];
                // the same building's LODs never run at once (the renderer builds one at a time per building), neighbours do
                var order = Enumerable.Range(0, jobs.Count).OrderBy(i => (i * 7919 + round) % jobs.Count).ToArray();
                Parallel.ForEach(order.GroupBy(i => jobs[i].b.id), new ParallelOptions { MaxDegreeOfParallelism = 8 },
                    g => { foreach (int i in g) got[i] = Each(many, jobs[i].b, jobs[i].lod); });
                for (int i = 0; i < jobs.Count; i++) Assert.True(want[i] == got[i], $"{jobs[i].b.name} LOD{jobs[i].lod}: {want[i]} one at a time, {got[i]} in parallel");
            }
        }
    }
}
