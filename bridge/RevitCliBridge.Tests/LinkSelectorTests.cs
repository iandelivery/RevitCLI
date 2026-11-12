using RevitCliBridge.Handlers;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for the pure link-selector parsing (LinkSelectorParser, linked
    /// from the main project). The Document walk in
    /// HandlerUtilities.TryResolveTargetDocument takes Revit types and is
    /// covered by live verification.
    /// </summary>
    public class LinkSelectorTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ResolveLinkSelector_Empty_ReturnsNull(string? raw)
        {
            Assert.Null(LinkSelectorParser.ResolveLinkSelector(raw));
        }

        [Theory]
        [InlineData("184324")]
        [InlineData(" 184324 ")]
        public void ResolveLinkSelector_PositiveInteger_ReturnsInstanceId(string raw)
        {
            var selector = LinkSelectorParser.ResolveLinkSelector(raw);
            Assert.NotNull(selector);
            Assert.Equal(184324, selector!.InstanceId);
            Assert.Null(selector.Name);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        public void ResolveLinkSelector_NonPositiveInteger_TreatedAsName(string raw)
        {
            // Element ids are positive; anything else that parses as an
            // integer cannot be an instance id and falls through to the
            // name path instead of silently matching nothing.
            var selector = LinkSelectorParser.ResolveLinkSelector(raw);
            Assert.NotNull(selector);
            Assert.Null(selector!.InstanceId);
            Assert.Equal(raw, selector.Name);
        }

        [Fact]
        public void ResolveLinkSelector_Name_ReturnsName()
        {
            var selector = LinkSelectorParser.ResolveLinkSelector("Structural Model.rvt");
            Assert.NotNull(selector);
            Assert.Null(selector!.InstanceId);
            Assert.Equal("Structural Model.rvt", selector.Name);
        }

        [Fact]
        public void ResolveLinkSelector_NameIsTrimmed()
        {
            var selector = LinkSelectorParser.ResolveLinkSelector("  Structural Model.rvt\t");
            Assert.Equal("Structural Model.rvt", selector!.Name);
        }

        [Fact]
        public void Describe_InstanceId_ReturnsIdForm()
        {
            var selector = LinkSelectorParser.ResolveLinkSelector("184324");
            Assert.NotNull(selector);
            Assert.Equal("id 184324", LinkSelectorParser.Describe(selector!));
        }

        [Fact]
        public void Describe_Name_ReturnsQuotedNameForm()
        {
            var selector = LinkSelectorParser.ResolveLinkSelector("Structural Model.rvt");
            Assert.NotNull(selector);
            Assert.Equal("name 'Structural Model.rvt'", LinkSelectorParser.Describe(selector!));
        }
    }
}
