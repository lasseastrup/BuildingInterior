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
            Assert.Contains("site.CullSteadyBackFaces = quality != null && quality.cullBackFacesWhenSteady;", File.ReadAllText(Path.Combine(package, "Rendering", "StoreySite.cs")));
            Assert.Contains("Cull = Shader.PropertyToID(\"_StoreyCull\")", File.ReadAllText(Path.Combine(package, "Rendering", "StoreyShaderIds.cs")));
        }
    }
}
