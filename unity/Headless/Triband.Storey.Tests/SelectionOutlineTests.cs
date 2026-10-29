using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The Scene view's selection outline while a building is edited: on that building alone. The site is selected, and Unity
    /// outlines a selected object's children, every building's renderers; the GPU Resident Drawer does so from the selection
    /// itself, past a renderer's selected state. So every Storey shader has a selection pass, which Unity draws the outline
    /// with, and it keeps the building the editor names (StoreySelectionOutline). The shaders only compile in Unity (and in
    /// unity/tools/shaders/check.py), so this reads their source.
    /// </summary>
    public class SelectionOutlineTests
    {
        static string Code(string file) => Regex.Replace(File.ReadAllText(file), @"//[^\n]*", "");
        static string Text(string package, params string[] path) => Code(UnityAsset.Package(package, path));

        [Fact]
        public void EveryShaderOutlinesThroughItsSelectionPass()
        {
            var package = UnityAsset.Package("com.triband.storey");
            var shaders = Directory.GetFiles(Path.Combine(package, "Unity", "Shaders"), "*.shader").Concat(Directory.GetFiles(Path.Combine(package, "Samples~", "ColorPaletteLit"), "*.shader")).ToList();
            Assert.Equal(6, shaders.Count);   // Opaque, Glass and Massing, Storey's and the sample's
            foreach (var f in shaders)
            {
                var pass = Assert.Single(Regex.Split(Code(f), @"\bPass\s*\{").Skip(1), p => p.Contains("\"LightMode\" = \"SceneSelectionPass\""));
                Assert.Contains("Name \"SceneSelectionPass\"", pass);
                // both faces, depth written, no blending (the glass's subshader blends), as URP's own selection passes
                foreach (var state in new[] { @"\bCull\s+Off\b", @"\bZWrite\s+On\b", @"\bBlend\s+Off\b" }) Assert.Matches(new Regex(state), pass);
                Assert.Contains("#pragma vertex StoreyVert", pass);
                Assert.Contains("#pragma fragment StoreySelectionFrag", pass);
                Assert.Contains("#pragma multi_compile _ DOTS_INSTANCING_ON", pass);   // the GPU Resident Drawer's outline too
                Assert.Contains("#pragma editor_sync_compilation", pass);              // which has none while a variant compiles
                Assert.Contains("#define STOREY_DEPTH", pass);
                Assert.Contains("#define STOREY_SELECTION", pass);
                Assert.DoesNotContain("STOREY_STEADY", pass);   // the steady variant has no building index to keep one by
                Assert.Matches(new Regex(@"#include ""(Packages/com\.triband\.storey/Unity/Shaders/)?StoreyLit\.hlsl"""), pass);
            }
        }

        [Fact]
        public void ThePassKeepsTheBuildingTheEditorNames()
        {
            var lit = Text("com.triband.storey", "Unity", "Shaders", "StoreyLit.hlsl");
            // Unity sets the ids as integers (Shader Graph's pass declares them so): a float reads 0, and the outline isn't drawn
            Assert.Matches(new Regex(@"#ifdef STOREY_SELECTION\s+float _StoreyOutline;\s+int _ObjectId;\s+int _PassValue;\s+#endif"), lit);
            // after the building's own test (LOD, hidden), before the vertex is placed
            Assert.Matches(new Regex(@"bool shown = StoreyVertex\([^;]+;\s+#ifdef STOREY_SELECTION\s+if \(_StoreyOutline > 0\.5 && abs\(v\.bid \+ 1\.0 - _StoreyOutline\) > 0\.5\) shown = false;\s+#endif"), lit);
            Assert.Matches(new Regex(@"half4 StoreySelectionFrag\(Varyings IN\) : SV_Target\s+\{\s+UNITY_SETUP_INSTANCE_ID\(IN\);\s+StoreyOcclude\(StoreyUnpack\(IN\), IN\.positionCS\.xy, IN\.positionCS\.z\);\s+return half4\(_ObjectId, _PassValue, 1\.0, 1\.0\);\s+\}"), lit);
            // inside the depth passes' part, whose varyings carry the building index
            int depth = lit.IndexOf("#ifdef STOREY_DEPTH\n\nfloat4 StoreyDepthFrag", StringComparison.Ordinal), frag = lit.IndexOf("half4 StoreySelectionFrag", StringComparison.Ordinal);
            Assert.True(depth > 0 && frag > depth && frag < lit.IndexOf("struct StoreySurface", StringComparison.Ordinal), "StoreySelectionFrag is in the depth passes' part");
            Assert.Contains("public static readonly int Outline = Shader.PropertyToID(\"_StoreyOutline\");", Text("com.triband.storey", "Unity", "Rendering", "StoreyShaderIds.cs"));
        }

        /// <summary>The pass's test, as StoreyLit.hlsl has it: is building <paramref name="bid"/> outlined at <paramref name="outline"/>?</summary>
        static bool Outlined(double bid, double outline) => !(outline > 0.5 && Math.Abs(bid + 1.0 - outline) > 0.5);

        [Fact]
        public void TheEditorNamesTheEditedBuildingInTheStoreyContextAlone()
        {
            var editor = Text("com.triband.storey.authoring", "Editor", "StoreySelectionOutline.cs");
            Assert.Contains("const float None = BuildingTable.MaxBuildings + 1;", editor);
            Assert.Contains("if (ToolManager.activeContextType != typeof(StoreyToolContext) || !(StoreyToolContext.SelectedSite() is StoreySite s)) return 0;", editor);
            Assert.Contains("return row >= 0 ? row + 1 : None;", editor);
            Assert.Contains("Shader.SetGlobalFloat(StoreyShaderIds.Outline, want);", editor);
            // the values it sets, as the pass reads them: 0 every building, a row's alone, and None no row of the table
            int rows = int.Parse(Regex.Match(Text("com.triband.storey", "Unity", "Rendering", "BuildingTable.cs"), @"public const int MaxBuildings = (\d+);").Groups[1].Value);
            Assert.Equal(rows.ToString(), Regex.Match(Text("com.triband.storey", "Unity", "Shaders", "StoreyOcclusion.hlsl"), @"#define STOREY_ROWS (\d+)").Groups[1].Value);
            Assert.True(Outlined(0, 0) && Outlined(rows - 1, 0));
            Assert.True(Outlined(7, 7 + 1) && !Outlined(6, 7 + 1) && !Outlined(8, 7 + 1) && Outlined(0, 0 + 1) && !Outlined(1, 0 + 1));
            Assert.DoesNotContain(Enumerable.Range(0, rows), b => Outlined(b, rows + 1));
        }
    }
}
