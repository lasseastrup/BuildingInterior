using System.IO;
using System.Reflection;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>The corpus emitted by prototype/tools/export-fixtures.mjs, copied beside the test assembly.</summary>
    internal static class Fixtures
    {
        public static string Dir => Path.Combine(Path.GetDirectoryName(typeof(Fixtures).Assembly.Location)!, "Fixtures");

        public static string Text(string name)
        {
            var path = Path.Combine(Dir, name);
            Assert.True(File.Exists(path), $"{path} is missing; run `npm run fixtures` in prototype/tools");
            return File.ReadAllText(path);
        }
    }
}
