using System;
using System.Collections.Generic;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Resolves user-supplied command names to registered handler names.
    /// Pure logic — no Revit or filesystem dependencies, fully unit-testable.
    /// </summary>
    /// <remarks>
    /// Supports three resolution strategies, in order:
    /// 1. Exact match — the input is already a registered command name.
    /// 2. Domain path — "elements.walls.create" tries progressively shorter
    ///    suffixes ("walls.create", then "create") to find a match.
    /// 3. Underscore reversal — "wall_create" → "create_wall" for the common
    ///    verb_noun → noun_verb aliasing pattern.
    ///
    /// Extracted from <c>CommandRouter.ResolveCommandName</c> so the routing
    /// rules can be tested without loading the executing assembly.
    /// </remarks>
    public static class CommandNameResolver
    {
        /// <summary>
        /// Resolves <paramref name="input"/> against the set of registered
        /// command names. Returns the resolved name (which may equal
        /// <paramref name="input"/> if no rule applied) — callers should
        /// check for existence in their handler map.
        /// </summary>
        /// <param name="input">The raw command name from the request.</param>
        /// <param name="registeredNames">All known command names (primary + alias).</param>
        public static string Resolve(string input, ICollection<string> registeredNames)
        {
            if (input is null) return input ?? string.Empty;
            if (registeredNames is null || registeredNames.Count == 0) return input;
            if (registeredNames.Contains(input)) return input;

            // Domain path: try progressively shorter suffixes.
            // "elements.walls.create" → "walls.create" → "create"
            if (input.Contains("."))
            {
                var parts = input.Split('.');
                for (int i = 1; i < parts.Length; i++)
                {
                    var candidate = string.Join(".", parts, i, parts.Length - i);
                    if (registeredNames.Contains(candidate)) return candidate;
                }

                // Try last segment only.
                var lastSegment = parts[parts.Length - 1];
                if (registeredNames.Contains(lastSegment)) return lastSegment;
            }

            // Underscore reversal: "wall_create" → "create_wall" (two-segment only).
            if (input.Contains("_"))
            {
                var parts = input.Split('_');
                if (parts.Length == 2)
                {
                    var reversed = $"{parts[1]}_{parts[0]}";
                    if (registeredNames.Contains(reversed)) return reversed;
                }
            }

            return input;
        }
    }
}
