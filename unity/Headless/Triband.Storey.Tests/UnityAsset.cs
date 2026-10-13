using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>The assets the packages ship, read as Unity reads their YAML: a top-level field of the one object in the file.</summary>
    static class UnityAsset
    {
        public static string UnityRoot =>
            typeof(UnityAsset).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "UnityRoot").Value!;

        /// <summary>A package folder (or file in it), by its path below the package.</summary>
        public static string Package(string package, params string[] path) => Path.Combine(new[] { UnityRoot, "Packages", package }.Concat(path).ToArray());

        /// <summary>The GUID in a file's .meta.</summary>
        public static string Guid(string file) => File.ReadAllLines(file + ".meta").First(l => l.StartsWith("guid: ")).Substring(6).Trim();

        /// <summary>A top-level field of a Unity YAML asset, its single-quoted value unquoted (folded lines joined as YAML joins them).</summary>
        public static string Field(string[] lines, string name)
        {
            string head = "  " + name + ": ";
            int i = System.Array.FindIndex(lines, l => l.StartsWith(head));
            Assert.True(i >= 0, $"no {name}");
            string v = lines[i].Substring(head.Length);
            if (!v.StartsWith("'")) return v.Trim();
            var sb = new StringBuilder(v.Substring(1));
            // a single-quoted scalar ends at a quote that isn't doubled
            bool Closed(string s) { int q = 0; for (int j = s.Length - 1; j >= 0 && s[j] == '\''; j--) q++; return q % 2 == 1; }
            while (!Closed(sb.ToString())) { var next = lines[++i].Trim(); sb.Append(next.Length == 0 ? "\n" : " " + next); }
            return sb.ToString(0, sb.Length - 1).Replace("''", "'");
        }

        /// <summary>The asset's script: its m_Script field holds this GUID.</summary>
        public static void IsA(string[] lines, string scriptFile) => Assert.Contains($"guid: {Guid(scriptFile)}, type: 3", Field(lines, "m_Script"));
    }
}
