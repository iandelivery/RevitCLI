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
        /// Default version suffix used when a request omits an explicit
        /// <c>@version</c> pin. The router resolves unversioned requests to
        /// the highest available version of the matching command.
        /// </summary>
        public const string DefaultVersion = "v1";

        /// <summary>
        /// Splits a possibly-versioned command string into its base name and
        /// version tag. <c>"create_wall@v2"</c> → ("create_wall", "v2").
        /// <c>"create_wall"</c> → ("create_wall", null). The base name may
        /// itself contain dots (domain path) — only the last <c>@segment</c>
        /// is treated as a version tag.
        /// </summary>
        public static (string baseName, string? version) SplitVersion(string input)
        {
            if (input is null) return (string.Empty, null);
            var at = input.LastIndexOf('@');
            if (at < 0) return (input, null);
            return (input.Substring(0, at), input.Substring(at + 1));
        }

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

            // Strip @version suffix before applying domain/underscore rules,
            // then re-attach it so the caller looks up the versioned key.
            var (baseName, version) = SplitVersion(input);
            if (version is not null)
            {
                // Apply domain path and underscore rules on the base name,
                // checking for "{candidate}@{version}" in registered names.
                // We can't recurse on Resolve() because registered names carry
                // @version suffixes that would prevent base-name matching.
                if (baseName.Contains("."))
                {
                    var parts = baseName.Split('.');
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var candidate = string.Join(".", parts, i, parts.Length - i);
                        if (VersionedOrBareMatch(registeredNames, candidate, version))
                            return $"{candidate}@{version}";
                    }
                    var lastSegment = parts[parts.Length - 1];
                    if (VersionedOrBareMatch(registeredNames, lastSegment, version))
                        return $"{lastSegment}@{version}";
                }

                if (baseName.Contains("_"))
                {
                    var parts = baseName.Split('_');
                    if (parts.Length == 2)
                    {
                        var reversed = $"{parts[1]}_{parts[0]}";
                        if (VersionedOrBareMatch(registeredNames, reversed, version))
                            return $"{reversed}@{version}";
                    }
                }

                return $"{baseName}@{version}";
            }

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

        /// <summary>
        /// Checks whether <paramref name="candidate"/> (bare) or
        /// <c>{candidate}@{version}</c> exists in <paramref name="registeredNames"/>.
        /// The bare match handles default-version entries registered without
        /// a suffix; the versioned match handles explicit version pins.
        /// </summary>
        private static bool VersionedOrBareMatch(
            ICollection<string> registeredNames, string candidate, string version)
        {
            return registeredNames.Contains(candidate)
                || registeredNames.Contains($"{candidate}@{version}");
        }
    }
}
