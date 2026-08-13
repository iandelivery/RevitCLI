using System.Collections.Generic;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for CommandNameResolver — the pure-logic command name resolution
    /// extracted from CommandRouter. Covers exact match, domain path suffix
    /// matching, and underscore reversal rules.
    /// </summary>
    public class CommandNameResolverTests
    {
        private static readonly ISet<string> SampleNames = new HashSet<string>
        {
            "create_wall", "create_door", "get_elements", "doc_open", "list"
        };

        [Fact]
        public void Resolve_ReturnsInputOnExactMatch()
        {
            Assert.Equal("create_wall", CommandNameResolver.Resolve("create_wall", SampleNames));
        }

        [Fact]
        public void Resolve_ReturnsInputWhenNoMatch()
        {
            Assert.Equal("nonexistent", CommandNameResolver.Resolve("nonexistent", SampleNames));
        }

        [Fact]
        public void Resolve_HandlesNullInput()
        {
            Assert.Equal(string.Empty, CommandNameResolver.Resolve(null!, SampleNames));
        }

        [Fact]
        public void Resolve_HandlesEmptyRegisteredNames()
        {
            Assert.Equal("anything", CommandNameResolver.Resolve("anything", new HashSet<string>()));
        }

        [Fact]
        public void Resolve_HandlesNullRegisteredNames()
        {
            Assert.Equal("anything", CommandNameResolver.Resolve("anything", null!));
        }

        [Fact]
        public void Resolve_DomainPath_FindsSuffix()
        {
            // "elements.walls.create" → "walls.create" not registered, "create" not registered alone,
            // but with this sample set the rule should still be exercised.
            var names = new HashSet<string> { "walls.create", "create" };
            Assert.Equal("walls.create", CommandNameResolver.Resolve("elements.walls.create", names));
        }

        [Fact]
        public void Resolve_DomainPath_FindsLastSegment()
        {
            var names = new HashSet<string> { "create" };
            Assert.Equal("create", CommandNameResolver.Resolve("elements.walls.create", names));
        }

        [Fact]
        public void Resolve_DomainPath_ReturnsInputWhenNoSuffixMatches()
        {
            var names = new HashSet<string> { "other" };
            Assert.Equal("elements.walls.create", CommandNameResolver.Resolve("elements.walls.create", names));
        }

        [Fact]
        public void Resolve_UnderscoreReversal_FindsReversed()
        {
            // "wall_create" → "create_wall"
            Assert.Equal("create_wall", CommandNameResolver.Resolve("wall_create", SampleNames));
        }

        [Fact]
        public void Resolve_UnderscoreReversal_OnlyAppliesToTwoSegments()
        {
            // Three-segment names should not be reversed.
            var names = new HashSet<string> { "a_b_c_reversed" };
            Assert.Equal("a_b_c", CommandNameResolver.Resolve("a_b_c", names));
        }

        [Fact]
        public void Resolve_UnderscoreReversal_ReturnsInputWhenReversedNotRegistered()
        {
            var names = new HashSet<string> { "other_command" };
            Assert.Equal("wall_create", CommandNameResolver.Resolve("wall_create", names));
        }

        [Fact]
        public void Resolve_CombinedDomainAndUnderscore_DomainTakesPrecedence()
        {
            // Domain path is tried first; underscore reversal only if no domain match.
            var names = new HashSet<string> { "walls.create" };
            Assert.Equal("walls.create", CommandNameResolver.Resolve("domain.walls.create", names));
        }

        [Fact]
        public void Resolve_DomainPath_FindsMiddleSegment()
        {
            // "a.b.c.d" with "c.d" registered should resolve to "c.d"
            var names = new HashSet<string> { "c.d" };
            Assert.Equal("c.d", CommandNameResolver.Resolve("a.b.c.d", names));
        }
    }
}
