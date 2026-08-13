using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Paged query result. The <see cref="Items"/> list is the page slice;
    /// <see cref="HasMore"/> tells the caller whether another page exists.
    /// </summary>
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int Count { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }
        public bool HasMore { get; set; }
    }

    /// <summary>
    /// Pure paging logic — no Revit dependencies. Lives in Abstractions so it
    /// can be unit-tested without the Revit API.
    ///
    /// Usage: caller provides an <em>already-ordered</em> source (stable key
    /// required for correct pagination), then <see cref="Build{T}"/> applies
    /// Skip/Take and the has_more trick internally.
    /// </summary>
    public static class PagedResultBuilder
    {
        /// <summary>Default page size when caller omits <c>limit</c>.</summary>
        public const int DefaultLimit = 500;

        /// <summary>Hard upper bound on page size to protect memory.</summary>
        public const int MaxLimit = 5000;

        /// <summary>Default offset when caller omits <c>offset</c>.</summary>
        public const int DefaultOffset = 0;

        /// <summary>
        /// Parses <c>limit</c> and <c>offset</c> from a parameter dictionary,
        /// applying defaults and clamping to safe bounds:
        /// limit in [1, <see cref="MaxLimit"/>], offset &gt;= 0. Missing or
        /// invalid values fall back to defaults (backward compatible).
        /// </summary>
        public static (int limit, int offset) GetPagingParams(Dictionary<string, object>? parameters)
        {
            int limit = GetIntOrNull(parameters, "limit") ?? DefaultLimit;
            int offset = GetIntOrNull(parameters, "offset") ?? DefaultOffset;

            if (limit < 1) limit = DefaultLimit;
            if (limit > MaxLimit) limit = MaxLimit;
            if (offset < 0) offset = DefaultOffset;

            return (limit, offset);
        }

        /// <summary>
        /// Has_more trick: caller fetched <c>limit + 1</c> items. If the list
        /// contains more than <c>limit</c>, the extra tail is dropped and
        /// <c>hasMore = true</c>; otherwise <c>hasMore = false</c>. Avoids a
        /// full <c>Count()</c> over the source.
        /// </summary>
        public static (List<T> items, bool hasMore) ApplyPaging<T>(List<T> overFetched, int limit)
        {
            if (overFetched.Count > limit)
                return (overFetched.GetRange(0, limit), true);
            return (overFetched, false);
        }

        /// <summary>
        /// Builds a <see cref="PagedResult{T}"/> from an already-ordered source.
        /// Applies Skip(offset).Take(limit+1) internally, then the has_more trick.
        /// </summary>
        public static PagedResult<T> Build<T>(IEnumerable<T> orderedSource, int limit, int offset)
        {
            var overFetched = orderedSource.Skip(offset).Take(limit + 1).ToList();
            var (items, hasMore) = ApplyPaging(overFetched, limit);
            return new PagedResult<T>
            {
                Items = items,
                Count = items.Count,
                Offset = offset,
                Limit = limit,
                HasMore = hasMore
            };
        }

        private static int? GetIntOrNull(Dictionary<string, object>? parameters, string key)
        {
            if (parameters is null || !parameters.TryGetValue(key, out var value))
                return null;
            return value switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }
    }
}
