using RevitCliBridge.Handlers;
using Xunit;

namespace RevitCliBridge.Tests
{
    public class ParamMatcherTests
    {
        [Fact]
        public void EqualsTarget_IntegerStorage_TextOne_MatchesNumberOne()
        {
            var ok = ParamMatcher.EqualsTarget("1", 1d, "1");
            Assert.True(ok);
        }

        [Fact]
        public void EqualsTarget_DoubleStorage_MatchesFractionalTarget()
        {
            var ok = ParamMatcher.EqualsTarget("240.63", 240.63d, "240.63");
            Assert.True(ok);
        }

        [Fact]
        public void EqualsTarget_IntegerStorage_NonNumericTarget_FallsBackToText()
        {
            var ok = ParamMatcher.EqualsTarget("1", 1d, "banana");
            Assert.False(ok);
        }

        [Fact]
        public void EqualsTarget_TextParameter_CaseInsensitive()
        {
            var ok = ParamMatcher.EqualsTarget("Timber Clad", null, "timber clad");
            Assert.True(ok);
        }

        [Fact]
        public void EqualsTarget_TextParameter_NullValue_NeverMatches()
        {
            var ok = ParamMatcher.EqualsTarget(null, null, "anything");
            Assert.False(ok);
        }

        [Fact]
        public void Compare_IntegerStorage_GtWorks()
        {
            Assert.True(ParamMatcher.Compare("5", 5d, "3") > 0);
            Assert.True(ParamMatcher.Compare("5", 5d, "5") == 0);
            Assert.True(ParamMatcher.Compare("5", 5d, "9") < 0);
        }

        [Fact]
        public void Compare_TextStorage_FallsBackToText()
        {
            Assert.True(ParamMatcher.Compare("b", null, "a") > 0);
            Assert.True(ParamMatcher.Compare("a", null, "b") < 0);
        }

        [Fact]
        public void Compare_IntegerStorage_NonNumericTarget_ComparesText()
        {
            Assert.True(ParamMatcher.Compare("5", 5d, "banana") < 0);
        }
    }
}
