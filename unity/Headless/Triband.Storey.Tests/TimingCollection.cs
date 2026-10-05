using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The timing benchmarks run on their own, not alongside the other tests: measured while every core is busy with
    /// another test class they read several times slower, and their bounds (catching a regression) would fail on noise.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class TimingCollection
    {
        public const string Name = "Timing";
    }
}
