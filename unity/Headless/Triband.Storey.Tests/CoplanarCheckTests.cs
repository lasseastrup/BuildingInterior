using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>The z-fighting check (<see cref="CoplanarCheck"/>, SPEC §4.5) on faces made for it.</summary>
    public class CoplanarCheckTests
    {
        static readonly Swatch Wall = new Swatch(new StyleRef(0, 0), ColorSlot.Wall), Trim = new Swatch(new StyleRef(0, 0), ColorSlot.Trim);

        /// <summary>Overlaps between two level 2 m squares a metre apart each way, at the same height, in two colours.</summary>
        static int Overlaps(P3 n0, P3 n1)
        {
            var m = MeshBuilder.Make();
            try
            {
                m.Quad(new P3(0, 1, 0), new P3(0, 1, 2), new P3(2, 1, 2), new P3(2, 1, 0), n0, Wall);
                m.Quad(new P3(1, 1, 1), new P3(1, 1, 3), new P3(3, 1, 3), new P3(3, 1, 1), n1, Trim);
                return CoplanarCheck.Run(m).Overlaps.Count;
            }
            finally { m.Recycle(); }
        }

        [Fact]
        public void FacesInOnePlaneAreCheckedTogetherWhenANormalHasANegativeZero()
        {
            // a level face's normal worked out from a slope of 0 (a bridge's deck) can come out (−0, 1, −0)
            var up = new P3(0, 1, 0); var up0 = new P3(-0.0, 1, -0.0);
            int n = Overlaps(up, up);
            Assert.True(n > 0);
            Assert.Equal(n, Overlaps(up, up0));
        }
    }
}
