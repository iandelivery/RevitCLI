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

        public static double? GetDoubleOrNull(Dictionary<string, object> parameters, string key)
        {
            if (parameters.TryGetValue(key, out var val) && val is not null)
            {
                try { return Convert.ToDouble(val); }
                catch { return null; }
            }
            return null;
        }

        public static int? GetIntOrNull(Dictionary<string, object> parameters, string key)
        {
            if (parameters.TryGetValue(key, out var val) && val is not null)
            {
                try { return Convert.ToInt32(val); }
                catch { return null; }
            }
            return null;
        }

        public static string? GetStringOrNull(Dictionary<string, object> parameters, string key)
        {
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
    }
}
