using NUnit.Framework;

namespace Triband.Storey.Tests
{
    /// <summary>Workstream 0: proves the runtime assembly compiles, loads, and is reachable from the test runner.</summary>
    public class SmokeTests
    {
        [Test]
        public void RuntimeAssemblyLoads()
        {
            Assert.That(StoreyVersion.DataFormat, Is.GreaterThanOrEqualTo(1));
            Assert.That(StoreyVersion.PrototypeSchema, Is.GreaterThanOrEqualTo(1));
        }
    }
}
