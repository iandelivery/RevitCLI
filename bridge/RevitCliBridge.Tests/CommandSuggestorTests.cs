using System.Collections.Generic;
using System.Linq;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for CommandSuggestor, the pure-logic name suggester behind the
    /// router's unknown-command "Did you mean" text.
    /// </summary>
    public class CommandSuggestorTests
    {
        private static readonly string[] Pool =
        {
            "create_wall", "create_walls", "delete_element", "get_elements"
        };

        [Fact]
        public void SuggestCloseNames_RanksNearestFirst()
        {
            var got = CommandSuggestor.SuggestCloseNames("create-wal", Pool);

            Assert.NotEmpty(got);
            Assert.Equal("create_wall", got[0]);
            Assert.Contains("create_walls", got);
        }

        [Fact]
        public void SuggestCloseNames_GarbageGetsNothing()
        {
            var got = CommandSuggestor.SuggestCloseNames("zzzzzzzzzz", Pool);
            Assert.Empty(got);
        }

        [Fact]
        public void SuggestCloseNames_LimitsToThree_AlphabeticalTies()
        {
            var pool = new[] { "probe_cmd_c", "probe_cmd_a", "probe_cmd_b", "probe_cmd_d" };
            var got = CommandSuggestor.SuggestCloseNames("probe_cmd", pool);

            Assert.Equal(new[] { "probe_cmd_a", "probe_cmd_b", "probe_cmd_c" }, got);
        }

        [Fact]
        public void SuggestCloseNames_EmptyTypedNameIsEmpty()
        {
            Assert.Empty(CommandSuggestor.SuggestCloseNames("", Pool));
        }

        [Fact]
        public void EditDistance_KnownValues()
        {
            Assert.Equal(0, CommandSuggestor.EditDistance("create_wall", "create_wall"));
            Assert.Equal(2, CommandSuggestor.EditDistance("create-wal", "create_wall"));
            Assert.Equal(3, CommandSuggestor.EditDistance("kitten", "sitting"));
            Assert.Equal(3, CommandSuggestor.EditDistance("", "abc"));
            Assert.Equal(2, CommandSuggestor.EditDistance("ab", ""));
        }
    }
}
