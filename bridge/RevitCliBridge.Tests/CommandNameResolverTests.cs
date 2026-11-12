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
            Assert.Equal("create_wall", CommandNameResolver.Resolve("wall_create", SampleNames));
        }

        [Fact]
        public void Resolve_UnderscoreReversal_OnlyAppliesToTwoSegments()
        {
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
            var names = new HashSet<string> { "walls.create" };
            Assert.Equal("walls.create", CommandNameResolver.Resolve("domain.walls.create", names));
        }

        [Fact]
        public void Resolve_DomainPath_FindsMiddleSegment()
        {
            var names = new HashSet<string> { "c.d" };
            Assert.Equal("c.d", CommandNameResolver.Resolve("a.b.c.d", names));
        }

        [Fact]
        public void SplitVersion_NoAtSign_ReturnsBaseAndNullVersion()
        {
            var (baseName, version) = CommandNameResolver.SplitVersion("create_wall");
            Assert.Equal("create_wall", baseName);
            Assert.Null(version);
        }

        [Fact]
        public void SplitVersion_WithAtSign_SplitsCorrectly()
        {
            var (baseName, version) = CommandNameResolver.SplitVersion("create_wall@v2");
            Assert.Equal("create_wall", baseName);
            Assert.Equal("v2", version);
        }

        [Fact]
        public void SplitVersion_DomainPathWithVersion_OnlyLastAtIsVersion()
        {
            var (baseName, version) = CommandNameResolver.SplitVersion("elements.walls.create@v2");
            Assert.Equal("elements.walls.create", baseName);
            Assert.Equal("v2", version);
        }

        [Fact]
        public void SplitVersion_NullInput_ReturnsEmptyAndNull()
        {
            var (baseName, version) = CommandNameResolver.SplitVersion(null!);
            Assert.Equal(string.Empty, baseName);
            Assert.Null(version);
        }

        [Fact]
        public void Resolve_VersionedExactMatch_ReturnsAsIs()
        {
            var names = new HashSet<string> { "create_wall@v1", "create_wall@v2" };
            Assert.Equal("create_wall@v2", CommandNameResolver.Resolve("create_wall@v2", names));
        }

        [Fact]
        public void Resolve_VersionedDomainPath_ReattachesVersion()
        {
            var names = new HashSet<string> { "create_wall@v1", "create_wall@v2" };
            Assert.Equal("create_wall@v2", CommandNameResolver.Resolve("domain.create_wall@v2", names));
        }

        [Fact]
        public void Resolve_VersionedUnderscoreReversal_ReattachesVersion()
        {
            var names = new HashSet<string> { "create_wall@v2" };
            Assert.Equal("create_wall@v2", CommandNameResolver.Resolve("wall_create@v2", names));
        }

        [Fact]
        public void Resolve_VersionedNoMatch_ReturnsVersionedInput()
        {
            var names = new HashSet<string> { "create_wall@v1" };
            Assert.Equal("create_wall@v9", CommandNameResolver.Resolve("create_wall@v9", names));
        }

        [Fact]
        public void Resolve_EmptyStringInput_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, CommandNameResolver.Resolve(string.Empty, SampleNames));
        }

        [Fact]
        public void Resolve_WhitespaceOnlyInput_TreatedAsExactMatchOrReturnedAsIs()
        {
            Assert.Equal("   ", CommandNameResolver.Resolve("   ", SampleNames));
        }

        [Fact]
        public void Resolve_InputWithTrailingDot_ReturnsInputWhenNoMatch()
        {
            var names = new HashSet<string> { "create_wall" };
            Assert.Equal("create_wall.", CommandNameResolver.Resolve("create_wall.", names));
        }

        [Fact]
        public void Resolve_InputWithLeadingDot_StillFindsMatch()
        {
            var names = new HashSet<string> { "create_wall" };
            Assert.Equal("create_wall", CommandNameResolver.Resolve(".create_wall", names));
        }

        [Fact]
        public void Resolve_DomainPathWithTrailingDot_PreservesExactMatch()
        {
            var names = new HashSet<string> { "create_wall." };
            Assert.Equal("create_wall.", CommandNameResolver.Resolve("create_wall.", names));
        }

        [Fact]
        public void Resolve_UnderscoreReversal_ThreeSegmentsNotReversed()
        {
            var names = new HashSet<string> { "c_b_a" };
            Assert.Equal("a_b_c", CommandNameResolver.Resolve("a_b_c", names));
        }

        [Fact]
        public void Resolve_UnderscoreReversal_SingleSegmentNotReversed()
        {
            var names = new HashSet<string> { "create" };
            Assert.Equal("create", CommandNameResolver.Resolve("create", names));
        }

        [Fact]
        public void Resolve_UnderscoreReversal_OnlyUnderscoreChar_ReturnedAsIs()
        {
            var names = new HashSet<string> { "create_wall" };
            Assert.Equal("_", CommandNameResolver.Resolve("_", names));
        }

        [Fact]
        public void SplitVersion_LeadingAtSign_TreatsAsBase()
        {
            var (baseName, version) = CommandNameResolver.SplitVersion("@v1");
            Assert.Equal(string.Empty, baseName);
            Assert.Equal("v1", version);
        }

        [Fact]
        public void SplitVersion_TrailingAtSign_ReturnsEmptyVersion()
        {
            var (baseName, version) = CommandNameResolver.SplitVersion("create_wall@");
            Assert.Equal("create_wall", baseName);
            Assert.Equal(string.Empty, version);
        }

        [Fact]
        public void SplitVersion_MultipleAtSigns_OnlyLastSplitsVersion()
        {
            var (baseName, version) = CommandNameResolver.SplitVersion("a@b@v2");
            Assert.Equal("a@b", baseName);
            Assert.Equal("v2", version);
        }

        [Fact]
        public void SplitVersion_EmptyStringInput_ReturnsEmptyAndNull()
        {
            var (baseName, version) = CommandNameResolver.SplitVersion(string.Empty);
            Assert.Equal(string.Empty, baseName);
            Assert.Null(version);
        }

        [Fact]
        public void Resolve_VersionedDomainPath_FallsBackToBareName()
        {
            var names = new HashSet<string> { "create_wall@v1", "create_wall@v2" };
            Assert.Equal("domain.create_wall@v9",
                CommandNameResolver.Resolve("domain.create_wall@v9", names));
        }

        [Fact]
        public void Resolve_VersionedBareName_FallsBackToVersionedInput()
        {
            var names = new HashSet<string> { "create_wall@v1" };
            Assert.Equal("create_wall@v3", CommandNameResolver.Resolve("create_wall@v3", names));
        }

        [Fact]
        public void Resolve_VersionedUnderscoreNotMatched_ReturnsVersionedInput()
        {
            var names = new HashSet<string> { "create_wall@v1" };
            Assert.Equal("wall_other@v1",
                CommandNameResolver.Resolve("wall_other@v1", names));
        }
    }
}
