using System.Linq;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A cut wall's top is the wall's colour (docs/PLAY.md §3): its outside's, recorded as LOD0 draws the wall
    /// (<see cref="MeshBuilder.Outside"/>, <see cref="Lod0Shape.OutsideSlots"/>), which the vertex stage gives a wall's
    /// inside face seen from behind (StoreyLit.hlsl); a room wall's is its faces' own.
    /// </summary>
    public class CutCapTests
    {
        static StoreyDocument Demo() => PrototypeJson.Read(Fixtures.Text("demo.json")).Document;

        /// <summary>
        /// Every wall of the demo's walk-in buildings: a room wall names no outside colour; an outside wall names its outside
        /// face's, which some of its faces facing out are in; and no face of a wall with an outside faces out in the rooms'
        /// colour, so the vertex stage giving a room-coloured face seen from behind the outside's colour only ever recolours
        /// an inside face.
        /// </summary>
        [Fact]
        public void AWallsOutsideColourIsItsOutsideFaces()
        {
            var doc = Demo(); var site = new Site(doc.buildings); int outside = 0, rooms = 0;
            foreach (var b in doc.buildings.Where(x => x.interior))
            {
                var r = Lod0.Build(site, b); var op = r.Op; var shape = r.Shape;
                var slots = new int[shape.Walls.Count]; shape.OutsideSlots(slots);
                foreach (int i in shape.Partitions) Assert.Equal(-1, slots[i]);
                rooms += shape.Partitions.Count;
                var outward = new bool[shape.Walls.Count];
                for (int v = 0; v < op.P.Count; v++)
                {
                    int id = op.WI[v]; if (id <= 0 || op.K[v] != 1 || slots[id - 1] < 0) continue;
                    var W = shape.Walls[id - 1].W; double nl = System.Math.Sqrt(W[2] * W[2] + W[3] * W[3]); if (nl < 1) continue;
                    var n = op.N[v]; double d = (n.x * W[2] + n.z * W[3]) / nl;
                    if (d > 0.9 && (int)op.C[v].slot == slots[id - 1]) outward[id - 1] = true;
                    Assert.False(d > 0.1 && op.C[v].slot == ColorSlot.Interior, $"{b.name}: wall {id} faces out in the rooms' colour");
                }
                // the outside walls a panel drew (its outside face in the slot it names)
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] >= 0 && shape.Walls[i].K == 1 && i < shape.Outside.Count && shape.Outside[i] >= 0)
                    {
                        Assert.True(outward[i], $"{b.name}: wall {i + 1}'s faces facing out aren't in {(ColorSlot)slots[i]}");
                        outside++;
                    }
                op.Recycle(); r.Glass.Recycle();
            }
            Assert.True(outside > 50 && rooms > 5, $"{outside} outside walls, {rooms} room walls");
        }

        /// <summary>A style with a ground storey's wall colour of its own: the ground storey's outside walls name it, the rest the walls'.</summary>
        [Fact]
        public void TheGroundStoreysOwnWallColourIsItsWallsOutside()
        {
            var doc = Demo(); var site = new Site(doc.buildings);
            var b = doc.buildings.First(x => x.name == "Corner Shop");
            Derived.StyleAt(b, 0).groundWall = "#7A4B3A";
            var r = Lod0.Build(site, b); var shape = r.Shape;
            var slots = new int[shape.Walls.Count]; shape.OutsideSlots(slots);
            double ground = Derived.FloorBase(b, 0);
            int groundWalls = 0, upper = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (shape.Walls[i].K != 1 || i >= shape.Outside.Count || shape.Outside[i] < 0 || shape.Partitions.Contains(i)) continue;
                bool onGround = System.Math.Abs(shape.WallBase[i] - ground) < 1e-6;
                Assert.Equal(onGround ? ColorSlot.GroundWall : ColorSlot.Wall, (ColorSlot)slots[i]);
                if (onGround) groundWalls++; else upper++;
            }
            Assert.True(groundWalls > 0 && upper > 0);
            r.Op.Recycle(); r.Glass.Recycle();
        }

        /// <summary>The shaders' slot numbers are the C# side's, and the vertex stage recolours only a room-coloured face of a wall with an outside, seen from behind.</summary>
        [Fact]
        public void TheShadersKnowTheSlots()
        {
            var palette = System.IO.File.ReadAllText(UnityAsset.Package("com.triband.storey", "Unity", "Shaders", "StoreyPalette.hlsl"));
            Assert.Contains($"#define STOREY_SLOT_INTERIOR {(int)ColorSlot.Interior}u", palette);
            var lit = System.IO.File.ReadAllText(UnityAsset.Package("com.triband.storey", "Unity", "Shaders", "StoreyLit.hlsl"));
            Assert.Contains("[branch] if (!pane && ce.z == STOREY_SLOT_INTERIOR && wall.z > 0.5 && kind < 7.5 && dot(normalWS, GetWorldSpaceViewDir(worldPos)) < 0.0)", lit);
            Assert.Contains("OUT.color.rgb = StoreyPaletteColor(ce.x | (ce.y << 8), (uint)(wall.z - 0.5), 1.0);", lit);
        }
    }
}
