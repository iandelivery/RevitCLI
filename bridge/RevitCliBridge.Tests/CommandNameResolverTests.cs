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

        // ---------- SplitVersion ----------

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
            // Only the last @segment is treated as a version tag — the base
            // name may itself contain dots (domain path).
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

        // ---------- Versioned Resolve ----------

        [Fact]
        public void Resolve_VersionedExactMatch_ReturnsAsIs()
        {
            var names = new HashSet<string> { "create_wall@v1", "create_wall@v2" };
            Assert.Equal("create_wall@v2", CommandNameResolver.Resolve("create_wall@v2", names));
        }

        [Fact]
        public void Resolve_VersionedDomainPath_ReattachesVersion()
        {
            // "domain.create_wall@v2" → domain path strips "domain." →
            // "create_wall" matches "create_wall@v2" → return "create_wall@v2"
            var names = new HashSet<string> { "create_wall@v1", "create_wall@v2" };
            Assert.Equal("create_wall@v2", CommandNameResolver.Resolve("domain.create_wall@v2", names));
        }

        [Fact]
        public void Resolve_VersionedUnderscoreReversal_ReattachesVersion()
        {
            // "wall_create@v2" → base reverses to "create_wall" →
            // re-attach @v2 → "create_wall@v2"
            var names = new HashSet<string> { "create_wall@v2" };
            Assert.Equal("create_wall@v2", CommandNameResolver.Resolve("wall_create@v2", names));
        }

        [Fact]
        public void Resolve_VersionedNoMatch_ReturnsVersionedInput()
        {
            var names = new HashSet<string> { "create_wall@v1" };
            Assert.Equal("create_wall@v9", CommandNameResolver.Resolve("create_wall@v9", names));
        }

        // ---------- Edge cases: empty / unusual inputs ----------

        [Fact]
        public void Resolve_EmptyStringInput_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, CommandNameResolver.Resolve(string.Empty, SampleNames));
        }

        [Fact]
        public void Resolve_WhitespaceOnlyInput_TreatedAsExactMatchOrReturnedAsIs()
        {
            // Whitespace is not normalized — resolver returns it unchanged
            // when not an exact match.
            Assert.Equal("   ", CommandNameResolver.Resolve("   ", SampleNames));
        }

        [Fact]
        public void Resolve_InputWithTrailingDot_ReturnsInputWhenNoMatch()
        {
            var names = new HashSet<string> { "create_wall" };
            // Trailing dot yields an empty final segment; resolver returns
            // input unchanged since no suffix matches.
            Assert.Equal("create_wall.", CommandNameResolver.Resolve("create_wall.", names));
        }

        [Fact]
        public void Resolve_InputWithLeadingDot_StillFindsMatch()
        {
            // ".create_wall" → first suffix candidate is "create_wall".
            var names = new HashSet<string> { "create_wall" };
            Assert.Equal("create_wall", CommandNameResolver.Resolve(".create_wall", names));
        }

        [Fact]
        public void Resolve_DomainPathWithTrailingDot_PreservesExactMatch()
        {
            var names = new HashSet<string> { "create_wall." };
            Assert.Equal("create_wall.", CommandNameResolver.Resolve("create_wall.", names));
        }

        // ---------- Edge cases: underscore reversal ----------

        [Fact]
        public void Resolve_UnderscoreReversal_ThreeSegmentsNotReversed()
        {
            // Three-segment names do NOT trigger underscore reversal.
            var names = new HashSet<string> { "c_b_a" };
            Assert.Equal("a_b_c", CommandNameResolver.Resolve("a_b_c", names));
        }

        [Fact]
        public void Resolve_UnderscoreReversal_SingleSegmentNotReversed()
        {
            // No underscore → no reversal attempted.
            var names = new HashSet<string> { "create" };
            Assert.Equal("create", CommandNameResolver.Resolve("create", names));
        }

        [Fact]
        public void Resolve_UnderscoreReversal_OnlyUnderscoreChar_ReturnedAsIs()
        {
            // "_" splits into two empty segments; reversed is still "_" and
            // unlikely to match. Verify no exception is thrown.
            var names = new HashSet<string> { "create_wall" };
            Assert.Equal("_", CommandNameResolver.Resolve("_", names));
        }

        // ---------- Edge cases: version handling ----------

        [Fact]
        public void SplitVersion_LeadingAtSign_TreatsAsBase()
        {
            // "@v1" → baseName="", version="v1"
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
            // "a@b@v2" → baseName="a@b", version="v2"
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

        // ---------- Edge cases: combined rules ----------

        [Fact]
        public void Resolve_VersionedDomainPath_FallsBackToBareName()
        {
            // Versioned domain path where neither the versioned nor bare
            // base-name-with-version matches falls back to the versioned
            // input string.
            var names = new HashSet<string> { "create_wall@v1", "create_wall@v2" };
            Assert.Equal("domain.create_wall@v9",
                CommandNameResolver.Resolve("domain.create_wall@v9", names));
        }

        [Fact]
        public void Resolve_VersionedBareName_FallsBackToVersionedInput()
        {
            // Direct versioned name not registered → returned as-is (no
            // underscore/domain rules apply since there are no dots/underscores
            // in the base name).
            var names = new HashSet<string> { "create_wall@v1" };
            Assert.Equal("create_wall@v3", CommandNameResolver.Resolve("create_wall@v3", names));
        }

        [Fact]
        public void Resolve_VersionedUnderscoreNotMatched_ReturnsVersionedInput()
        {
            // "wall_other@v1" reverses to "other_wall@v1" — if not registered,
            // returns the input unchanged.
            var names = new HashSet<string> { "create_wall@v1" };
            Assert.Equal("wall_other@v1",
                CommandNameResolver.Resolve("wall_other@v1", names));
        }
    }
}
