using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The steady variant (<see cref="Steady"/>, docs/CITY.md §2): a building draws without discard only while nothing
    /// can hide its pixels, the shaders keep every discard out of that variant, and a full-detail mesh knows the
    /// buildings whose party walls it carries (the cutaway cuts those too). Storey's own.
    /// </summary>
    public class SteadyTests
    {
        [Fact]
        public void ABuildingIsSteadyOnlyWhileNothingCanHideItsPixels()
        {
            Assert.True(Steady.Of(5, 1, 0, -1, 0, -1));
            Assert.False(Steady.Of(5, 0.4f, 0, -1, 0, -1));                       // cross-fading between LODs
            Assert.False(Steady.Of(5, 1, 0, -1, 0.5f, 9));                        // another building isolated
            Assert.True(Steady.Of(9, 1, 0, -1, 0.5f, 9));                         // the isolated one itself
            Assert.False(Steady.Of(5, 1, 3, -1, 0, -1));                          // in the way (occluder slot 2)
            Assert.False(Steady.Of(5, 1, 3, -1, 0, -1, null, far: true, farOcclusion: true));
            Assert.True(Steady.Of(5, 1, 3, -1, 0, -1, null, far: true, farOcclusion: false));   // far occlusion off
            Assert.False(Steady.Of(5, 1, 0, 5, 0, -1));                           // the player's building
            Assert.False(Steady.Of(5, 1, 0, 7, 0, -1, new[] { 2, 7 }));           // a party wall with the player's
            Assert.True(Steady.Of(5, 1, 0, 7, 0, -1, new[] { 2, 3 }));            // party walls with others only
        }

        /// <summary>
        /// The building table marks a row for its site to look at again only when its fade or occluder slot changed in a way
        /// the answer goes by (docs/PERFORMANCE.md §3.3): every row change it leaves unmarked gives every renderer, near or
        /// far, of any building, under any view, the answer it had.
        /// </summary>
        [Fact]
        public void ARowChangeLeftUnmarkedChangesNoAnswer()
        {
            float[] fades = { 0, 0.25f, 0.999f, 1, 1.5f, float.NaN }, slots = { 0, 0.4f, 0.5f, 0.6f, 1, 3, 16 };
            (int active, float iso, int isolated)[] views = { (-1, 0, -1), (5, 0, -1), (7, 0, -1), (-1, 0.5f, 9), (-1, 0.0005f, 9), (-1, 1, -1), (7, 1, 5) };
            var mates = new[] { null, new[] { 2, 7 } };
            int same = 0, marked = 0;
            foreach (var f0 in fades) foreach (var o0 in slots) foreach (var f1 in fades) foreach (var o1 in slots)
            {
                if (!Steady.Same(f0, o0, f1, o1)) { marked++; continue; }
                same++;
                foreach (var v in views) foreach (var m in mates) foreach (var far in new[] { false, true }) foreach (var farOcc in new[] { false, true })
                    foreach (int idx in new[] { 5, 9 })
                        Assert.Equal(Steady.Of(idx, f0, o0, v.active, v.iso, v.isolated, m, far, farOcc), Steady.Of(idx, f1, o1, v.active, v.iso, v.isolated, m, far, farOcc));
            }
            Assert.True(same > 0 && marked > 0, $"{same} unmarked, {marked} marked");
            // a fade under way to its end, or a slot moved to another, is not a change; starting or ending either is
            Assert.True(Steady.Same(0.2f, 0, 0.6f, 0)); Assert.True(Steady.Same(1, 2, 1, 5));
            Assert.False(Steady.Same(0.99f, 0, 1, 0)); Assert.False(Steady.Same(1, 0, 1, 1));
        }

        [Fact]
        public void IsolatingIsWhatTheAnswerGoesBy()
        {
            foreach (var iso in new[] { 0f, 0.0005f, 0.001f, 0.0011f, 0.5f, 1f })
                Assert.Equal(Steady.Isolating(iso), !Steady.Of(5, 1, 0, -1, iso, 9));
        }

        /// <summary>Marked indices are taken a site's block at a time, each once, and the other blocks' stay marked.</summary>
        [Fact]
        public void MarksAreTakenABlockAtATime()
        {
            var m = new Lod.Marks(100); var got = new System.Collections.Generic.List<int>();
            foreach (int i in new[] { 5, 42, 7, 5, 99, 100, -1, 41, 42 }) m.Mark(i);
            Assert.Equal(5, m.Count);   // 5 and 42 once; 100 and −1 are out of range
            m.Take(0, 10, got); Assert.Equal(new[] { 5, 7 }, got); Assert.Equal(3, m.Count);
            got.Clear(); m.Take(0, 10, got); Assert.Empty(got);
            m.Mark(5); got.Clear(); m.Take(5, 1, got); Assert.Equal(new[] { 5 }, got);   // taken, it can be marked again
            got.Clear(); m.Take(40, 60, got); Assert.Equal(new[] { 42, 99, 41 }, got); Assert.Equal(0, m.Count);
            got.Clear(); m.Take(0, 0, got); Assert.Empty(got);
        }

        [Fact]
        public void APackedFullDetailMeshNamesTheBuildingsWhosePartyWallsItCarries()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; Edit.TestCity.Generate(d, 300);
            var site = new Site(d.buildings); int withWalls = 0;
            foreach (var b in site.Buildings)
            {
                var r = Lod0.Build(site, b);
                var expect = r.Op.K.Where(k => k >= 8).Select(k => (k >> 3) - 1).Where(m => m < site.Buildings.Count).Select(m => 1000 + m).Distinct().OrderBy(m => m).ToArray();
                var parts = MeshPack.Pack(r.Op, true, (1000, site.Buildings.Count), weld: false);
                if (parts.Count == 0) continue;
                Assert.All(parts, p => Assert.Equal(expect, p.Mates.OrderBy(m => m).ToArray()));
                if (expect.Length > 0) withWalls++;
                Assert.Empty(MeshPack.Pack(Lod1.Build(site, b), true).SelectMany(p => p.Mates));   // LOD1 carries no cutaway kinds
            }
            Assert.True(withWalls > 0, "no building with party walls in the sample");
        }

        static string Shaders => Path.Combine(typeof(SteadyTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "UnityRoot").Value!, "Packages", "com.triband.storey", "Unity", "Shaders");

        [Fact]
        public void EveryDiscardIsOutsideTheSteadyVariant()
        {
            // StoreyOcclude is the only function that discards, and its steady branch returns before any of it
            foreach (var f in Directory.GetFiles(Shaders, "*.hlsl").Where(f => Path.GetFileName(f) != "StoreyOcclusion.hlsl" && Path.GetFileName(f) != "StoreyProp.hlsl"))
                Assert.DoesNotMatch(new Regex(@"\b(clip|discard)\s*\("), Regex.Replace(File.ReadAllText(f), @"//[^\n]*", ""));
            // comments blanked, so a clip a comment names isn't counted
            var occ = Regex.Replace(File.ReadAllText(Path.Combine(Shaders, "StoreyOcclusion.hlsl")), @"//[^\n]*", m => new string(' ', m.Length));
            int start = occ.IndexOf("float StoreyOcclude(", StringComparison.Ordinal), steady = occ.IndexOf("#ifdef STOREY_STEADY", start, StringComparison.Ordinal);
            int other = occ.IndexOf("#else", steady, StringComparison.Ordinal), end = occ.IndexOf("\n}", start, StringComparison.Ordinal);
            Assert.True(start > 0 && steady > start && other > steady && end > other, "StoreyOcclude opens with its steady branch");
            Assert.DoesNotMatch(new Regex(@"\bclip\s*\("), occ.Substring(steady, other - steady));
            var clips = Regex.Matches(occ, @"\bclip\s*\(").Select(m => m.Index).ToList();
            Assert.NotEmpty(clips);
            Assert.All(clips, i => Assert.InRange(i, other, end));
            Assert.Contains("#if !defined(STOREY_DEPTH) && !defined(STOREY_STEADY)", occ);   // no Sink in the steady variant
            // every pass of the opaque and massing shaders has the variant; the glass doesn't need it (it blends)
            foreach (var name in new[] { "StoreyOpaque.shader", "StoreyMassing.shader" })
            {
                var text = File.ReadAllText(Path.Combine(Shaders, name));
                int passes = Regex.Matches(text, @"\bPass\s*\{").Count, variants = Regex.Matches(text, @"#pragma multi_compile_local _ STOREY_STEADY").Count;
                Assert.True(passes == 3 && variants == passes, $"{name}: {variants} steady variants for {passes} passes");
            }
        }

        [Fact]
        public void SteadyDrawsComeBeforeDiscardingOnes()
        {
            // docs/PERFORMANCE.md §7.6: the shaders (the package's and the Color Palette Lit sample's) queue at AlphaTest for
            // their discarding variant, and the steady copies draw at Geometry, so no opaque draw follows a discarding one
            var package = Path.GetDirectoryName(Shaders)!;
            foreach (var dir in new[] { Shaders, Path.Combine(Path.GetDirectoryName(package)!, "Samples~", "ColorPaletteLit") })
                foreach (var name in new[] { "StoreyOpaque.shader", "StoreyMassing.shader" })
                    Assert.Matches(new Regex("Tags\\s*\\{[^}]*\"Queue\"\\s*=\\s*\"AlphaTest\""), File.ReadAllText(Path.Combine(dir, name)));
            var renderer = File.ReadAllText(Path.Combine(package, "Rendering", "SiteRenderer.cs"));
            Assert.Equal(3, Regex.Matches(renderer, @"renderQueue = (\(int\)UnityEngine\.Rendering\.RenderQueue\.Geometry|geometry)").Count);
        }

        [Fact]
        public void OnlyTheSteadyCopiesCullBackFacesAndOnlyWhenAsked()
        {
            // docs/PERFORMANCE.md §7.1: Opaque and Massing (the package's and the Color Palette Lit sample's) cull by a property
            // that is Off on the materials, whose section cap draws a cut wall's inside (back faces); the renderer sets Back on
            // the steady copies alone, when the quality asset says so (an experiment until the shells are closed). The glass,
            // which has no cap, culls its back faces always
            var package = Path.GetDirectoryName(Shaders)!;
            var sample = Path.Combine(Path.GetDirectoryName(package)!, "Samples~", "ColorPaletteLit");
            foreach (var dir in new[] { Shaders, sample })
            {
                foreach (var name in new[] { "StoreyOpaque.shader", "StoreyMassing.shader" })
                {
                    var text = Regex.Replace(File.ReadAllText(Path.Combine(dir, name)), @"//[^\n]*", "");
                    Assert.Equal(new[] { "[_StoreyCull]" }, Regex.Matches(text, @"\bCull\s+(\S+)").Select(m => m.Groups[1].Value));
                    Assert.Matches(new Regex(@"_StoreyCull\s*\(""Cull"",\s*Float\)\s*=\s*0\s"), text);   // CullMode.Off
                }
                Assert.Matches(new Regex(@"\bCull\s+Back\b"), File.ReadAllText(Path.Combine(dir, "StoreyGlass.shader")));
            }
            var renderer = File.ReadAllText(Path.Combine(package, "Rendering", "SiteRenderer.cs"));
            var sets = Regex.Matches(renderer, @"(\w+)\??\.SetFloat\(StoreyShaderIds\.Cull").Select(m => m.Groups[1].Value).ToArray();
            Assert.Equal(new[] { "opaqueSteady", "massingSteady" }, sets);
            Assert.Contains("CullSteadyBackFaces ? UnityEngine.Rendering.CullMode.Back : UnityEngine.Rendering.CullMode.Off", renderer);
            Assert.Contains("site.CullSteadyBackFaces = quality == null || quality.cullBackFacesWhenSteady;", File.ReadAllText(Path.Combine(package, "Rendering", "StoreySite.cs")));
            Assert.Contains("Cull = Shader.PropertyToID(\"_StoreyCull\")", File.ReadAllText(Path.Combine(package, "Rendering", "StoreyShaderIds.cs")));
        }
    }
}
