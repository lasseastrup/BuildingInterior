using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The facade details the package ships (Facade Details/, docs/EDITOR.md §6.12): the generator's own five, as Facade
    /// Detail assets of the kind an artist makes, each saying what the generator's says.
    /// </summary>
    public class FacadeDetailAssetTests
    {
        [Fact]
        public void TheGeneratorsFiveShipAsFacadeDetailAssets()
        {
            var ids = new List<string>();
            foreach (var path in Directory.GetFiles(UnityAsset.Package("com.triband.storey", "Facade Details"), "*.asset"))
            {
                var lines = File.ReadAllLines(path);
                UnityAsset.IsA(lines, UnityAsset.Package("com.triband.storey", "Unity", "Rendering", "StoreyDetail.cs"));   // as Create ▸ Storey ▸ Facade Detail makes one
                string id = UnityAsset.Field(lines, "id"); ids.Add(id);
                var k = DetailKinds.Get(id);
                Assert.True(DetailKinds.IsBuiltIn(id) && k != null, id);
                Assert.Equal(Path.GetFileNameWithoutExtension(path), UnityAsset.Field(lines, "m_Name"));
                Assert.Equal(k!.name, UnityAsset.Field(lines, "m_Name"));
                Assert.Equal(((int)k.anchor).ToString(), UnityAsset.Field(lines, "anchor"));
                Assert.Equal(((int)k.rule).ToString(), UnityAsset.Field(lines, "rule"));
                Assert.Equal(k.stretch ? "1" : "0", UnityAsset.Field(lines, "stretch"));
                double D(string v) => double.Parse(v, CultureInfo.InvariantCulture);
                var size = UnityAsset.Field(lines, "size").Trim('{', '}').Split(',').Select(p => D(p.Split(':')[1])).ToArray();
                Assert.Equal(new[] { k.w, k.h, k.d }, size);
                Assert.Equal("{fileID: 0}", UnityAsset.Field(lines, "mesh"));   // the generator draws it
            }
            Assert.Equal(DetailKinds.BuiltIn.OrderBy(i => i, System.StringComparer.Ordinal), ids.OrderBy(i => i, System.StringComparer.Ordinal));
        }
    }
}
