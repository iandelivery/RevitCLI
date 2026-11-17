using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Suggests command names close to a typed name, so unknown-command
    /// errors can carry "Did you mean" candidates. Pure logic with no Revit
    /// dependency; used by the CommandRouter and unit-tested directly.
    /// </summary>
    public static class CommandSuggestor
    {
        /// <summary>
        /// Returns up to three names from <paramref name="names"/> closest
        /// to the typed name by edit distance, skipping candidates farther
        /// than half the typed length so garbage input suggests nothing.
        /// Ties break alphabetically.
        /// </summary>
        public static IReadOnlyList<string> SuggestCloseNames(string typed, IEnumerable<string> names)
        {
            var result = new List<ScoredName>();
            if (string.IsNullOrEmpty(typed))
                return Array.Empty<string>();

            int limit = typed.Length / 2;
            foreach (var name in names)
            {
                int dist = EditDistance(typed, name);
                if (dist <= limit)
                    result.Add(new ScoredName(name, dist));
            }

            result.Sort((a, b) => a.Distance != b.Distance
                ? a.Distance.CompareTo(b.Distance)
                : string.CompareOrdinal(a.Name, b.Name));
            return result.Take(3).Select(r => r.Name).ToList();
        }

        /// <summary>
        /// Levenshtein edit distance between two strings.
        /// </summary>
        public static int EditDistance(string a, string b)
        {
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
                prev[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                }
                var swap = prev;
                prev = curr;
                curr = swap;
            }
            return prev[b.Length];
        }

        private readonly struct ScoredName
        {
            public ScoredName(string name, int distance)
            {
                Name = name;
                Distance = distance;
            }

            public string Name { get; }
            public int Distance { get; }
        }
    }
}
