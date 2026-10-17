using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// LOD0's rooms (<see cref="MeshBuilder.InsideTris"/>, docs/PERFORMANCE.md §7.7): the walls' inner faces, the partitions
    /// and the cores of each storey seen only through its windows. The renderer draws them apart, only while a window can
    /// show them, and casts their shadows only for the building the player is in. Storey's own.
    /// </summary>
    [Collection("OpeningKinds")]   // one test registers an artist's window
    public class RoomsTests
    {
        // the colours the rooms are made of: inner faces and partitions, cores and their steps, lifts, rails
        static readonly ColorSlot[] Inside = { ColorSlot.Interior, ColorSlot.Core, ColorSlot.LiftInterior, ColorSlot.Metal, ColorSlot.LiftButton, ColorSlot.Rail };

        static bool[] Marked(MeshBuilder m)
        {
            var r = new bool[m.Tris];
            foreach (var (s, e) in m.InsideTris) for (int t = s; t < e; t++) r[t] = true;
            return r;
        }

        /// <summary>
        /// LOD0 built storey by storey (<see cref="Lod0.Shared.Steps"/>), as <see cref="Lod0.Build"/> builds it: its opaque
        /// mesh, and the storey each triangle was made in (the roof is storey N).
        /// </summary>
        static (MeshBuilder op, int[] storey) Build(Site site, BuildingData b)
        {
            var r = new Lod0Result { Op = new MeshBuilder(site.IndexOf(b)), Glass = new MeshBuilder(site.IndexOf(b)) };
            r.Glass.Walls = r.Op.Walls; r.Glass.WallBase = r.Op.WallBase;
            var storey = new List<int>();
            foreach (int k in new Lod0.Shared(site, b).Steps(r)) while (storey.Count < r.Op.Tris) storey.Add(k);
            return (r.Op, storey.ToArray());
        }

        static int[] StoreysWithRooms(Site site, BuildingData b)
        {
            var (m, storey) = Build(site, b); var marked = Marked(m);
            return Enumerable.Range(0, m.Tris).Where(t => marked[t]).Select(t => storey[t]).Distinct().OrderBy(k => k).ToArray();
        }

        /// <summary>A walk-in block of <paramref name="floors"/> storeys, 20 × 16 m, flat-roofed, its door on the ground floor.</summary>
        static (StoreyDocument d, BuildingData b) Block(int floors, RoofType roof = RoofType.Flat)
        {
            var (d, b) = CourtyardTests.Block(floors, roof);
            b.interior = true;
            return (d, b);
        }

        [Fact]
        public void TheRoomsAreTheInsideAlone()
        {
            int with = 0;
            var docs = new List<StoreyDocument> { PrototypeJson.Read(Fixtures.Text("demo.json")).Document, PrototypeJson.Read(Fixtures.Text("variants.json")).Document };
            var city = new StoreyDocument(); TestCity.Generate(city, 60, walkIn: true); docs.Add(city);
            foreach (var doc in docs)
            {
                var site = new Site(doc.buildings);
                foreach (var b in doc.buildings)
                {
                    var (m, storey) = Build(site, b);
                    // ranges in order, none empty or overlapping, inside the mesh
                    int end = 0;
                    foreach (var (s, e) in m.InsideTris) { Assert.True(s >= end && e > s, $"{b.name}: range {s}..{e} after {end}"); end = e; }
                    Assert.True(end <= m.Tris);
                    if (!b.interior) { Assert.Empty(m.InsideTris); continue; }
                    if (m.InsideTris.Count > 0) with++;
                    var marked = Marked(m); var backing = new bool[m.Tris];
                    foreach (var (s, e) in m.BackingTris) for (int t = s; t < e; t++) backing[t] = true;
                    for (int t = 0; t < m.Tris; t++)
                    {
                        if (!marked[t]) continue;
                        // and Unity's backs that only clear glass shows, whatever their colour: a strip's over an opening, a slab's edge
                        if (!backing[t]) for (int j = 0; j < 3; j++) Assert.Contains(m.C[m.I[3 * t + j]].slot, Inside);
                        Assert.True(storey[t] < b.floors.Count, $"{b.name}: a room on the roof");
                    }
                    Assert.Empty(Lod1.Build(site, b).InsideTris);
                }
            }
            Assert.True(with > 50, $"{with} buildings with rooms kept apart");
        }

        [Fact]
        public void AStoreysDoorwayKeepsItsRoomsWithTheShell()
        {
            // four storeys, the door on the ground floor: the rooms of the three above are kept apart, every inner face
            // and partition of theirs, and none of the ground floor's, which the doorway shows at any distance
            var (d, b) = Block(4);
            b.floors[2].walls.Add(new WallData { a = new Vec2(10, 1), b = new Vec2(10, 15) });
            var site = new Site(d.buildings);
            var (m, storey) = Build(site, b); var marked = Marked(m);
            Assert.Equal(new[] { 1, 2, 3 }, StoreysWithRooms(site, b));
            int partition = 0;
            for (int t = 0; t < m.Tris; t++)
                if (m.C[m.I[3 * t]].slot == ColorSlot.Interior && storey[t] < b.floors.Count)
                {
                    Assert.True(marked[t] == (storey[t] > 0), $"an inner face on storey {storey[t]} {(marked[t] ? "is" : "isn't")} the rooms'");
                    if (storey[t] == 2 && Math.Abs(m.P[m.I[3 * t]].x - 10) < 0.2) partition++;
                }
            Assert.True(partition > 0, "the partition");
            // a glazed door's glass is painted over far away as a window's is: the ground floor keeps its rooms apart too
            b.style.doorType = DoorType.Glazed;
            Assert.Contains(0, StoreysWithRooms(new Site(d.buildings), b));
        }

        [Fact]
        public void AnArtistsGlassKeepsItsStoreysRoomsWithTheShell()
        {
            // an artist's window's glass is never painted over (it has no pane coordinates): every storey with one shows its rooms
            var m = new OpeningKind.Mesh { P = new[] { new P3(-0.5, 0, 0), new P3(-0.5, 1.4, 0), new P3(0.5, 1.4, 0), new P3(0.5, 0, 0) }, N = Enumerable.Repeat(new P3(0, 0, 1), 4).ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Glass, tris = new[] { 0, 1, 2, 0, 2, 3 } });
            OpeningKinds.Register(new OpeningKind { id = "t-rooms-win", name = "t-rooms-win", w = 1, h = 1.5, lod0 = m });
            var (d, b) = Block(3);
            b.style.windowKind = "t-rooms-win";
            var op = Lod0.Build(new Site(d.buildings), b).Op;
            Assert.Empty(op.InsideTris);
            Assert.True(op.SeeThrough > 0);
        }

        [Fact]
        public void TheRoofsOpeningsKeepWhatTheyShow()
        {
            // stairs up to a bulkhead on the roof: through its door the top storey shows, at any distance
            var (d, b) = Block(4);
            var stairs = Shafts.Place(b, 0, new Vec2(4, 8), CoreType.Stairs, 0, Shafts.NewId(d))!;
            Assert.True(Cores.StairHoleAt(b, stairs, 4));
            var site = new Site(d.buildings);
            Assert.Equal(new[] { 1, 2 }, StoreysWithRooms(site, b));
            var op = Build(site, b).op; var marked = Marked(op);
            Assert.Contains(Enumerable.Range(0, op.Tris), t => marked[t] && op.C[op.I[3 * t]].slot == ColorSlot.Core);   // the core is the rooms' below
            // a straight flight's railed opening, stacked over every flight: every storey shows from above
            (d, b) = Block(4);
            Assert.NotNull(Shafts.Place(b, 0, new Vec2(10, 8), CoreType.Flight, 0, Shafts.NewId(d)));
            Assert.Empty(Lod0.Build(new Site(d.buildings), b).Op.InsideTris);
            // an atrium's skylight: every storey round it; under a pitched roof, which covers it, the rooms are apart again
            foreach (var roof in new[] { RoofType.Flat, RoofType.Hip })
            {
                (d, b) = Block(4, roof);
                CourtyardTests.Court(b, VoidKind.Atrium, 0);
                var rooms = Lod0.Build(new Site(d.buildings), b).Op.InsideTris;
                if (roof == RoofType.Flat) Assert.Empty(rooms); else Assert.NotEmpty(rooms);
            }
        }

        static int RowOf(StyleRef s) => 1 + s.building * 5 + s.tier;

        /// <summary>Each triangle of the packed meshes as its three vertices, in both streams, in order.</summary>
        static List<(PackedMesh.Vertex, PackedMesh.Cutaway)> Triangles(List<PackedMesh> parts)
        {
            var o = new List<(PackedMesh.Vertex, PackedMesh.Cutaway)>();
            foreach (var p in parts)
            {
                p.Finish(p.Styles.Select(RowOf).ToArray(), 100);
                for (int i = 0; i < p.Indices; i++)
                {
                    int x = p.I16 != null ? p.I16[i] : p.I32![i];
                    Assert.InRange(x, 0, p.Verts - 1);
                    o.Add((p.V[x], p.Cut![x]));
                }
            }
            return o;
        }

        [Fact]
        public void TheSplitPacksEveryTriangleOnce()
        {
            // packed apart, the shell and the rooms are the whole mesh's triangles, the shell's first, each in the order
            // built, as one mesh each or (a small part size) in parts
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 40, walkIn: true);
            var site = new Site(d.buildings); var mates = (100, 400); int split = 0;
            foreach (var b in d.buildings.Where((x, i) => i % 4 == 0))
                foreach (int max in new[] { MeshPack.MaxVerts, 997 })
                {
                    var m = Lod0.Build(site, b).Op; var marked = Marked(m);
                    var whole = MeshPack.Pack(m, true, mates, int.MaxValue, weld: false);
                    var rooms = new List<PackedMesh>();
                    var shell = MeshPack.Pack(m, true, mates, max, weld: false, rooms: rooms);
                    var all = Triangles(whole);
                    var want = new List<(PackedMesh.Vertex, PackedMesh.Cutaway)>();
                    for (int t = 0; t < m.Tris; t++) if (!marked[t]) want.AddRange(all.Skip(3 * t).Take(3));
                    int shellIndices = want.Count;
                    for (int t = 0; t < m.Tris; t++) if (marked[t]) want.AddRange(all.Skip(3 * t).Take(3));
                    var got = Triangles(shell); Assert.Equal(shellIndices, got.Count); got.AddRange(Triangles(rooms));
                    Assert.True(want.SequenceEqual(got), $"{b.name}: the split draws other triangles");
                    Assert.All(shell.Concat(rooms), p => Assert.True(p.Verts <= max));
                    Assert.All(shell, p => Assert.Equal(whole[0].Mates, p.Mates));
                    Assert.All(rooms, p => Assert.Subset(whole[0].Mates.ToHashSet(), p.Mates.ToHashSet()));   // a party wall's inner face is the rooms'
                    if (rooms.Count > 0) split++;
                    if (m.InsideTris.Count == 0) Assert.Empty(rooms);
                }
            Assert.True(split > 10);
        }

        [Fact]
        public void AMarkTakesBackWhatFollowsIt()
        {
            var m = new MeshBuilder();
            void Q() => m.Quad(new P3(0, 0, 0), new P3(1, 0, 0), new P3(1, 1, 0), new P3(0, 1, 0), new P3(0, 0, 1), default);
            Q(); m.Inside = true; Q(); Q(); m.Inside = false;
            Assert.Equal(new[] { (2, 6) }, m.InsideTris);
            var mark = m.InsideMark;
            m.Inside = true; Q(); m.Inside = false;          // straight after: one range with the one before
            Q(); m.Inside = true; Q(); m.Inside = false;
            Assert.Equal(new[] { (2, 8), (10, 12) }, m.InsideTris);
            m.TakeBackInside(mark);
            Assert.Equal(new[] { (2, 6) }, m.InsideTris);
            m.SeeThrough++; m.Recycle();
            var n = MeshBuilder.Make();
            Assert.Empty(n.InsideTris); Assert.Equal(0, n.SeeThrough); Assert.False(n.Inside);
            MeshBuilder.ForgetSpares();
        }

        [Fact]
        public void TheRendererDrawsTheRoomsOnlyWhereAWindowCanShowThem()
        {
            // SiteRenderer: LOD0's rooms packed apart and uploaded to their own renderers; drawn with the LOD0 while its
            // nearest point is within the distance its windows are painted from (the glass fill's), or anything can show its
            // inside, and casting shadows only for the building the player is in
            var renderer = File.ReadAllText(UnityAsset.Package("com.triband.storey", "Unity", "Rendering", "SiteRenderer.cs"));
            Assert.Contains("MeshPack.Pack(l0.Op, true, mates, weld: false, rooms: full.rooms)", renderer);
            Assert.Contains("bool on = !roomsHide || bt.near || !steady || bt.idx == active || forcedIdx.Contains(bt.idx), cast = bt.idx == active;", renderer);
            Assert.Contains("StoreyGlobals.SetGlassFill(at * clear, clear >= 1 ? 0 : at);", renderer);
            Assert.Contains("float at = (float)lods.Settings.Lod0Distance(ge.pixelsPerMetre)", renderer);
            Assert.Contains("roomsHide = clear < 1 && !ge.orthographic;", renderer);
            Assert.Contains("double paint = l.Settings.Lod0Distance(eye.pixelsPerMetre)", renderer);
            Assert.Contains("bool near = Math.Sqrt(dx * dx + dy * dy + dz * dz) < paint * (bt.near ? 1.1 : 1.05);", renderer);
            Assert.Contains("if (near != bt.near) { bt.near = near; steadyDirty.Add(bt.idx); }", renderer);   // looked at again (docs/PERFORMANCE.md §3.3)
            Assert.Contains("Add(bt, 0, Part(\"LOD0 rooms\", r, nr), MeshUpload.Upload(d.rooms[r], Part(name + \" rooms\", r, nr), RowOf, bt.wallBase), opaque, false, rooms: true);", renderer);
        }
    }
}
