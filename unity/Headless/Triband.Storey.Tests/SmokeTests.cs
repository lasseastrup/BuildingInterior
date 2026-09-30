using Triband.Storey;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Workstream 0: the engine-free assembly builds and is reachable from the test runner.</summary>
    public class SmokeTests
    {
        [Fact]
        public void TheRuntimeAssemblyLoads()
        {
            Assert.True(StoreyVersion.DataFormat >= 1);
            Assert.True(StoreyVersion.PrototypeSchema >= 1);
        }
    }
}
