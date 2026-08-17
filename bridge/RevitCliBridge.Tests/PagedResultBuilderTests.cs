using RevitCliBridge.Abstractions;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RevitCliBridge.Tests
{
    public class PagedResultBuilderTests
    {
        // ---------- GetPagingParams ----------

        [Fact]
        public void GetPagingParams_NullParameters_ReturnsDefaults()
        {
            var (limit, offset) = PagedResultBuilder.GetPagingParams(null);
            Assert.Equal(PagedResultBuilder.DefaultLimit, limit);
            Assert.Equal(PagedResultBuilder.DefaultOffset, offset);
        }

        [Fact]
        public void GetPagingParams_EmptyDict_ReturnsDefaults()
        {
            var (limit, offset) = PagedResultBuilder.GetPagingParams(new Dictionary<string, object>());
            Assert.Equal(PagedResultBuilder.DefaultLimit, limit);
            Assert.Equal(PagedResultBuilder.DefaultOffset, offset);
        }

        [Fact]
        public void GetPagingParams_ValidLimitOffset_ReturnsThem()
        {
            var param = new Dictionary<string, object> { ["limit"] = 100, ["offset"] = 200 };
            var (limit, offset) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(100, limit);
            Assert.Equal(200, offset);
        }

        [Fact]
        public void GetPagingParams_LimitExceedsMax_ClampedToMax()
        {
            var param = new Dictionary<string, object> { ["limit"] = 99999 };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(PagedResultBuilder.MaxLimit, limit);
        }

        [Fact]
        public void GetPagingParams_LimitZero_FallsBackToDefault()
        {
            var param = new Dictionary<string, object> { ["limit"] = 0 };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(PagedResultBuilder.DefaultLimit, limit);
        }

        [Fact]
        public void GetPagingParams_NegativeLimit_FallsBackToDefault()
        {
            var param = new Dictionary<string, object> { ["limit"] = -5 };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(PagedResultBuilder.DefaultLimit, limit);
        }

        [Fact]
        public void GetPagingParams_NegativeOffset_ResetToZero()
        {
            var param = new Dictionary<string, object> { ["offset"] = -10 };
            var (_, offset) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(0, offset);
        }

        [Fact]
        public void GetPagingParams_StringLimit_ParsedCorrectly()
        {
            var param = new Dictionary<string, object> { ["limit"] = "50" };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(50, limit);
        }

        [Fact]
        public void GetPagingParams_InvalidStringLimit_FallsBackToDefault()
        {
            var param = new Dictionary<string, object> { ["limit"] = "abc" };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(PagedResultBuilder.DefaultLimit, limit);
        }

        // ---------- ApplyPaging ----------

        [Fact]
        public void ApplyPaging_FewerThanLimit_ReturnsAll_NoHasMore()
        {
            var data = Enumerable.Range(1, 5).ToList();
            var (items, hasMore) = PagedResultBuilder.ApplyPaging(data, 10);
            Assert.Equal(5, items.Count);
            Assert.False(hasMore);
        }

        [Fact]
        public void ApplyPaging_ExactlyLimit_ReturnsAll_NoHasMore()
        {
            var data = Enumerable.Range(1, 10).ToList();
            var (items, hasMore) = PagedResultBuilder.ApplyPaging(data, 10);
            Assert.Equal(10, items.Count);
            Assert.False(hasMore);
        }

        [Fact]
        public void ApplyPaging_MoreThanLimit_Truncates_SetsHasMore()
        {
            var data = Enumerable.Range(1, 11).ToList();
            var (items, hasMore) = PagedResultBuilder.ApplyPaging(data, 10);
            Assert.Equal(10, items.Count);
            Assert.True(hasMore);
            Assert.Equal(1, items[0]);
            Assert.Equal(10, items[9]);
        }

        [Fact]
        public void ApplyPaging_EmptyList_ReturnsEmpty_NoHasMore()
        {
            var data = new List<int>();
            var (items, hasMore) = PagedResultBuilder.ApplyPaging(data, 10);
            Assert.Empty(items);
            Assert.False(hasMore);
        }

        // ---------- Build ----------

        [Fact]
        public void Build_FirstPage_FullData_ReturnsCorrectPage()
        {
            var source = Enumerable.Range(1, 100);
            var result = PagedResultBuilder.Build(source, 10, 0);
            Assert.Equal(10, result.Count);
            Assert.Equal(0, result.Offset);
            Assert.Equal(10, result.Limit);
            Assert.True(result.HasMore);
            Assert.Equal(1, result.Items[0]);
            Assert.Equal(10, result.Items[9]);
        }

        [Fact]
        public void Build_SecondPage_ReturnsCorrectOffset()
        {
            var source = Enumerable.Range(1, 100);
            var result = PagedResultBuilder.Build(source, 10, 10);
            Assert.Equal(10, result.Count);
            Assert.Equal(10, result.Offset);
            Assert.True(result.HasMore);
            Assert.Equal(11, result.Items[0]);
            Assert.Equal(20, result.Items[9]);
        }

        [Fact]
        public void Build_LastPage_PartialData_NoHasMore()
        {
            var source = Enumerable.Range(1, 25);
            var result = PagedResultBuilder.Build(source, 10, 20);
            Assert.Equal(5, result.Count);
            Assert.False(result.HasMore);
            Assert.Equal(21, result.Items[0]);
            Assert.Equal(25, result.Items[4]);
        }

        [Fact]
        public void Build_OffsetBeyondData_ReturnsEmpty_NoHasMore()
        {
            var source = Enumerable.Range(1, 10);
            var result = PagedResultBuilder.Build(source, 10, 100);
            Assert.Equal(0, result.Count);
            Assert.False(result.HasMore);
            Assert.Empty(result.Items);
        }

        [Fact]
        public void Build_ExactlyOnePage_NoHasMore()
        {
            var source = Enumerable.Range(1, 10);
            var result = PagedResultBuilder.Build(source, 10, 0);
            Assert.Equal(10, result.Count);
            Assert.False(result.HasMore);
        }

        [Fact]
        public void Build_OneMoreThanPage_HasMore()
        {
            var source = Enumerable.Range(1, 11);
            var result = PagedResultBuilder.Build(source, 10, 0);
            Assert.Equal(10, result.Count);
            Assert.True(result.HasMore);
        }

        [Fact]
        public void Build_EmptySource_ReturnsEmpty_NoHasMore()
        {
            var source = Enumerable.Empty<int>();
            var result = PagedResultBuilder.Build(source, 10, 0);
            Assert.Equal(0, result.Count);
            Assert.False(result.HasMore);
            Assert.Empty(result.Items);
        }

        // ---------- Edge cases: numeric type coercion ----------

        [Fact]
        public void GetPagingParams_LongValue_ConvertedToInt()
        {
            var param = new Dictionary<string, object> { ["limit"] = 50L };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(50, limit);
        }

        [Fact]
        public void GetPagingParams_DoubleValue_TruncatedToInt()
        {
            // Doubles are cast to int via the switch expression in GetIntOrNull.
            var param = new Dictionary<string, object> { ["limit"] = 50.5 };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(50, limit);
        }

        [Fact]
        public void GetPagingParams_NonNumericObject_FallsBackToDefault()
        {
            var param = new Dictionary<string, object> { ["limit"] = new { x = 1 } };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(PagedResultBuilder.DefaultLimit, limit);
        }

        [Fact]
        public void GetPagingParams_BoolValue_FallsBackToDefault()
        {
            // bool is not int/long/double/string → returns null → default.
            var param = new Dictionary<string, object> { ["limit"] = true };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(PagedResultBuilder.DefaultLimit, limit);
        }

        // ---------- Edge cases: boundary values ----------

        [Fact]
        public void GetPagingParams_LimitAtMax_PreservedExactly()
        {
            var param = new Dictionary<string, object> { ["limit"] = PagedResultBuilder.MaxLimit };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(PagedResultBuilder.MaxLimit, limit);
        }

        [Fact]
        public void GetPagingParams_LimitAtMaxPlusOne_ClampedToMax()
        {
            var param = new Dictionary<string, object> { ["limit"] = PagedResultBuilder.MaxLimit + 1 };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(PagedResultBuilder.MaxLimit, limit);
        }

        [Fact]
        public void GetPagingParams_LimitOne_PreservedExactly()
        {
            // Smallest valid limit — boundary check.
            var param = new Dictionary<string, object> { ["limit"] = 1 };
            var (limit, _) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(1, limit);
        }

        [Fact]
        public void GetPagingParams_OffsetZero_PreservedExactly()
        {
            var param = new Dictionary<string, object> { ["offset"] = 0 };
            var (_, offset) = PagedResultBuilder.GetPagingParams(param);
            Assert.Equal(0, offset);
        }

        // ---------- Edge cases: ApplyPaging boundary ----------

        [Fact]
        public void ApplyPaging_CountExactlyOneMoreThanLimit_TruncatesAndSetsHasMore()
        {
            var data = Enumerable.Range(1, 11).ToList();
            var (items, hasMore) = PagedResultBuilder.ApplyPaging(data, 10);
            Assert.Equal(10, items.Count);
            Assert.True(hasMore);
        }

        [Fact]
        public void ApplyPaging_LimitLargerThanAvailable_ReturnsAll_NoHasMore()
        {
            var data = Enumerable.Range(1, 3).ToList();
            var (items, hasMore) = PagedResultBuilder.ApplyPaging(data, 100);
            Assert.Equal(3, items.Count);
            Assert.False(hasMore);
        }

        // ---------- Edge cases: Build integration ----------

        [Fact]
        public void Build_OffsetEqualsDataLength_ReturnsEmpty_NoHasMore()
        {
            var source = Enumerable.Range(1, 10);
            var result = PagedResultBuilder.Build(source, 10, 10);
            Assert.Equal(0, result.Count);
            Assert.False(result.HasMore);
            Assert.Empty(result.Items);
        }

        [Fact]
        public void Build_OffsetOneBeyondDataLength_ReturnsEmpty_NoHasMore()
        {
            var source = Enumerable.Range(1, 10);
            var result = PagedResultBuilder.Build(source, 10, 11);
            Assert.Equal(0, result.Count);
            Assert.False(result.HasMore);
        }

        [Fact]
        public void Build_PreservesOffsetAndLimitInResult()
        {
            // The returned PagedResult echoes the requested limit/offset
            // even when the actual data is smaller.
            var source = Enumerable.Range(1, 3);
            var result = PagedResultBuilder.Build(source, limit: 10, offset: 20);
            Assert.Equal(10, result.Limit);
            Assert.Equal(20, result.Offset);
            Assert.Equal(0, result.Count);
            Assert.False(result.HasMore);
        }

        [Fact]
        public void Build_LimitExactlyAtSourceSize_ReturnsAllNoHasMore()
        {
            var source = Enumerable.Range(1, 100);
            var result = PagedResultBuilder.Build(source, 100, 0);
            Assert.Equal(100, result.Count);
            Assert.False(result.HasMore);
        }

        [Fact]
        public void Build_SourceLargerByOne_HasMoreTrue()
        {
            var source = Enumerable.Range(1, 101);
            var result = PagedResultBuilder.Build(source, 100, 0);
            Assert.Equal(100, result.Count);
            Assert.True(result.HasMore);
        }

        [Fact]
        public void Build_WithCustomType_PreservesItems()
        {
            var source = new[] { "alpha", "beta", "gamma", "delta", "epsilon" };
            var result = PagedResultBuilder.Build(source, 2, 1);
            Assert.Equal(2, result.Count);
            Assert.Equal("beta", result.Items[0]);
            Assert.Equal("gamma", result.Items[1]);
            Assert.True(result.HasMore);
        }
    }
}
