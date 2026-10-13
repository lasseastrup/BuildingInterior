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
            Assert.Equal(d.MaxLod0, int.Parse(p["lod0Residency"])); Assert.Equal(d.MaxLod1, int.Parse(p["lod1Residency"]));
            Assert.Equal(d.MaxColliders, int.Parse(p["collidersKept"])); Assert.Equal(d.BudgetMs, D(p["buildBudgetMs"]));
            Assert.Equal(d.GlassClear, D(p["windowsClearAt"]));
            Assert.Equal(d.Threads, p["buildOnWorkerThreads"] == "1" && p["buildThreads"] == "0" ? -1 : 0);   // automatic
            Assert.Equal(d.KeepChunkBytes, long.Parse(p["meshChunksKeptMB"]) << 20);
            Assert.Equal(d.FarOcclusion, p["occlusionOnFarMaterials"] == "1");
            // the minimum tier: half the residency, 12 MB of chunks, no occlusion on far materials, and nothing else
            Assert.Equal(new[] { "lod0Residency", "lod1Residency", "meshChunksKeptMB", "occlusionOnFarMaterials" }, p.Keys.Where(k => p[k] != m[k]));
            Assert.Equal(("8", "130", "12", "0"), (m["lod0Residency"], m["lod1Residency"], m["meshChunksKeptMB"], m["occlusionOnFarMaterials"]));
        }
    }
}
