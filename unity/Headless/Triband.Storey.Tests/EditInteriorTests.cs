using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Stairs, lifts, the interior wall graph, doors and erase (docs/EDITOR.md slice 6.2) against the prototype:
    /// unity/Fixtures/ops-interior.json holds cases of one or more steps, as the tools make them, with what the last
    /// step returned and the building afterwards (none for queries, "unchanged" when it is as it started).
    /// </summary>
    public class EditInteriorTests
    {
        static readonly Lazy<OpsReplay> Data = new(() => new OpsReplay("ops-interior.json"));

        public static IEnumerable<object[]> Names() => Data.Value.Names();

        [Theory, MemberData(nameof(Names))]
        public void MatchesThePrototype(string name) => Data.Value.Check(name, (site, b, op, a) => Step(site, b, op, a));

        static Vec2 V(JsonElement e) => new Vec2(e[0].GetDouble(), e[1].GetDouble());
        static CoreType Type(JsonElement e) => e.GetString() == "lift" ? CoreType.Lift : CoreType.Stairs;

        static SnapOptions Options(JsonElement o)
        {
            var r = new SnapOptions();
            if (o.TryGetProperty("from", out var f)) r.from = V(f);
            if (o.TryGetProperty("anchors", out var a)) r.anchors = a.EnumerateArray().Select(V).ToList();
            if (o.TryGetProperty("ex", out var ex)) foreach (var s in ex.EnumerateArray()) { var p = s.GetString()!.Split(':'); r.ignoreEnds.Add((int.Parse(p[0], CultureInfo.InvariantCulture), p[1] == "b")); }
            if (o.TryGetProperty("exW", out var ew)) foreach (var w in ew.EnumerateArray()) r.ignoreWalls.Add(w.GetInt32());
            if (o.TryGetProperty("free", out var fr)) r.free = fr.GetBoolean();
            return r;
        }

        /// <summary>One step, its result in the shape the exporter records it.</summary>
        static object? Step(Site site, BuildingData b, string op, JsonElement[] a)
        {
            int I(int j) => a[j].GetInt32();
            switch (op)
            {
                case "snapPoint": { var q = Walls.Snap(b, I(0), V(a[1]), Options(a[2])); return new Dictionary<string, object?> { ["x"] = q.x, ["z"] = q.z, ["kind"] = q.kind.ToString().ToLowerInvariant(), ["wi"] = q.wall, ["guides"] = q.guides.Select(g => new[] { g.x, g.z }).ToList() }; }
                case "placementAt": { var (it, ok) = Shafts.Placement(b, I(0), V(a[1]), Type(a[2]), a[3].GetDouble()); return new Dictionary<string, object?> { ["x"] = it.x, ["z"] = it.z, ["rot"] = it.rot, ["ok"] = ok }; }
                case "doorAt": return Door(Walls.DoorAt(site, b, I(0), V(a[1]), a[2].GetBoolean()));
                case "hoverTarget":
                {
                    var t = Walls.HoverTarget(b, I(0), V(a[1])); if (t == null) return null; var v = t.Value;
                    return v.kind switch
                    {
                        TargetKind.Shaft => new Dictionary<string, object?> { ["kind"] = "shaft", ["si"] = v.index },
                        TargetKind.Wall => new Dictionary<string, object?> { ["kind"] = "wall", ["wi"] = v.index },
                        TargetKind.Door => new Dictionary<string, object?> { ["kind"] = "door", ["wi"] = v.index, ["di"] = v.door },
                        _ => new Dictionary<string, object?> { ["kind"] = "entrance", ["ei"] = v.index },
                    };
                }
                case "wallAngle": { var p = V(a[1]); return Shafts.WallAngle(Derived.OutlineAt(b, I(0)), p.x, p.z); }
                case "snapCore": { var p = V(a[2]); double rot = a[3].GetDouble(); var q = Shafts.Snap(Derived.OutlineAt(b, I(0)), new CoreData { type = Type(a[1]), x = p.x, z = p.z, rot = rot }, p.x, p.z, rot); return new[] { q.x, q.z }; }
                case "itemsOverlap": return Shafts.Overlap(b.shafts[I(0)], b.shafts[I(1)], a[2].GetDouble());
                case "addWall": Walls.Add(b, I(0), V(a[1]), V(a[2])); return null;
                case "removeJoint": return Walls.RemoveJoint(b, I(0), a[1].GetString()!);
                case "dragJoint": return Walls.MoveJoint(b, I(0), a[1].GetString()!, V(a[2]), a[3].GetBoolean());
                case "splitDrag": Walls.SplitAndDrag(b, I(0), I(1), V(a[2]), a[3].GetBoolean()); return null;
                case "toggleDoor": return Walls.ToggleDoor(site, b, I(0), V(a[1]));
                case "erase": { var t = Walls.HoverTarget(b, I(0), V(a[1])); if (t == null) return false; Walls.Erase(b, I(0), t.Value); return true; }
                case "placeCore": return Shafts.Place(b, I(0), V(a[1]), Type(a[2]), a[3].GetDouble(), a[4].GetString()!) != null;
                case "dragCore": return Shafts.Drag(b, b.shafts[I(0)], V(a[1]), V(a[2]));
                case "setAngle": return Shafts.SetAngle(b, b.shafts[I(0)], a[1].GetDouble());
                case "alignCore": return Shafts.Align(b, b.shafts[I(0)]);
                case "setRange": if (a[1].GetString() == "bottom") Shafts.SetBottom(b.shafts[I(0)], I(2)); else Shafts.SetTop(b.shafts[I(0)], I(2)); return null;
                default: throw new NotSupportedException("no C# step for " + op);
            }
        }

        internal static object? Door(DoorSpot? d)
        {
            if (d == null) return null;
            return d.exterior
                ? new Dictionary<string, object?> { ["kind"] = "ext", ["edge"] = d.edge, ["t"] = d.t, ["ei"] = d.entrance, ["L"] = d.L, ["k"] = d.k }
                : new Dictionary<string, object?> { ["kind"] = "int", ["wi"] = d.wall, ["t"] = d.t, ["di"] = d.door, ["L"] = d.L };
        }


        /// <summary>A wall wholly over a setback's terrace builds nothing: the editor refuses it, the problem list flags one.</summary>
        [Fact]
        public void AWallOnTheTerraceIsRefusedAndFlagged()
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));   // 12 × 9 m
            b.floors = Enumerable.Range(0, 3).Select(_ => new FloorData()).ToList();
            b.floors[2].shape = new List<Vec2> { new Vec2(0, 0), new Vec2(6, 0), new Vec2(6, 9), new Vec2(0, 9) };   // set back to the west half
            Assert.True(Walls.Inside(b, 2, new Vec2(1, 4), new Vec2(5, 4)));        // inside the setback storey
            Assert.True(Walls.Inside(b, 2, new Vec2(0, 4), new Vec2(12, 4)));       // across it and the terrace: the inside part is built
            Assert.True(Walls.Inside(b, 2, new Vec2(0, 2), new Vec2(6, 2)));        // from wall to wall
            Assert.False(Walls.Inside(b, 2, new Vec2(8, 2), new Vec2(11, 6)));      // wholly on the terrace
            Assert.True(Walls.Inside(b, 1, new Vec2(8, 2), new Vec2(11, 6)));       // the storey below has room there
            b.floors[2].walls.Add(new WallData { a = new Vec2(8, 2), b = new Vec2(11, 6) });
            var site = new Site(d.buildings);
            Assert.Contains(Validate.Problems.Check(site), p => p.code == "wall-outside" && p.k == 2);
        }
    }
}
