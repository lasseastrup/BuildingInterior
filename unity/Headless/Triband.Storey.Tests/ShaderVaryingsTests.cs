using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What Storey's colour passes hand from the vertex to the fragment stage (docs/PERFORMANCE.md §7.2, §7.3): the ambient
    /// probe evaluated where URP's SH Evaluation Mode says, per vertex on mobile, and the outputs a half holds in halves.
    /// The shaders only compile in Unity (and in unity/tools/shaders/check.py), so this reads their source.
    /// </summary>
    public class ShaderVaryingsTests
    {
        static string Text(params string[] path) => Regex.Replace(File.ReadAllText(UnityAsset.Package("com.triband.storey", path)), @"//[^\n]*", "");

        [Fact]
        public void TheAmbientProbeIsEvaluatedWhereUrpsShModeSays()
        {
            // each colour pass declares URP's keywords (URP's build strips the variants its pipeline asset's mode doesn't use)
            foreach (var name in new[] { "StoreyOpaque.shader", "StoreyMassing.shader", "StoreyGlass.shader" })
            {
                var passes = Regex.Split(Text("Unity", "Shaders", name), @"\bPass\s*\{").Skip(1).ToList();
                var forward = Assert.Single(passes, p => p.Contains("\"LightMode\" = \"UniversalForward\""));
                Assert.Matches(new Regex(@"#pragma\s+multi_compile\s+_\s+EVALUATE_SH_MIXED\s+EVALUATE_SH_VERTEX"), forward);
                Assert.All(passes.Where(p => p != forward), p => Assert.DoesNotContain("EVALUATE_SH", p));
            }
            // the vertex stage evaluates it (or its L2 part), the lighting takes it through URP's own split; no SampleSH per pixel
            var lit = Text("Unity", "Shaders", "StoreyLit.hlsl");
            Assert.Contains("OUT.vertexSH = SampleSHVertex(half3(normalWS));", lit);
            Assert.Contains("inputData.bakedGI = SampleSHPixel(half3(s.vertexSH), half3(s.normalWS));", lit);
            Assert.DoesNotContain("SampleSH(s.normalWS)", lit);
            Assert.Contains("s.vertexSH = IN.vertexSH;", Text("Unity", "Shaders", "StoreyFragment.hlsl"));
        }

        [Fact]
        public void TheBuildsStrippingKnowsEveryStoreyShader()
        {
            // StoreyShaderStripping (the authoring package) acts on Storey's shaders by name, and leaves out the minimum tier's
            // lighting keyword alone, the one the site sets (docs/PERFORMANCE.md §7.9)
            var strip = File.ReadAllText(UnityAsset.Package("com.triband.storey.authoring", "Editor", "StoreyShaderStripping.cs"));
            Assert.Contains("new ShaderKeyword(StoreyGlobals.SimpleLightingKeyword)", strip);
            Assert.Single(Regex.Matches(strip, @"data\.RemoveAt\("));
            var prefixes = Regex.Matches(strip, @"shader\.name\.StartsWith\(""([^""]+)""").Select(m => m.Groups[1].Value).ToArray();
            Assert.Equal(new[] { "Storey/", "Flamingo/Storey/" }, prefixes);
            var package = UnityAsset.Package("com.triband.storey");
            foreach (var f in Directory.GetFiles(Path.Combine(package, "Unity", "Shaders"), "*.shader").Concat(Directory.GetFiles(Path.Combine(package, "Samples~", "ColorPaletteLit"), "*.shader")))
            {
                var name = Regex.Match(File.ReadAllText(f), @"Shader\s+""([^""]+)""").Groups[1].Value;
                Assert.True(prefixes.Any(p => name.StartsWith(p, System.StringComparison.Ordinal)), $"{Path.GetFileName(f)}: \"{name}\" isn't one the build's stripping knows");
            }
        }

        [Fact]
        public void ColourPassOutputsAHalfHoldsAreHalves()
        {
            // a direction, a colour, a place on a pane and a window's number (up to 255) in halves; positions, the facade's
            // metres and building indices (up to 8,192, past a half's 2,048 exact integers) in floats
            var lit = Text("Unity", "Shaders", "StoreyLit.hlsl");
            var varyings = lit.Substring(lit.IndexOf("struct Varyings", System.StringComparison.Ordinal));
            varyings = varyings.Substring(0, varyings.IndexOf("};", System.StringComparison.Ordinal));
            foreach (var half in new[] { @"half3\s+normalWS\s*:", @"half4\s+color\s*:", @"half4\s+win\s*:", @"half3\s+vertexSH\s*:" })
                Assert.Matches(new Regex(half), varyings);
            foreach (var full in new[] { @"float3\s+worldPos\s*:", @"float4\s+fac\s*:", @"float4\s+fac2\s*:", @"float4\s+misc\s*:", @"float2\s+misc\s*:", @"float4\s+positionCS\s*:" })
                Assert.Matches(new Regex(full), varyings);
            // the pane's number is the vertex colour's slot byte (0 to 255), which a half holds exactly
            Assert.Contains("(float)ce.z", lit);
        }
    }
}
