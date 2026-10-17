using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The quality tiers the package ships (Quality Tiers/, docs/CITY.md §7): the primary tier is what a site without a
    /// quality asset runs, and the minimum tier (iPhone 7) changes only what the docs say it does.
    /// </summary>
    public class QualityTierTests
    {
        static readonly string Script = UnityAsset.Package("com.triband.storey", "Unity", "StoreyQualitySettings.cs");

        /// <summary>Every field the settings serialize, in the order the class declares them.</summary>
        static List<string> Fields() =>
            Regex.Matches(File.ReadAllText(Script), @"public (?:float|int|bool) (\w+) = ").Select(m => m.Groups[1].Value).ToList();

        static Dictionary<string, string> Tier(string name)
        {
            var lines = File.ReadAllLines(UnityAsset.Package("com.triband.storey", "Quality Tiers", name + ".asset"));
            UnityAsset.IsA(lines, Script);
            Assert.Equal(name, UnityAsset.Field(lines, "m_Name"));
            return Fields().ToDictionary(f => f, f => UnityAsset.Field(lines, f));
        }

        static double D(string v) => double.Parse(v, CultureInfo.InvariantCulture);

        [Fact]
        public void ThePrimaryTierIsTheDefaultsAndTheMinimumTierHalvesTheResidency()
        {
            var p = Tier("Storey Primary Tier"); var m = Tier("Storey Minimum Tier");
            // the primary tier: the automatic LOD's defaults, which a site without a quality asset has
            var d = new LodSettings();
            Assert.Equal(d.PX, new[] { D(p["lod0PixelsPerMetre"]), D(p["lod1PixelsPerMetre"]), D(p["lod2PixelsPerMetre"]) });
            Assert.Equal(d.Hysteresis, D(p["hysteresis"])); Assert.Equal(d.Fade, D(p["fadeSeconds"])); Assert.Equal(d.Far, D(p["farDistance"]));
            Assert.Equal(d.FadeDetail, p["crossFadeFullDetail"] == "1");
            Assert.Equal(d.MaxLod0, int.Parse(p["lod0Residency"])); Assert.Equal(d.MaxLod1, int.Parse(p["lod1Residency"]));
            Assert.Equal(d.MaxColliders, int.Parse(p["collidersKept"])); Assert.Equal(d.BudgetMs, D(p["buildBudgetMs"]));
            Assert.Equal(d.GlassClear, D(p["windowsClearAt"])); Assert.Equal(d.WindowsPainted, D(p["windowsPaintedFrom"]));
            Assert.Equal(d.Threads, p["buildOnWorkerThreads"] == "1" && p["buildThreads"] == "0" ? -1 : 0);   // automatic
            Assert.Equal(d.KeepChunkBytes, long.Parse(p["meshChunksKeptMB"]) << 20);
            Assert.Equal(d.FarOcclusion, p["occlusionOnFarMaterials"] == "1");
            Assert.Equal("1", p["specularAndReflections"]);   // URP's full PBR, as a site without a quality asset lights
            Assert.Equal("1", p["cullBackFacesWhenSteady"]);   // the shells are closed for it (docs/PERFORMANCE.md §7.1, BackFaceTests)
            // the minimum tier: full detail and shell switched at once, half the residency, the build budget of a 30 fps frame
            // (docs/PERFORMANCE.md §3.2), 12 MB of chunks, no occlusion on far materials, no specular or reflections, the
            // performance run's pass mark for 30 fps, and nothing else
            Assert.Equal(new[] { "crossFadeFullDetail", "lod0Residency", "lod1Residency", "buildBudgetMs", "meshChunksKeptMB", "occlusionOnFarMaterials", "specularAndReflections", "p99FrameMs" }, p.Keys.Where(k => p[k] != m[k]));
            Assert.Equal(("0", "8", "130", "4", "12", "0", "0"), (m["crossFadeFullDetail"], m["lod0Residency"], m["lod1Residency"], m["buildBudgetMs"], m["meshChunksKeptMB"], m["occlusionOnFarMaterials"], m["specularAndReflections"]));
            Assert.Equal(2, D(p["buildBudgetMs"]));   // a 60 fps frame's
            // Storey Performance Run's pass mark (docs/PERFORMANCE.md §14): at most a frame in a hundred late, at 60 fps and at 30
            Assert.Equal((20, 40), (D(p["p99FrameMs"]), D(m["p99FrameMs"])));
        }

        [Fact]
        public void SpecularAndReflectionsOffIsAGlobalKeywordThatTurnsURPsOwnOff()
        {
            // docs/PERFORMANCE.md §7.3: the site sets the keyword from its quality asset, the opaque and massing colour
            // passes declare it (the glass keeps its specular), and StoreyLit.hlsl turns it into URP's two keywords before
            // URP's lighting is included, where URP reads them
            string Text(params string[] path) => File.ReadAllText(UnityAsset.Package("com.triband.storey", path));
            var keyword = Regex.Match(Text("Unity", "Rendering", "StoreyGlobals.cs"), @"SimpleLightingKeyword = ""(\w+)""").Groups[1].Value;
            Assert.Equal("STOREY_SIMPLE_LIGHTING", keyword);
            Assert.Contains("StoreyGlobals.SetSimpleLighting(quality != null && !quality.specularAndReflections)", Text("Unity", "Rendering", "StoreySite.cs"));
            foreach (var name in new[] { "StoreyOpaque.shader", "StoreyMassing.shader", "StoreyGlass.shader" })
            {
                var passes = Regex.Split(Regex.Replace(Text("Unity", "Shaders", name), @"//[^\n]*", ""), @"\bPass\s*\{").Skip(1).ToList();
                var with = passes.Where(p => p.Contains($"#pragma multi_compile_fragment _ {keyword}")).ToList();
                if (name == "StoreyGlass.shader") { Assert.Empty(with); continue; }
                Assert.Single(with);
                Assert.Contains("\"LightMode\" = \"UniversalForward\"", with[0]);
            }
            var lit = Text("Unity", "Shaders", "StoreyLit.hlsl");
            int on = lit.IndexOf($"#ifdef {keyword}", System.StringComparison.Ordinal), urp = lit.IndexOf("ShaderLibrary/Lighting.hlsl\"", System.StringComparison.Ordinal);
            Assert.True(on > 0 && urp > on, "the keyword is read before URP's Lighting.hlsl is included");
            var block = lit.Substring(on, lit.IndexOf("#endif", on, System.StringComparison.Ordinal) - on);
            Assert.Contains("#define _SPECULARHIGHLIGHTS_OFF", block);
            Assert.Contains("#define _ENVIRONMENTREFLECTIONS_OFF", block);
        }
    }
}
