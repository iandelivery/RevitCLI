using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
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

        /// <summary>
        /// Parses a raw `link` parameter value into an instance-id selector
        /// or an exact-name selector. Delegates the pure string logic to
        /// <see cref="LinkSelectorParser.ResolveLinkSelector"/> (linked into
        /// the test project; see LinkSelectorParser.cs).
        /// </summary>
        public static LinkSelector? ResolveLinkSelector(string? raw)
        {
            return LinkSelectorParser.ResolveLinkSelector(raw);
        }

        /// <summary>
        /// Resolves the target document for a query: the host document when
        /// no `link` parameter is present, otherwise the linked document of
        /// the named or id-addressed RevitLinkInstance. Returns null with
        /// linkLabel set when the link exists but is not loaded.
        /// </summary>
        public static Document? TryResolveTargetDocument(
            Document hostDoc,
            Dictionary<string, object> parameters,
            out string? linkLabel)
        {
            linkLabel = null;
            var selector = ResolveLinkSelector(GetStringOrNull(parameters, "link"));
            if (selector is null)
                return hostDoc;

            var instances = new FilteredElementCollector(hostDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            RevitLinkInstance? match = selector.InstanceId.HasValue
                ? instances.FirstOrDefault(l => l.Id.IntegerValue == selector.InstanceId.Value)
                : instances.FirstOrDefault(l =>
                    string.Equals(l.Name, selector.Name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                return null;

            var linkDoc = match.GetLinkDocument();
            if (linkDoc is null)
            {
                linkLabel = $"{match.Name} (id {match.Id.IntegerValue})";
                return null;
            }

            linkLabel = $"{match.Name} (id {match.Id.IntegerValue})";
            return linkDoc;
        }
    }
}
