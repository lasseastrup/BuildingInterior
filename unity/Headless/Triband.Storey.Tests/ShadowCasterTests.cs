using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Where Storey's shadow caster passes put a vertex: as URP's own caster does (ShadowCasterPass.hlsl). Unity fits a
    /// directional light's cascade near plane to what the cascade shows and pancakes the casters in front of it onto it
    /// (ApplyShadowClamping); without the clamp they are clipped, and a shell building at LOD0 cast only the window frames
    /// of its shaded side, its sunlit facade and roof cut away. The shaders only compile in Unity (and in
    /// unity/tools/shaders/check.py), so this reads their source.
    /// </summary>
    public class ShadowCasterTests
    {
        static string Text(params string[] path) => Regex.Replace(File.ReadAllText(UnityAsset.Package("com.triband.storey", path)), @"//[^\n]*", "");

        [Fact]
        public void TheShadowPassPancakesAndTakesAPunctualLightsDirectionAsUrpDoes()
        {
            var lit = Text("Unity", "Shaders", "StoreyLit.hlsl");
            var shadow = Regex.Match(lit, @"#ifdef STOREY_SHADOW\s+#ifdef _CASTING_PUNCTUAL_LIGHT_SHADOW(.*?)#else\s+OUT\.positionCS = TransformWorldToHClip\(worldPos\);", RegexOptions.Singleline);
            Assert.True(shadow.Success, "the shadow pass's vertex position isn't where it was");
            Assert.Contains("float3 lightDir = normalize(_LightPosition - worldPos);", shadow.Groups[1].Value);
            Assert.Contains("float3 lightDir = _LightDirection;", shadow.Groups[1].Value);
            Assert.Contains("OUT.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(worldPos, normalWS, lightDir)));", shadow.Groups[1].Value);
            Assert.Matches(new Regex(@"#ifdef STOREY_SHADOW\s+float3 _LightDirection;\s+float3 _LightPosition;"), lit);
            // every shadow caster pass of the buildings' shaders, the sample's too, is Storey's and compiles the punctual variant
            var package = UnityAsset.Package("com.triband.storey");
            int passes = 0;
            foreach (var f in Directory.GetFiles(Path.Combine(package, "Unity", "Shaders"), "*.shader").Concat(Directory.GetFiles(Path.Combine(package, "Samples~", "ColorPaletteLit"), "*.shader")))
            {
                var text = Regex.Replace(File.ReadAllText(f), @"//[^\n]*", "");
                foreach (var pass in Regex.Split(text, @"\bPass\s*\{").Skip(1).Where(p => p.Contains("\"LightMode\" = \"ShadowCaster\"")))
                {
                    passes++;
                    Assert.Contains("#define STOREY_SHADOW", pass);
                    Assert.Matches(new Regex(@"#pragma\s+multi_compile_vertex\s+_\s+_CASTING_PUNCTUAL_LIGHT_SHADOW"), pass);
                }
            }
            Assert.Equal(4, passes);   // Opaque and Massing, Storey's and the sample's; the glass casts none
        }
    }
}
