using NUnit.Framework;

namespace Triband.Storey.PlayKit.Tests
{
    public class SmokeTests
    {
        [Test]
        public void PlayKitAssemblyLoads()
        {
            Assert.That(PlayKitInfo.Name, Is.Not.Empty);
        }
    }
}
