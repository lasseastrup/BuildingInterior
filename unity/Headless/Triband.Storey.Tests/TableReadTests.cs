using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The shaders' reads of the building table stay inside its buffers, those whose value then goes unused too. D3D and
    /// Vulkan read an element outside a buffer as 0; Metal leaves it undefined, and on an iPhone the GPU can fault, after
    /// which iOS stops running the app's GPU work: a city that loads with artifacts and then freezes on a phone, or freezes
    /// later on an upper storey, while the editor shows it fine. A condition guards no read: HLSL evaluates both sides of ?:
    /// and of && and || (FXC, which compiles Unity's Metal shaders, never short-circuits), and an if the compiler flattens
    /// reads whatever its condition (it reads, then picks). The shaders only compile in Unity, so this reads their source.
    /// </summary>
    public class TableReadTests
    {
        static string Root => typeof(TableReadTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "UnityRoot").Value!;
        static string Package => Path.Combine(Root, "Packages", "com.triband.storey");

        /// <summary>What lies between the bracket at <paramref name="open"/> and the one that closes it.</summary>
        static string Bracketed(string s, int open)
        {
            for (int i = open + 1, depth = 1; i < s.Length; i++)
            {
                if (s[i] == '[') depth++;
                else if (s[i] == ']' && --depth == 0) return s.Substring(open + 1, i - open - 1);
            }
            return s.Substring(open + 1);
        }

        // what keeps an index in its table: StoreyOcclusion.hlsl's helpers, or a min, max or clamp of its own
        const string Kept = @"\b(StoreyRow|StoreySlotRow|StoreyWallId|max|min|clamp)\s*\(";

        static string Code(string file) => Regex.Replace(File.ReadAllText(file), @"//[^\n]*", m => new string(' ', m.Length));   // comments blanked, positions held

        /// <summary>
        /// Every read, whatever its statement: no condition guards it (see above), and an index from a mesh's vertices or a
        /// state row is only as good as the data. So each index goes through a clamp, or starts from a variable that one made.
        /// </summary>
        [Fact]
        public void EveryReadKeepsItsIndexInItsTable()
        {
            int reads = 0;
            foreach (var f in Directory.GetFiles(Path.Combine(Package, "Unity", "Shaders"), "*.hlsl"))
            {
                var src = Code(f);
                foreach (Match m in Regex.Matches(src, @"\b_Storey\w+\s*\["))
                {
                    reads++;
                    var index = Bracketed(src, m.Index + m.Length - 1);
                    if (Regex.IsMatch(index, Kept)) continue;
                    // row + 1 + i: a row kept in the table by its own assignment (row = StoreySlotRow(slot), row = min(row, …))
                    var first = Regex.Match(index, @"^\s*([A-Za-z_]\w*)");
                    bool fromKept = first.Success && Regex.IsMatch(src, @"\b" + first.Groups[1].Value + @"\s*=\s*" + Kept);
                    Assert.True(fromKept, $"{Path.GetFileName(f)}: {m.Value}{index}] reads an index nothing keeps in the table");
                }
            }
            Assert.True(reads >= 20, $"only {reads} table reads found: has the shaders' code moved?");
        }

        [Fact]
        public void TheShadersKnowTheTablesSizes()
        {
            // the fixed tables' sizes, as the shaders clamp to them, are the C# side's
            var table = File.ReadAllText(Path.Combine(Package, "Unity", "Rendering", "BuildingTable.cs"));
            var occlusion = Code(Path.Combine(Package, "Unity", "Shaders", "StoreyOcclusion.hlsl"));
            var palette = Code(Path.Combine(Package, "Unity", "Shaders", "StoreyPalette.hlsl"));
            var facade = Code(Path.Combine(Package, "Unity", "Shaders", "StoreyFacade.hlsl"));
            int Cs(string pattern) => int.Parse(Regex.Match(table, pattern).Groups[1].Value);
            int Define(string src, string name) => int.Parse(Regex.Match(src, @"#define\s+" + name + @"\s+(\d+)").Groups[1].Value);
            Assert.Equal(Cs(@"MaxBuildings\s*=\s*(\d+)"), Define(occlusion, "STOREY_ROWS"));
            Assert.Equal(Cs(@"OccSlots\s*=\s*(\d+)"), Define(occlusion, "STOREY_OCC_SLOTS"));
            Assert.Equal(Cs(@"OccWidth\s*=\s*(\d+)"), Define(occlusion, "STOREY_OCC_W"));
            Assert.Equal(Cs(@"ParamTexels\s*=\s*(\d+)"), Define(facade, "STOREY_PARAM_TEXELS"));
            Assert.Equal(256 - ColorRows.Entries, Define(palette, "STOREY_DETAIL_COLORS"));
            // the growing tables' sizes go up with their buffers, in the order the shaders read them (x wall ids, y
            // parameter rows, z colour rows)
            Assert.Matches(new Regex(@"SetGlobalVector\(StoreyShaderIds\.TableSize,\s*new Vector4\(Wall\.Length,\s*Params\.Length\s*/\s*ParamTexels,\s*Colors\.Length\s*/\s*Generate\.ColorRows\.Uint4s"), table);
            // a colour row is four of the buffer's elements, uint4s, as the shaders read it (_StoreyColors[row * STOREY_ROW_UINT4S + …])
            Assert.Matches(new Regex(@"public struct ColorWords \{ public uint x, y, z, w; \}"), table);
            Assert.Equal(ColorRows.Uint4s, Define(palette, "STOREY_ROW_UINT4S"));
            Assert.Equal(ColorRows.RemapEntry, Define(palette, "STOREY_REMAP_ENTRY"));
            foreach (var src in new[] { occlusion, palette, facade }) Assert.Contains("float4 _StoreyTableSize;", src);
        }

        [Fact]
        public void TheDetailColoursHaveAnEntryForEverySlotAVertexCanName()
        {
            // StoreyPalette.hlsl reads _StoreyDetailColors[slot - 32] for every colour, a slot of 32 and up included: a
            // vertex's slot is a byte, and the table must reach to 255
            var table = File.ReadAllText(Path.Combine(Package, "Unity", "Rendering", "BuildingTable.cs"));
            Assert.Matches(new Regex(@"DetailColors\s*=\s*new uint\[\s*256\s*-\s*Generate\.ColorRows\.Entries\s*\]"), table);
            Assert.Equal(32, ColorRows.Entries);   // StoreyPalette.hlsl's STOREY_ROW_SLOTS
            var palette = File.ReadAllText(Path.Combine(Package, "Unity", "Shaders", "StoreyPalette.hlsl"));
            Assert.Contains("#define STOREY_ROW_SLOTS 32", palette);
        }
    }
}
