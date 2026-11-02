using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What the cutaway leaves of the walls it takes down, and the ceiling clip of those it cuts (docs/PLAY.md §3): no part
    /// of a pitched roof's eave left in the air over a wall gone down, and a cut wall that reads as solid from above, looking
    /// down along it: its foot closed, and the faces of the walls that run into it hidden (kind 3, <see
    /// cref="MeshBuilder.Buried"/>), as the shader hides them in the building the player is in. Storey's own.
    /// </summary>
    public class CutawayWallTests
    {
        readonly ITestOutputHelper output;
        public CutawayWallTests(ITestOutputHelper o) { output = o; }

        static (BuildingData b, Site site) Block(RoofType type)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(16, 0), new Vec2(16, 11), new Vec2(0, 11) };
            b.entrances.Clear(); b.details.Clear(); b.blank.Clear(); b.shafts.Clear();
            foreach (var f in b.floors) f.walls.Clear();
            b.style.roofType = type;
            if (type == RoofType.Shed) b.style.pitch = 30;   // a shed's eave comes below the ceiling clip from about 25° on
            return (b, new Site(d.buildings));
        }

        /// <summary>How far (x, z) lies outside polygon <paramref name="fp"/>; 0 inside it.</summary>
        static double Out(List<Vec2> fp, double x, double z)
        {
            if (Geo.Pip(fp, x, z)) return 0;
            double d = double.MaxValue;
            for (int i = 0; i < fp.Count; i++) { var a = fp[i]; var c = fp[(i + 1) % fp.Count]; d = Math.Min(d, Tiers.SegDist(x, z, a.x, a.z, c.x, c.z).d); }
            return d;
        }

        /// <summary>
        /// Storey k of b as the player sees it from inside, every wall taken down: what lies outside its outside walls between
        /// the stub and the ceiling clip goes down with the wall it stands out from (the wall's dressing, its cornice, and the
        /// eave of the roof over it). Returns how many of those triangles are a roof's (its slope, fascia and soffit).
        /// </summary>
        static int EaveGoesWithItsWall(BuildingData b, Site site, int k)
        {
            var op = Lod0.Build(site, b).Op;
            var (clip, cutBase, _) = Picking.StoreyView(b, k); double stub = cutBase + new OcclusionSettings().stub;
            var fp = new List<Vec2>(); foreach (var p in Derived.OutlineAt(b, k)) fp.Add(new Vec2(p.x + b.pos.x, p.z + b.pos.z));
            int roof = 0, n = 0;
            for (int t = 0; t < op.Tris; t++)
            {
                int ia = op.I[3 * t], ib = op.I[3 * t + 1], ic = op.I[3 * t + 2];
                P3 A = op.P[ia], B = op.P[ib], C = op.P[ic];
                double lo = Math.Min(A.y, Math.Min(B.y, C.y)), hi = Math.Max(A.y, Math.Max(B.y, C.y));
                if (hi <= stub + 0.01 || lo >= clip - 1e-6) continue;   // none of it between the stub and the clip
                double mx = (A.x + B.x + C.x) / 3, mz = (A.z + B.z + C.z) / 3;
                if (Out(fp, mx, mz) < Dim.T_EXT + 0.01) continue;      // the walls, and what is inside them
                n++;
                Assert.True(op.K[ia] % 8 == 1 && op.WI[ia] > 0, $"{op.C[ia].slot} at ({mx:0.00}, {lo:0.00}..{hi:0.00}, {mz:0.00}) stays when the walls go down (kind {op.K[ia]})");
                var W = op.Walls[op.WI[ia] - 1].W;
                Assert.True((mx - W[0]) * W[2] + (mz - W[1]) * W[3] > 0, $"{op.C[ia].slot} at ({mx:0.00}, {lo:0.00}, {mz:0.00}) goes with a wall it isn't over");
                if (op.C[ia].slot == ColorSlot.Roof || op.C[ia].slot == ColorSlot.Trim) roof++;
            }
            Assert.True(n > 0);
            op.Recycle();
            return roof;
        }

        [Theory]
        [InlineData(RoofType.Hip)]
        [InlineData(RoofType.Gable)]
        [InlineData(RoofType.Mansard)]
        [InlineData(RoofType.Shed)]
        public void APitchedRoofsEaveGoesWithTheWallUnderIt(RoofType type)
        {
            var (b, site) = Block(type);
            Assert.True(EaveGoesWithItsWall(b, site, b.floors.Count - 1) > 0, "no eave hangs below the top storey's ceiling clip: the test sees nothing");
        }

        [Fact]
        public void ATerracesRoofsEaveGoesWithTheWallUnderIt()
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 14), new Vec2(0, 14) };
            b.entrances.Clear(); b.details.Clear(); b.shafts.Clear();
            Floors.SetCount(b, 5); b.style = Styles.Preset("brick").Clone();
            int k = Setbacks.Add(b, 0);
            Assert.Equal(OutlineIssue.None, Outlines.Set(b, k, Outlines.Inset(Tiers.Outline(b, k), 2.5)));
            Facades.SetTerraceRoof(b, k, true);
            Assert.True(EaveGoesWithItsWall(b, new Site(d.buildings), k - 1) > 0, "no eave of the terrace's roof hangs below the ceiling clip under it");
        }

        /// <summary>Whether (x, y, z) lies strictly inside one of the room walls (its doors' openings apart).</summary>
        static bool InRoomWall(List<Lod0.Shared.RoomWall> rooms, double x, double y, double z, double floor)
        {
            double E = Lod0.Shared.RoomWall.E, hw = Dim.T_INT / 2;
            foreach (var r in rooms)
            {
                double u = (x - r.a.x) * r.u.x + (z - r.a.z) * r.u.z, w = (x - r.a.x) * r.w.x + (z - r.a.z) * r.w.z;
                if (u <= -E + 1e-4 || u >= r.L + E - 1e-4 || Math.Abs(w) >= hw - 1e-4) continue;
                if (r.ops.Any(d => u > d.u0 - 1e-4 && u < d.u1 + 1e-4 && y < floor + d.y1)) continue;
                return true;
            }
            return false;
        }

        [Fact]
        public void TheFacesInsideARoomWallAreBuriedAndNoOthers()
        {
            // a T: the second wall runs into the first's middle; a third runs on from the first's end, with a door
            var (b, site) = Block(RoofType.Hip);
            var walls = b.floors[0].walls;
            walls.Add(new WallData { a = new Vec2(2, 5), b = new Vec2(10, 5) });
            walls.Add(new WallData { a = new Vec2(6, 5), b = new Vec2(6, 9) });
            walls.Add(new WallData { a = new Vec2(10, 5), b = new Vec2(14, 5), doors = { new DoorData { t = 0.5 } } });
            var r = Lod0.Build(site, b); var op = r.Op;
            var rooms = new Lod0.Shared(site, b).RoomWalls(0);
            Assert.Equal(3, rooms.Count);
            double floor = Derived.FloorBase(b, 0), top = Derived.FloorBase(b, 1) - 0.05;
            var feet = new bool[op.Tris]; foreach (var (s0, e0) in op.BackingTris) for (int t = s0; t < e0; t++) feet[t] = true;
            int buried = 0;
            for (int t = 0; t < op.Tris; t++)
            {
                int ia = op.I[3 * t]; P3 A = op.P[ia], B = op.P[op.I[3 * t + 1]], C = op.P[op.I[3 * t + 2]];
                if (Math.Max(A.y, Math.Max(B.y, C.y)) > top || feet[t]) continue;   // the storey's, but the feet that close the walls
                var nr = op.N[ia]; double mx = (A.x + B.x + C.x) / 3, my = (A.y + B.y + C.y) / 3, mz = (A.z + B.z + C.z) / 3;
                // a casing head's ends are left whole (Lod0.Shared.Casing): only the ceiling's cut shows them, 8 cm high
                if (Math.Abs(nr.y) < 0.5 && my > floor + 2.19 && my < floor + 2.27 && op.C[ia].Equals(new Palette(new StyleRef(0, 0)).doorTrim)) continue;
                // a face with a room wall's inside on both its sides lies inside it
                bool inside = InRoomWall(rooms, mx + nr.x * 1e-3, my + nr.y * 1e-3, mz + nr.z * 1e-3, floor) && InRoomWall(rooms, mx - nr.x * 1e-3, my - nr.y * 1e-3, mz - nr.z * 1e-3, floor);
                bool kind3 = op.K[ia] % 8 == 3;
                if (kind3) buried++;
                Assert.True(kind3 == inside, kind3 ? $"a face at ({mx:0.00}, {my:0.00}, {mz:0.00}) is buried, but no room wall holds it"
                    : $"a {op.C[ia].slot} face at ({mx:0.00}, {my:0.00}, {mz:0.00}), normal ({nr.x:0.0}, {nr.y:0.0}, {nr.z:0.0}), lies inside a room wall and shows in its cut");
            }
            Assert.True(buried > 0);
            op.Recycle(); r.Glass.Recycle();
        }

        /// <summary>
        /// The walk-in storeys' walls of a layout cut at the stub and at the ceiling clip, every one: from inside each one's
        /// full-height stretches (outside walls and room walls) at the cut, rays down into it, straight and slanting along it
        /// and across it, meet the back of a face first (the section cap), the buried faces left out as the shader leaves them.
        /// Straight down every one; slanting, all but a few (1.5 % at most): those that leave by a door or a window beside, a
        /// party wall's end, or meet a core's wall or a casing head's end (left whole) on the way.
        /// </summary>
        [Theory]
        [InlineData("demo.json")]
        [InlineData("variants.json")]
        public void ACutWallIsSolidLookingDownIt(string file)
        {
            var d = PrototypeJson.Read(Fixtures.Text(file)).Document;
            var site = new Site(d.buildings);
            var rng = new Random(3); int down = 0, slant = 0, slantBack = 0; var misses = new List<string>();
            foreach (var b in d.buildings)
            {
                if (!b.interior) continue;
                var r = Lod0.Build(site, b, solids: true); var op = r.Op;
                var sc = new RayScene();
                sc.Add(op.P, op.I, t => t, 1.0, Enumerable.Range(0, op.Tris).Where(t => op.K[op.I[3 * t]] % 8 != 3).ToList());
                for (int k = 0; k < b.floors.Count; k++)
                {
                    if (Derived.ShellAt(b, k)) continue;
                    double fb = Derived.FloorBase(b, k), h = Derived.FloorH(b, k);
                    var fp = new List<Vec2>(); foreach (var q in Derived.OutlineAt(b, k)) fp.Add(new Vec2(q.x + b.pos.x, q.z + b.pos.z));
                    foreach (var s in op.Solids!)
                    {
                        // a full-height stretch of an outside wall or a room wall
                        bool outside = s.Mitred && Math.Abs(s.w0) < 1e-9 && Math.Abs(s.w1 - Dim.T_EXT) < 1e-9 && Math.Abs(s.y0 - fb) < 1e-9 && Math.Abs(s.y1 - fb - h) < 1e-9;
                        bool room = s.Mitred && Math.Abs(s.w0 + Dim.T_INT / 2) < 1e-9 && Math.Abs(s.w1 - Dim.T_INT / 2) < 1e-9 && Math.Abs(s.y0 - fb) < 1e-9 && Math.Abs(s.y1 - fb - h + Dim.SLAB) < 1e-9;
                        if (!outside && !room) continue;
                        foreach (double yc in new[] { fb + 1.0, fb + h - Dim.SLAB - 0.01 })
                            for (int j = 0; j < 4; j++)
                            {
                                double w = s.w0 + 0.01 + rng.NextDouble() * (s.w1 - s.w0 - 0.02), a0 = s.sA + s.kA * w, a1 = s.eB + s.kB * w;
                                if (a1 - a0 < 0.06) continue;
                                var p = s.F.At(a0 + 0.03 + rng.NextDouble() * (a1 - a0 - 0.06), yc - 1e-4, w);
                                // a room wall's end runs on into the outside wall it meets, whose window it may stand in: inside its line's
                                if (room && !Geo.Pip(fp, p.x, p.z)) continue;
                                var u = s.F.u; var n = s.F.w;
                                foreach (var (dx, dy, dz, what) in new[] { (0.0, -1.0, 0.0, "down"), (u.x * 0.8, -0.6, u.z * 0.8, "along"), (-u.x * 0.8, -0.6, -u.z * 0.8, "back along"), (u.x * 0.6 + n.x * 0.1, -0.79, u.z * 0.6 + n.z * 0.1, "aslant") })
                                {
                                    int hit = sc.Hit(p.x, p.y, p.z, dx, dy, dz, out double th);
                                    bool back = hit >= 0 && sc.Back(hit, dx, dy, dz);
                                    if (what == "down") { down++; Assert.True(back, $"{b.name} storey {k}: looking straight down into its cut {(outside ? "outside" : "room")} wall at ({p.x:0.00}, {p.y:0.00}, {p.z:0.00}), {(hit < 0 ? "nothing" : op.C[op.I[3 * sc[hit].tag]].slot + " " + th.ToString("0.00") + " m down")}"); continue; }
                                    slant++; if (back) { slantBack++; continue; }
                                    if (misses.Count < 12) misses.Add($"{b.name} storey {k} {what}: {(hit < 0 ? "nothing" : op.C[op.I[3 * sc[hit].tag]].slot.ToString())} from ({p.x:0.00}, {p.y:0.00}, {p.z:0.00})");
                                }
                            }
                    }
                }
                op.Recycle(); r.Glass.Recycle();
            }
            output.WriteLine($"{down} rays straight down; {slantBack} of {slant} slanting met the cap");
            foreach (var m in misses) output.WriteLine(m);
            Assert.True(down > 500 && slant > 1500);
            Assert.True(slantBack >= 0.985 * slant, $"{slant - slantBack} of {slant} slanting rays saw into a cut wall:\n  " + string.Join("\n  ", misses));
        }
    }
}
