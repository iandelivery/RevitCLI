using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Standardized paging constants shared across query handlers.
    /// </summary>
    public static class PagingDefaults
    {
        /// <summary>Default page size when caller omits <c>limit</c>.</summary>
        public const int DefaultLimit = 500;

        /// <summary>Hard upper bound on page size to protect memory.</summary>
        public const int MaxLimit = 5000;

        /// <summary>Default offset when caller omits <c>offset</c>.</summary>
        public const int DefaultOffset = 0;
    }

    public static class HandlerUtilities
    {
        public static void ConfigureFailureHandling(this Transaction t)
        {
            var options = t.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new CliFailurePreprocessor());
            t.SetFailureHandlingOptions(options);
        }

        public static double? GetDoubleOrNull(Dictionary<string, object>? parameters, string key)
        {
            if (parameters is null)
                return null;

            if (parameters.TryGetValue(key, out var val) && val is not null)
            {
                try { return Convert.ToDouble(val); }
                catch { return null; }
            }
            return null;
        }

        public static int? GetIntOrNull(Dictionary<string, object>? parameters, string key)
        {
            if (parameters is null)
                return null;

            if (parameters.TryGetValue(key, out var val) && val is not null)
            {
                try { return Convert.ToInt32(val); }
                catch { return null; }
            }
            return null;
        }

        public static string? GetStringOrNull(Dictionary<string, object>? parameters, string key)
        {
            if (parameters is null)
                return null;

            if (parameters.TryGetValue(key, out var val) && val is not null)
            {
                return val.ToString();
            }
            return null;
        }

        public static int[]? GetIntArrayOrNull(Dictionary<string, object> parameters, string key)
        {
            if (parameters.TryGetValue(key, out var val) && val is not null)
            {
                try
                {
                    if (val is System.Collections.IEnumerable enumerable && val is not string)
                    {
                        var list = new List<int>();
                        foreach (var item in enumerable)
                        {
                            list.Add(Convert.ToInt32(item));
                        }
                        return list.ToArray();
                    }
                    return null;
                }
                catch { return null; }
            }
            return null;
        }

        public static List<Document> ResolveLinkDocuments(Document doc, List<RevitLinkInstance> linkInstances, Dictionary<string, object> parameters)
        {
            bool noLinks = parameters.ContainsKey("no_links");
            bool allLinks = parameters.ContainsKey("all_links");
            string[]? linkNames = null;

            if (parameters.TryGetValue("link_names", out var linkNamesObj) && linkNamesObj is not null)
            {
                if (linkNamesObj is System.Collections.IEnumerable enumerable && linkNamesObj is not string)
                {
                    var nameList = new List<string>();
                    foreach (var item in enumerable)
                        nameList.Add(item.ToString() ?? "");
                    linkNames = nameList.ToArray();
                }
                else
                {
                    linkNames = linkNamesObj.ToString()?.Split(',');
                }
            }

            if (noLinks)
                return new List<Document>();

            if (linkNames is not null && linkNames.Length > 0)
            {
                var nameSet = new HashSet<string>(linkNames);
                return linkInstances
                    .Where(l => nameSet.Contains(l.Name))
                    .Select(l => l.GetLinkDocument())
                    .Where(d => d is not null)
                    .ToList()!;
            }

            if (allLinks)
            {
                return linkInstances
                    .Select(l => l.GetLinkDocument())
                    .Where(d => d is not null)
                    .ToList()!;
            }

            return linkInstances
                .Select(l => l.GetLinkDocument())
                .Where(d => d is not null)
                .ToList()!;
        }

        /// <summary>
        /// Parses standardized paging parameters (<c>limit</c>, <c>offset</c>)
        /// from a command parameter dictionary. Applies defaults and clamps to
        /// safe bounds: limit in [1, MaxLimit], offset &gt;= 0. Missing or
        /// invalid values fall back to defaults (backward compatible).
        /// </summary>
        /// <returns>
        /// (limit, offset) tuple ready to feed into Skip(offset).Take(limit+1).
        /// </returns>
        public static (int limit, int offset) GetPagingParams(Dictionary<string, object>? parameters)
        {
            int limit = GetIntOrNull(parameters, "limit") ?? PagingDefaults.DefaultLimit;
            int offset = GetIntOrNull(parameters, "offset") ?? PagingDefaults.DefaultOffset;

            if (limit < 1) limit = PagingDefaults.DefaultLimit;
            if (limit > PagingDefaults.MaxLimit) limit = PagingDefaults.MaxLimit;
            if (offset < 0) offset = PagingDefaults.DefaultOffset;

            return (limit, offset);
        }

        /// <summary>
        /// Applies the "has_more" paging trick: the caller must have fetched
        /// <c>limit + 1</c> items. If <paramref name="overFetched"/> contains
        /// more than <paramref name="limit"/> items, the extra tail is dropped
        /// and <c>hasMore = true</c>; otherwise <c>hasMore = false</c>. This
        /// avoids a full <c>Count()</c> over the source collection.
        /// </summary>
        public static (List<T> items, bool hasMore) ApplyPaging<T>(List<T> overFetched, int limit)
        {
            if (overFetched.Count > limit)
            {
                return (overFetched.GetRange(0, limit), true);
            }
            return (overFetched, false);
        }
    }
}
