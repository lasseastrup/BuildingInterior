using NUnit.Framework;

namespace Triband.Storey.Editor.Tests
{
    /// <summary>Workstream 0: proves the editor assembly compiles and can see the runtime's internals.</summary>
    public class SmokeTests
    {
        [Test]
        public void EditorAssemblyLoads()
        {
            Assert.That(StoreyEditorInfo.MenuRoot, Does.StartWith("Tools/"));
            Assert.That(StoreyVersion.DataFormat, Is.EqualTo(1));
        }
    }
}
