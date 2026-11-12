using RevitCliBridge.Handlers;
using Xunit;

namespace RevitCliBridge.Tests
{
    public class ParamConverterTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TryConvertBool_BoolValue_ReturnsIt(bool value)
        {
            var ok = ParamConverter.TryConvertBool(value, out var result);
            Assert.True(ok);
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("True", true)]
        [InlineData("false", false)]
        [InlineData("False", false)]
        public void TryConvertBool_BoolString_Parses(string value, bool expected)
        {
            var ok = ParamConverter.TryConvertBool(value, out var result);
            Assert.True(ok);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("banana")]
        [InlineData("1")]
        [InlineData("")]
        public void TryConvertBool_NonBoolString_Fails(string value)
        {
            var ok = ParamConverter.TryConvertBool(value, out var result);
            Assert.False(ok);
            Assert.False(result);
        }

        [Fact]
        public void TryConvertBool_Null_Fails()
        {
            var ok = ParamConverter.TryConvertBool(null, out var result);
            Assert.False(ok);
            Assert.False(result);
        }

        [Fact]
        public void TryConvertBool_LongString_FailsWithoutThrow()
        {
            var ok = ParamConverter.TryConvertBool("not a bool at all", out _);
            Assert.False(ok);
        }
    }
}
