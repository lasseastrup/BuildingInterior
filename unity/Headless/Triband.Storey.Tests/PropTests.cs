using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Triband.Storey.Props;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Props inside buildings (docs/PROPS.md): which building a prop is in, and the CPU fallback's rule.</summary>
    public class PropTests
    {
        static (Site site, BuildingData a, BuildingData b) TwoHouses(double elevB = 0)
        {
            var d = new StoreyDocument();
            BuildingData One(double x0, double elev)
            {
                var b = Edit.Buildings.Add(d, "rect", new Vec2(0, 0));
                b.pos = new Vec2(x0, 0); b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 8), new Vec2(0, 8) };
                b.floors = Enumerable.Range(0, 3).Select(_ => new FloorData()).ToList(); b.elevation = elev;
                return b;
            }
            One(0, 0); One(30, elevB);
            var site = new Site(d.buildings);
            return (site, site.Buildings[0], site.Buildings[1]);
        }

        [Fact]
        public void APropIsInTheBuildingWhoseOutlineHoldsIt()
        {
            var (site, a, b) = TwoHouses();
            Assert.Same(a, PropLocator.BuildingAt(site, 5, 0.5, 4));            // ground floor
            Assert.Same(a, PropLocator.BuildingAt(site, 5, 7.0, 4));            // second floor
            Assert.Same(b, PropLocator.BuildingAt(site, 35, 4.0, 4));
            Assert.Null(PropLocator.BuildingAt(site, 20, 0.5, 4));              // the street between
            Assert.Null(PropLocator.BuildingAt(site, 5, 0.5, 9));               // out of the window, on the ground
            Assert.Null(PropLocator.BuildingAt(site, 5, 40, 4));                // well above the roof
        }

        [Fact]
        public void APropUnderARaisedBuildingIsntInIt()
        {
            var (site, _, b) = TwoHouses(elevB: 6);
            Assert.Null(PropLocator.BuildingAt(site, 35, 0.3, 4));
            Assert.Same(b, PropLocator.BuildingAt(site, 35, 6.3, 4));
        }

        [Fact]
        public void ANeighbouringDistrictsBuildingIsNeverAPropsHome()
        {
            var (site, a, _) = TwoHouses();
            var other = new List<BuildingData> { PrototypeJson.ReadBuilding(PrototypeJson.Write(a)) };
            var withEdge = Site.WithContext(new List<BuildingData> { site.Buildings[1] }, "Here", new[] { (other, "There", 21.0, 0.0) });
            Assert.Null(PropLocator.BuildingAt(withEdge, 26, 0.5, 4));         // the neighbour's copy, 21 m east: there for its walls only
            Assert.NotNull(PropLocator.BuildingAt(withEdge, 35, 0.5, 4));
        }

        [Fact]
        public void TheUserValueIsTheTableIndexPlusOne()
        {
            Assert.Equal(0u, PropLocator.Encode(-1));
            Assert.Equal(1u, PropLocator.Encode(0));
            Assert.Equal(8176u, PropLocator.Encode(8175));
            Assert.Equal(-1, PropLocator.Decode(0)); Assert.Equal(41, PropLocator.Decode(42));
        }

        [Fact]
        public void LocatingMakesNoGarbage()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; Edit.TestCity.Generate(d, 300);
            var site = new Site(d.buildings); var rnd = new Random(3); int found = 0;
            for (int i = 0; i < 200; i++) PropLocator.BuildingAt(site, rnd.NextDouble() * 200 - 100, 1, rnd.NextDouble() * 200 - 100);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) if (PropLocator.BuildingAt(site, rnd.NextDouble() * 400 - 200, rnd.NextDouble() * 10, rnd.NextDouble() * 400 - 200) != null) found++;
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - a0);
            Assert.True(found > 100, $"only {found} of 2,000 points landed in a building");
        }

        [Fact]
        public void TheFallbackHidesWhatTheShaderWould()
        {
            // the player on a's ground floor: a's upper floors are hidden, its ground floor isn't, b's floors aren't
            var (site, a, b) = TwoHouses();
            var core = new OcclusionCore(new PlayWorld(site), new OcclusionSettings { mode = OccluderMode.Sink });
            var p = new PlayerState(); p.Spawn(5, 0, 4);
            core.Frame(p, (5, 6, -6), (5, 1.4, 4), 1.0 / 30);
            Assert.Same(a, core.View.active);
            Assert.False(core.HidesPoint(a, 1.0));
            Assert.True(core.HidesPoint(a, 5.0));
            Assert.False(core.HidesPoint(b, 5.0));
        }

        [Fact]
        public void TheFallbackHidesTheStoreysOfASunkBuilding()
        {
            // the player in the street behind b, the camera beyond it: b sinks to the player's storey, a prop upstairs goes
            var (site, _, b) = TwoHouses();
            var core = new OcclusionCore(new PlayWorld(site), new OcclusionSettings { mode = OccluderMode.Sink });
            var p = new PlayerState(); p.Spawn(35, 0, 14);
            for (int f = 0; f < 30; f++) core.Frame(p, (35, 8, -8), (35, 1.4, 14), 1.0 / 30);
            Assert.Contains(core.Active, r => r.b == b);
            Assert.True(core.HidesPoint(b, 7.0));
            Assert.True(core.HidesPoint(b, 1.0));    // the player's own storey squashes to a low base too
            Assert.False(core.HidesPoint(b, -0.5));  // below it: nothing
        }

        /// <summary>
        /// docs/PERFORMANCE.md §11.1: the props run after the sites, so they read this frame's rows; in Play mode their frame
        /// visits the moving props alone; a prop leaves its lists by the last taking its place; and a prop material's steady
        /// copy is made only for a graph with the STOREY_STEADY keyword whose alpha clip isn't baked in, in Play mode, from
        /// the building's own steady answer.
        /// </summary>
        [Fact]
        public void ThePropsFrameIsTheMovingPropsAndTheirSteadyCopiesAreTheGraphsOwn()
        {
            string Text(params string[] path) => System.IO.File.ReadAllText(UnityAsset.Package("com.triband.storey", path));
            var props = Text("Unity", "Props", "StoreyProps.cs");
            int Order(string text) => int.Parse(System.Text.RegularExpressions.Regex.Match(text, @"\[DefaultExecutionOrder\((\d+)\)\]").Groups[1].Value);
            Assert.True(Order(props) > Order(Text("Unity", "Rendering", "StoreySite.cs")), "the props after the sites");
            Assert.Contains("if (playing)\n                for (int i = 0; i < moving.Count; i++)", props.Replace("\r\n", "\n"));
            Assert.DoesNotContain("readonly List<Entry> entries", props);
            Assert.Contains("items[p] = moved; Place(moved) = p;", props);
            Assert.Contains("FindKeyword(\"STOREY_STEADY\")", props);
            Assert.Contains("bool baked = !clip.isValid && m.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.AlphaTest;", props);
            Assert.Contains("bool steady = playing && r != null && !off && r.PropSteady(idx);", props);
            Assert.Contains("global::Triband.Storey.Occlusion.Steady.Of(idx, st.z, st.w, StoreyGlobals.ActiveIndex, StoreyGlobals.IsolateAmount, StoreyGlobals.IsolatedIndex)", Text("Unity", "Rendering", "SiteRenderer.cs"));
        }

        /// <summary>
        /// docs/PROPS.md §2.6: in Play mode a prop's rigid body is held still while there may be nothing under it. Whether is
        /// decided before the next physics step (a scene's props register before its site has read its layout), not as the
        /// prop registers (the game may set its velocity just after); a prop that stops being a prop lets go; a held prop
        /// isn't looked for again, as nothing moves it; a hold takes continuous detection down to speculative before making
        /// the body kinematic, and the body is dynamic again before its detection and velocities are given back; each
        /// building's collider is asked for once a frame for all its props; and props wait by default.
        /// </summary>
        [Fact]
        public void APropsRigidBodyIsHeldUntilTheFloorUnderItIsThere()
        {
            string Text(params string[] path) => System.IO.File.ReadAllText(UnityAsset.Package("com.triband.storey", path)).Replace("\r\n", "\n");
            var props = Text("Unity", "Props", "StoreyProps.cs");
            string Method(string start, string next)
            {
                int a = props.IndexOf(start, System.StringComparison.Ordinal); Assert.True(a > 0, start);
                int b = props.IndexOf(next, a, System.StringComparison.Ordinal); Assert.True(b > a, next);
                return props.Substring(a, b - a);
            }
            int At(string text, string what) { int i = text.IndexOf(what, System.StringComparison.Ordinal); Assert.True(i >= 0, what); return i; }
            Assert.Contains("Settle();", Method("void FixedUpdate()", "void Fresh(Entry e)"));
            var register = Method("internal static void Register(StoreyProp p)", "internal static void Unregister(");
            Assert.True(At(register, "m.Fresh(e);") < At(register, "m.Locate(e);"), "looked at later, not as it registers");
            Assert.Contains("!e.fresh", Method("void UpdateHold(Entry e)", "void Hold(Entry e)"));
            Assert.Contains("m.Let(e);", Method("internal static void Unregister(StoreyProp p)", "internal static void Refresh("));
            Assert.Contains("if (e.prop == null || e.held || (e.body != null && e.body.IsSleeping())) continue;", props);
            var hold = Method("void Hold(Entry e)", "void Let(Entry e)");
            Assert.True(At(hold, "= CollisionDetectionMode.ContinuousSpeculative;") < At(hold, "body.isKinematic = true;"));
            var let = Method("void Let(Entry e)", "// ---- prop materials' steady copies");
            Assert.True(At(let, "body.isKinematic = false;") < At(let, "body.collisionDetectionMode = h.mode;"));
            Assert.True(At(let, "body.isKinematic = false;") < At(let, "body.linearVelocity = h.v;"));
            Assert.Contains("bool ground = Ground(playing, r, idx);", Method("foreach (var kv in groups)", "// props drawn whole"));
            Assert.Contains("public bool waitForCollider = true;", Text("Unity", "Props", "StoreyProp.cs"));
        }
    }
}
