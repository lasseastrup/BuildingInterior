using System.Collections.Generic;
using Triband.Storey.Text;
using Xunit;

namespace Triband.Storey.Tests
{
    public class JsonTests
    {
        [Theory]
        [InlineData("0.1")]
        [InlineData("1e-7")]
        [InlineData("123456789.123456789")]
        [InlineData("-2.2250738585072014e-308")]
        [InlineData("1.7976931348623157e308")]
        [InlineData("0.30000000000000004")]
        public void ADoubleReadsBackToTheSameBitsItWasWrittenFrom(string literal)
        {
            double parsed = (double)Json.Parse(literal)!;
            double again = (double)Json.Parse(Json.Write(parsed))!;
            Assert.Equal(System.BitConverter.DoubleToInt64Bits(parsed), System.BitConverter.DoubleToInt64Bits(again));
        }

        [Fact]
        public void StringsEscapeAndUnescape()
        {
            const string s = "quote \" backslash \\ newline \n tab \t unicode é control \u0001";
            Assert.Equal(s, Json.Parse(Json.Write(s)));
        }

        [Fact]
        public void ObjectsAndArraysRoundTrip()
        {
            var tree = Json.Parse("{\"a\":[1,2,{\"b\":null,\"c\":true}],\"d\":\"e\"}") as Dictionary<string, object?>;
            Assert.NotNull(tree);
            Assert.Equal("{\"a\":[1,2,{\"b\":null,\"c\":true}],\"d\":\"e\"}", Json.Write(tree));
        }

        [Fact]
        public void TrailingGarbageIsAnError()
        {
            Assert.Throws<System.FormatException>(() => Json.Parse("{} x"));
        }
    }
}
