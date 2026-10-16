using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The shaders' reads of the building table stay inside its buffers, those whose value then goes unused too. HLSL
    /// evaluates both sides of ?: and of && and || (FXC, which compiles Unity's Metal shaders, never short-circuits), so a
    /// read a condition seems to guard runs whatever the condition. D3D and Vulkan read an element outside a buffer as 0;
    /// Metal leaves it undefined, and on an iPhone the GPU can fault, after which iOS stops running the app's GPU work: a
    /// city that loads with artifacts and then freezes on a phone while the editor shows it fine. The shaders only compile
    /// in Unity, so this reads their source.
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

        [Fact]
        public void AReadBehindAConditionKeepsItsIndexInItsTable()
        {
            int guarded = 0;
            foreach (var f in Directory.GetFiles(Path.Combine(Package, "Unity", "Shaders"), "*.hlsl"))
            {
                // comments blanked, so a read a comment names isn't counted and positions hold
                var src = Regex.Replace(File.ReadAllText(f), @"//[^\n]*", m => new string(' ', m.Length));
                foreach (Match m in Regex.Matches(src, @"\b_Storey\w+\s*\["))
                {
                    // a read after a ?, && or || in its statement runs whether or not that condition holds
                    int start = src.LastIndexOfAny(new[] { ';', '{', '}' }, m.Index) + 1;
                    if (!Regex.IsMatch(src.Substring(start, m.Index - start), @"\?|&&|\|\|")) continue;
                    guarded++;
                    var index = Bracketed(src, m.Index + m.Length - 1);
                    Assert.True(Regex.IsMatch(index, @"\b(max|min|clamp)\s*\("),
                        $"{Path.GetFileName(f)}: {m.Value}{index}] is read whether or not its condition holds, and nothing keeps its index in the table");
                }
            }
            Assert.True(guarded > 0, "no read behind a condition found: has the shaders' code moved?");
        }

        [Fact]
        public void TheDetailColoursHaveAnEntryForEverySlotAVertexCanName()
        {
            // StoreyPalette.hlsl reads _StoreyDetailColors[slot - 24] for every colour, a slot of 24 and up included: a
            // vertex's slot is a byte, and the table must reach to 255
            var table = File.ReadAllText(Path.Combine(Package, "Unity", "Rendering", "BuildingTable.cs"));
            Assert.Matches(new Regex(@"DetailColors\s*=\s*new uint\[\s*256\s*-\s*Generate\.ColorRows\.Entries\s*\]"), table);
            Assert.Equal(24, ColorRows.Entries);   // StoreyPalette.hlsl's STOREY_ROW_SLOTS
            var palette = File.ReadAllText(Path.Combine(Package, "Unity", "Shaders", "StoreyPalette.hlsl"));
            Assert.Contains("#define STOREY_ROW_SLOTS 24", palette);
        }
    }
}
