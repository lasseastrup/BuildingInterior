using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A building hidden at run time (docs/PLAY.md §6; <c>StoreySite.SetHidden</c>, <c>StoreyBuildingHandle</c>), for a
    /// game's own occlusion: its state row's displayed LOD goes past <see cref="StateRow.Hidden"/>, and the shaders draw
    /// none of it in the colour and depth passes, at any LOD and in its cell, while the shadow caster draws it as before.
    /// The shaders and the Unity half only compile in Unity, so their logic is mirrored here and their source pinned to
    /// it; the row's encoding and the occlusion core are the package's own. Storey's own.
    /// </summary>
    public class HiddenBuildingTests
    {
        static string Text(params string[] path) => File.ReadAllText(UnityAsset.Package("com.triband.storey", path));
        static string Code(params string[] path) => Regex.Replace(Text(path), @"//[^\n]*", "");

        /// <summary>A state row: displayed LOD, previous LOD, cross-fade, occluder slot + 1.</summary>
        readonly record struct Row(float x, float y, float z, float w);

        // the building table's writes (BuildingTable.SetLod and SetHidden, pinned below)
        static Row SetLod(Row s, int displayed, int from, float fade) => new Row(StateRow.Displayed(displayed, StateRow.IsHidden(s.x)), from, fade, s.w);
        static Row SetHidden(Row s, bool hidden) => s with { x = StateRow.Hide(s.x, hidden) };

        enum Pass { Colour, Depth, Shadow }

        /// <summary>
        /// StoreyVertex's answer (StoreyOcclusion.hlsl, pinned below): whether a vertex of a mesh of LOD <paramref name="lod"/>
        /// (2: the massing in its cell) is drawn in a pass, and the fade it hands on. <paramref name="kind"/> is LOD0's cutaway
        /// kind, whose party walls name their neighbour (kind + 8 × (its row + 1)), and <paramref name="rows"/> the table.
        /// </summary>
        static (bool shown, float fade) Vertex(Row st, int lod, Pass pass, bool massing, int kind = 0, Func<int, Row>? rows = null)
        {
            float shown = StateRow.Shown(st.x);
            bool isDisp, isFrom;
            if (pass == Pass.Shadow && massing) { isDisp = shown > 0.5f && shown < 2.5f; isFrom = st.z < 1 && st.y > 0.5f && st.y < 2.5f; }
            else { isDisp = Math.Abs(lod - shown) < 0.5f; isFrom = st.z < 1 && Math.Abs(lod - st.y) < 0.5f; }
            float fade = st.z >= 1 || (isDisp && isFrom) ? 3 : isDisp ? st.z : -1 - st.z;
            if (pass != Pass.Shadow && StateRow.IsHidden(st.x))
            {
                int im = (int)Math.Floor(kind / 8.0) - 1;
                if (lod > 0.5f || im < 0 || StateRow.IsHidden(rows!(im).x)) return (false, fade);
            }
            return (isDisp || isFrom, fade);
        }

        /// <summary>StoreyVertex as it was before buildings could be hidden: what a row that isn't hidden must still give.</summary>
        static (bool shown, float fade) Before(Row st, int lod, Pass pass, bool massing)
        {
            bool isDisp, isFrom;
            if (pass == Pass.Shadow && massing) { isDisp = st.x > 0.5f && st.x < 2.5f; isFrom = st.z < 1 && st.y > 0.5f && st.y < 2.5f; }
            else { isDisp = Math.Abs(lod - st.x) < 0.5f; isFrom = st.z < 1 && Math.Abs(lod - st.y) < 0.5f; }
            return (isDisp || isFrom, st.z >= 1 || (isDisp && isFrom) ? 3 : isDisp ? st.z : -1 - st.z);
        }

        /// <summary>Every row the LOD manager and the occlusion write: each LOD shown and fading from, mid-fade, done, a late LOD0's hold, in the way or not.</summary>
        static IEnumerable<Row> Rows()
        {
            foreach (int x in new[] { 0, 1, 2, 3 }) foreach (int y in new[] { 0, 1, 2, 3 }) foreach (float z in new[] { 0f, 0.4f, 0.999f, 1f, 1.6f, 2f }) foreach (float w in new[] { 0f, 3f })
                yield return new Row(x, y, z, w);
        }

        static readonly (int lod, bool massing)[] Meshes = { (0, false), (1, false), (2, false), (2, true) };   // LOD0, LOD1, the fixed LOD's massing, a cell

        [Fact]
        public void HidingAndShowingGivesBackTheRowForEveryLod()
        {
            foreach (float lod in new[] { 0f, 1, 2, 3 })
            {
                float hidden = StateRow.Hide(lod, true);
                Assert.True(StateRow.IsHidden(hidden)); Assert.False(StateRow.IsHidden(lod));
                Assert.Equal(lod, StateRow.Shown(hidden)); Assert.Equal(lod, StateRow.Shown(lod));
                Assert.Equal(hidden, StateRow.Hide(hidden, true));   // hidden twice is hidden once
                Assert.Equal(BitConverter.SingleToInt32Bits(lod), BitConverter.SingleToInt32Bits(StateRow.Hide(hidden, false)));
                Assert.Equal(lod, StateRow.Hide(lod, false));
            }
            foreach (var r in Rows())
            {
                var h = SetHidden(r, true);
                Assert.Equal((r.y, r.z, r.w), (h.y, h.z, h.w));   // the rest of the row is as it was
                Assert.Equal(r, SetHidden(h, false));
            }
        }

        /// <summary>
        /// Nothing of a hidden building is drawn in the colour pass or the depth prepass (which must match it), whatever its
        /// LOD state: LOD0, LOD1, or its massing in its cell or alone, shown or fading out. A row that isn't hidden is drawn
        /// exactly as before.
        /// </summary>
        [Fact]
        public void AHiddenBuildingDrawsNothingInTheColourPassAtAnyLodNorInItsCell()
        {
            int drawnBefore = 0;
            foreach (var r in Rows())
                foreach (var (lod, massing) in Meshes)
                    foreach (var pass in new[] { Pass.Colour, Pass.Depth, Pass.Shadow })
                    {
                        Assert.Equal(Before(r, lod, pass, massing), Vertex(r, lod, pass, massing));
                        if (pass == Pass.Shadow) continue;
                        if (Before(r, lod, pass, massing).shown) drawnBefore++;
                        Assert.False(Vertex(SetHidden(r, true), lod, pass, massing).shown, $"{r} LOD {lod}{(massing ? " in its cell" : "")}, {pass}");
                    }
            Assert.True(drawnBefore > 100, "the rows draw something before they are hidden");
        }

        /// <summary>The shadow caster draws a hidden building as it would anyway: its LOD0 and LOD1, and its massing as their proxy.</summary>
        [Fact]
        public void AHiddenBuildingCastsItsShadowAsBefore()
        {
            int cast = 0;
            foreach (var r in Rows())
                foreach (var (lod, massing) in Meshes)
                {
                    var (shown, fade) = Vertex(SetHidden(r, true), lod, Pass.Shadow, massing);
                    Assert.Equal(Before(r, lod, Pass.Shadow, massing), (shown, fade));
                    if (shown) cast++;
                }
            Assert.True(cast > 100);
        }

        /// <summary>
        /// LOD0's party walls are the neighbour's walls too (SPEC §4.3): a hidden building's stay in the colour pass while
        /// the neighbour they are built for shows, as Sink keeps them standing, and go when it is hidden too. LOD1 and the
        /// massing carry no neighbour, so all of theirs goes.
        /// </summary>
        [Fact]
        public void APartyWallStaysWhileTheNeighbourItIsBuiltForShows()
        {
            var shown = new Row(0, 0, 1, 0); var a = SetHidden(shown, true);
            var table = new Dictionary<int, Row> { [5] = shown, [6] = SetHidden(new Row(0, 0, 1, 0), true) };
            Row At(int i) => table.TryGetValue(i, out var r) ? r : new Row(3, 3, 1, 0);
            int Party(int mate, int kind = 1) => kind + 8 * (mate + 1);
            Assert.True(Vertex(a, 0, Pass.Colour, false, Party(5), At).shown);    // its neighbour shows
            Assert.True(Vertex(a, 0, Pass.Depth, false, Party(5), At).shown);
            Assert.False(Vertex(a, 0, Pass.Colour, false, Party(6), At).shown);   // its neighbour is hidden too
            Assert.False(Vertex(a, 0, Pass.Colour, false, 1, At).shown);          // a wall of its own
            Assert.False(Vertex(SetLod(a, 1, 0, 1), 1, Pass.Colour, false, Party(5), At).shown);   // LOD1 carries no kinds
            Assert.False(Vertex(SetLod(a, 2, 1, 1), 2, Pass.Colour, true, 0, At).shown);
            // shown as it would be: fading out with its LOD0
            var fading = SetLod(a, 1, 0, 0.3f);
            Assert.Equal(Before(SetHidden(fading, false), 0, Pass.Colour, false), Vertex(fading, 0, Pass.Colour, false, Party(5), At));
        }

        /// <summary>
        /// The LOD manager carries on under a hidden building: it fades in and out of its massing, LOD1 and LOD0 as the camera
        /// comes and goes, a late LOD0's windows clear, and it is hidden and shown at any point of that. The row the table
        /// holds always shows what the manager says, its shadow is cast from it, and shown again it is the row it would
        /// have been.
        /// </summary>
        [Fact]
        public void HiddenThroughLodChangesAndCrossFadesItComesBackAsItWouldHaveBeen()
        {
            var lods = new LodManager(); lods.Set(0, 0, 0, 20, 20, 30);
            var plain = new Row(3, 3, 1, 0); var row = plain; bool hidden = false; int changes = 0, hiddenFrames = 0;
            var rnd = new Random(7);
            for (int f = 0; f < 1200; f++)
            {
                double d = 10 + 600 * (0.5 + 0.5 * Math.Cos(f * Math.PI / 300));   // in to 10 m, out to 610 m, and back
                var fr = lods.Update(10 - d, 2, 10, 1200, 1.0 / 60);
                foreach (var (idx, lod, _) in fr.Build) lods.SetBuilt(idx, lod, true);
                if (rnd.NextDouble() < 0.05) { hidden = !hidden; row = SetHidden(row, hidden); }
                var e = lods.Get(0)!;
                foreach (int idx in fr.Changed.Concat(fr.Held)) { row = SetLod(row, e.Shown, e.From, e.RowFade); plain = SetLod(plain, e.Shown, e.From, e.RowFade); changes++; }
                Assert.Equal(hidden, StateRow.IsHidden(row.x));
                Assert.Equal(plain, SetHidden(row, false));
                Assert.Equal((float)e.Shown, StateRow.Shown(row.x));
                foreach (var (lod, massing) in Meshes)
                {
                    Assert.Equal(Before(plain, lod, Pass.Shadow, massing), Vertex(row, lod, Pass.Shadow, massing));
                    Assert.Equal(hidden ? (false, Before(plain, lod, Pass.Colour, massing).fade) : Before(plain, lod, Pass.Colour, massing), Vertex(row, lod, Pass.Colour, massing));
                }
                if (hidden) hiddenFrames++;
            }
            Assert.True(changes > 50 && hiddenFrames > 100, $"{changes} LOD changes, {hiddenFrames} frames hidden");
        }

        [Fact]
        public void TheShadersReadTheRowAsThisDoes()
        {
            var occ = Code("Unity", "Shaders", "StoreyOcclusion.hlsl");
            Assert.Equal(StateRow.Hidden, float.Parse(Regex.Match(occ, @"#define\s+STOREY_HIDDEN\s+(\d+)").Groups[1].Value));
            Assert.Contains("bool StoreyHidden(float4 st) { return st.x > STOREY_HIDDEN - 0.5; }", occ);
            Assert.Contains("float StoreyShownLod(float4 st) { return StoreyHidden(st) ? st.x - STOREY_HIDDEN : st.x; }", occ);
            int at = occ.IndexOf("bool StoreyVertex(", StringComparison.Ordinal), end = occ.IndexOf("\n}", at, StringComparison.Ordinal);
            var vertex = occ.Substring(at, end - at);
            Assert.Contains("float shownLod = StoreyShownLod(st);", vertex);
            Assert.Matches(new Regex(@"#if defined\(STOREY_SHADOW\) && defined\(STOREY_MASSING\)\s+bool isDisp = shownLod > 0\.5 && shownLod < 2\.5;\s+bool isFrom = st\.z < 1\.0 && st\.y > 0\.5 && st\.y < 2\.5;\s+#else\s+bool isDisp = abs\(v\.lod - shownLod\) < 0\.5;\s+bool isFrom = st\.z < 1\.0 && abs\(v\.lod - st\.y\) < 0\.5;\s+#endif"), vertex);
            // the colour and depth passes alone, after the fade is worked out, the neighbour read only for a hidden building;
            // one way out, through a float (a bool returned from inside the branch became a cast Metal's compiler refuses)
            Assert.Matches(new Regex(@"v\.hold = [^;]+;\s+float shown = \(isDisp \|\| isFrom\) \? 1\.0 : 0\.0;\s+#ifndef STOREY_SHADOW\s+\[branch\] if \(StoreyHidden\(st\)\)\s+\{\s+int im = \(int\)floor\(kind / 8\.0\) - 1;\s+if \(v\.lod > 0\.5 \|\| im < 0 \|\| StoreyHidden\(_StoreyState\[StoreyRow\(im\)\]\)\) shown = 0\.0;\s+\}\s+#endif\s+return shown > 0\.5;\s*$"), vertex);
            Assert.DoesNotContain("return false", vertex);
            // every shader passes its shadow caster STOREY_SHADOW and its depth prepass not (ShadowCasterTests counts them)
            foreach (var f in Directory.GetFiles(Path.Combine(UnityAsset.Package("com.triband.storey"), "Unity", "Shaders"), "*.shader"))
                foreach (var pass in Regex.Split(Regex.Replace(File.ReadAllText(f), @"//[^\n]*", ""), @"\bPass\s*\{").Skip(1))
                    Assert.Equal(pass.Contains("\"LightMode\" = \"ShadowCaster\""), pass.Contains("#define STOREY_SHADOW"));
            // the row's displayed LOD is read nowhere else: everything else goes through the two
            foreach (var f in Directory.GetFiles(Path.Combine(UnityAsset.Package("com.triband.storey"), "Unity", "Shaders"), "*.hlsl"))
            {
                var src = Regex.Replace(File.ReadAllText(f), @"//[^\n]*", "");
                Assert.DoesNotMatch(new Regex(@"_StoreyState\[[^;\n]*?\]\.x\b"), src);
                foreach (var line in src.Split('\n').Where(l => Regex.IsMatch(l, @"\bst\.x\b")))
                    Assert.True(line.Contains("bool StoreyHidden(float4 st)") || line.Contains("float StoreyShownLod(float4 st)"), $"{Path.GetFileName(f)}: {line.Trim()}");
            }
            // the props go with their building, but for their shadows
            var prop = Code("Unity", "Shaders", "StoreyProp.hlsl");
            Assert.Matches(new Regex(@"#if defined\(SHADERPASS\) && defined\(SHADERPASS_SHADOWCASTER\) && !defined\(STOREY_SHADOW\)\s+#if SHADERPASS == SHADERPASS_SHADOWCASTER\s+#define STOREY_SHADOW"), prop);
            Assert.Matches(new Regex(@"bool StoreyPropShown\(int ib\)\s+\{\s+float4 st = _StoreyState\[StoreyRow\(ib\)\];\s+float shown = \(StoreyShownLod\(st\) < 0\.5 \|\| \(st\.z < 1\.0 && st\.y < 0\.5\)\) \? 1\.0 : 0\.0;\s+#ifndef STOREY_SHADOW\s+if \(StoreyHidden\(st\)\) shown = 0\.0;\s+#endif\s+return shown > 0\.5;"), prop);
        }

        /// <summary>
        /// The table and the renderer hold a hidden building as mirrored here: its LOD writes keep it hidden, hiding changes
        /// the displayed LOD alone (so the steady variant's answer stands, Steady.Same), a row let go is shown, and a building
        /// built (again, or for the first time as a layout loads) is hidden as it is built.
        /// </summary>
        [Fact]
        public void TheTableAndTheRendererHoldItAsThisDoes()
        {
            var table = Text("Unity", "Rendering", "BuildingTable.cs");
            Assert.Contains("SetState(idx, new Vector4(Lod.StateRow.Displayed(displayed, Lod.StateRow.IsHidden(s.x)), from, fade, s.w));", table);
            Assert.Contains("SetState(idx, new Vector4(Lod.StateRow.Hide(s.x, hidden), s.y, s.z, s.w));", table);
            Assert.Equal(2, Regex.Matches(table, @"new Vector4\(3, 3, 1, 0\)").Count);   // a new table and a block given back: shown
            var r = Text("Unity", "Rendering", "SiteRenderer.cs");
            Assert.Contains("table.SetState(b.idx, new Vector4(3, 3, 1, 0));", r);   // a building let go
            Assert.Contains("if (built.TryGetValue(id, out var bt)) table.SetHidden(bt.idx, hide);", r);
            int build = r.IndexOf("void Build(BuildingData b, Lod2Mesh? made = null)", StringComparison.Ordinal);
            int made = r.IndexOf("built[b.id] = bt; byIdx[bt.idx] = bt;", build, StringComparison.Ordinal);
            int hide = r.IndexOf("if (hiddenIds.Count > 0 && hiddenIds.Contains(b.id)) table.SetHidden(bt.idx, true);", build, StringComparison.Ordinal);
            Assert.True(made > build && hide > made && hide < r.IndexOf("if (lods == null)", build, StringComparison.Ordinal), "hidden as it is built, before either LOD path writes its row");
            Assert.Equal(2, Regex.Matches(r, @"table\.SetHidden\(").Count);
        }

        /// <summary>
        /// Nothing about hiding runs per frame: the row is written when a building is hidden or shown, or built, and the
        /// shaders do the rest. The renderer's frame, its LOD and steady passes and the site's own frame never look at it;
        /// the occlusion asks only while something is hidden.
        /// </summary>
        [Fact]
        public void NothingIsDoneForItPerFrame()
        {
            var r = Text("Unity", "Rendering", "SiteRenderer.cs"); var site = Text("Unity", "Rendering", "StoreySite.cs");
            string Method(string text, string start)
            {
                int a = text.IndexOf(start, StringComparison.Ordinal); Assert.True(a >= 0, start);
                int b = text.IndexOf("\n        }", a, StringComparison.Ordinal);
                return text.Substring(a, b - a);
            }
            var named = new Regex(@"\b(hiddenIds|SetHidden|IsHidden|Hidden|StateRow)\b");
            foreach (var m in new[] { "public void Frame(bool lodTint, SiteView? view = null)", "void UpdateLods()", "void UpdateSteady()", "void TakeLoad(System.Diagnostics.Stopwatch? sw)", "void BindAndUpload()" })
                Assert.DoesNotMatch(named, Method(r, m));
            Assert.DoesNotMatch(named, Method(Text("Unity", "Rendering", "BuildingTable.cs"), "public void Upload()"));
            Assert.DoesNotMatch(named, Method(site, "void LateUpdate()"));
            // the site gives its buildings to a renderer once, as it is made, before anything is built
            var refresh = Method(site, "public void Refresh()");
            int made = refresh.IndexOf("site = new SiteRenderer(", StringComparison.Ordinal), given = refresh.IndexOf("foreach (var id in hiddenIds) site.SetHidden(id, true);", StringComparison.Ordinal);
            Assert.True(made > 0 && given > made && given < refresh.IndexOf("site.Layer = gameObject.layer;", StringComparison.Ordinal));
            Assert.Single(Regex.Matches(refresh, @"hiddenIds"));
            // the sets have room for a few: hiding and showing them make no garbage
            Assert.Contains("readonly HashSet<string> hiddenIds = new HashSet<string>(16, StringComparer.Ordinal);", r);
            Assert.Contains("readonly HashSet<string> hiddenIds = new HashSet<string>(16, StringComparer.Ordinal);", site);
            Assert.Contains("if (b == held || AtDoorOf(b, p) || (Hidden != null && Hidden.Count > 0 && Hidden.Contains(b.id))) continue;", Text("Runtime", "Occlusion", "OcclusionCore.cs"));
            Assert.Contains("&& !r.IsHidden(oc.b.id))", Text("Unity", "Occlusion", "StoreyOcclusion.cs"));
            Assert.Contains("public bool IsHidden(string id) => hiddenIds.Count > 0 && hiddenIds.Contains(id);", r);
            // what a call does, as the renderer does it: no garbage
            var ids = new HashSet<string>(16, StringComparer.Ordinal); var built = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 3, ["b"] = 9 };
            var dirty = new DirtyChunks(8192, 64); var x = new float[8192];
            void Call(string id, bool hide) { if (hide ? !ids.Add(id) : !ids.Remove(id)) return; if (built.TryGetValue(id, out int i)) { x[i] = StateRow.Hide(x[i], hide); dirty.Mark(i); } }
            Call("a", true); Call("a", false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) { Call("a", i % 2 == 0); Call("b", i % 3 == 0); Call("c", i % 5 == 0); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>
        /// The handle stands for its building: switched on it shows it, switched off it hides it, and does nothing per frame.
        /// A site not loaded yet hides it as it is built; a site destroyed first is left alone (Unity's null).
        /// </summary>
        [Fact]
        public void TheHandleHidesItsBuildingWhileSwitchedOff()
        {
            var handle = Code("Unity", "Rendering", "StoreyBuildingHandle.cs");
            Assert.Contains("void OnEnable() { if (site != null) site.SetHidden(buildingId, false); }", handle);
            Assert.Contains("void OnDisable() { if (site != null) site.SetHidden(buildingId, true); }", handle);
            Assert.Contains("public StoreySite? site;", handle); Assert.Contains("public string buildingId = \"\";", handle);
            Assert.Contains("public sealed class StoreyBuildingHandle : MonoBehaviour", handle);
            Assert.Contains("[AddComponentMenu(\"Storey/Storey Building Handle\")]", handle);
            Assert.Contains("[DisallowMultipleComponent]", handle);
            Assert.DoesNotContain("ExecuteAlways", handle);   // Play mode only: the editor's objects switched off hide nothing
            Assert.DoesNotMatch(new Regex(@"\b(Update|LateUpdate|FixedUpdate|OnGUI)\s*\("), handle);
            Assert.DoesNotContain("transform.position =", handle);   // it shows where its building is, and moves nothing
            // the site keeps what it is told while it has no renderer, and passes it on as it has one
            var site = Code("Unity", "Rendering", "StoreySite.cs");
            Assert.Matches(new Regex(@"public void SetHidden\(string buildingId, bool hidden\)\s+\{\s+if \(hidden \? !hiddenIds\.Add\(buildingId\) : !hiddenIds\.Remove\(buildingId\)\) return;\s+site\?\.SetHidden\(buildingId, hidden\);\s+\}"), site);
        }

        /// <summary>
        /// Storey's own occlusion (every mode) never takes a hidden building for one in the way: it draws nothing. The
        /// prototype's walks replayed twice, the second time with every building that was in the way hidden; and with
        /// buildings hidden, a frame still makes no garbage.
        /// </summary>
        [Theory]
        [InlineData(OccluderMode.Sink)]
        [InlineData(OccluderMode.Slice)]
        [InlineData(OccluderMode.Cutout)]
        [InlineData(OccluderMode.Fade)]
        [InlineData(OccluderMode.Dissolve)]
        public void AHiddenBuildingIsNeverInTheWay(OccluderMode mode)
        {
            var (doc, _) = PlayWorldTests.World("demo");
            var world = new PlayWorld(new Site(doc.buildings));
            var inTheWay = new HashSet<string>(StringComparer.Ordinal);
            Replay(world, mode, null, inTheWay, out _);
            Assert.NotEmpty(inTheWay);
            var hidden = new HashSet<string>(inTheWay, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Replay(world, mode, hidden, seen, out long bytesAFrame);
            Assert.Empty(seen.Intersect(hidden));
            Assert.True(bytesAFrame < 64, $"{bytesAFrame} bytes a frame");
        }

        /// <summary>
        /// The prototype's walks (play.json) under <paramref name="mode"/>, each twice on one core, as FrameAllocTests walks
        /// them: the buildings in the way into <paramref name="inTheWay"/>, and the garbage a frame of the second time.
        /// </summary>
        static void Replay(PlayWorld world, OccluderMode mode, HashSet<string>? hidden, HashSet<string> inTheWay, out long bytesAFrame)
        {
            var root = PlayWorldTests.Play.Value.RootElement; long bytes = 0; int frames = 0;
            foreach (var w in root.GetProperty("walks").EnumerateArray())
            {
                var core = new OcclusionCore(world, new OcclusionSettings { mode = mode }) { Hidden = hidden };
                var start = w.GetProperty("start").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                var c = w.GetProperty("cam").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                var inputs = w.GetProperty("frames").EnumerateArray().Select(fr =>
                {
                    var input = fr.GetProperty("input");
                    bool K(string k) => input.TryGetProperty(k, out var v) && v.GetBoolean();
                    double? yaw = input.TryGetProperty("yaw", out var yv) ? yv.GetDouble() : (double?)null;
                    return (move: new MoveInput((K("KeyD") ? 1 : 0) - (K("KeyA") ? 1 : 0), (K("KeyW") ? 1 : 0) - (K("KeyS") ? 1 : 0), K("ShiftLeft")), yaw);
                }).ToArray();
                for (int pass = 0; pass < 2; pass++)
                {
                    core.Clear();
                    var p = new PlayerState(); p.Spawn(start[0], start[1], start[2]);
                    var cam = new FollowCamera { yaw = c[0], pitch = c[1], dist = c[2], assist = 0 }; cam.Snap(p);
                    foreach (var (move, yaw) in inputs)
                    {
                        if (yaw is double y) cam.yaw = y;
                        Walker.Step(world, p, move, cam.yaw, 1.0 / 30);
                        cam.Follow(p, 1.0 / 30);
                        var pos = cam.Position();
                        long a0 = GC.GetAllocatedBytesForCurrentThread();
                        core.Frame(p, pos, (cam.tx, cam.ty, cam.tz), 1.0 / 30);
                        if (pass == 1) { bytes += GC.GetAllocatedBytesForCurrentThread() - a0; frames++; }
                        foreach (var o in core.Active) inTheWay.Add(o.b.id);
                    }
                }
            }
            bytesAFrame = bytes / Math.Max(1, frames);
        }
    }
}
