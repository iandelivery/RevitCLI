using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Query
{
    /// <summary>
    /// Lists every placed RevitLinkInstance in the active document with
    /// load state, source path, and placement transform. The entry point
    /// for linked-document queries: callers pick a link from this list and
    /// pass its id (or exact name) as the `link` parameter of the query
    /// commands that accept one.
    /// </summary>
    public class GetLinksHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_links";
        public override string Description => "Lists linked RVT documents placed in the active document (id, load state, path, transform)";
        public override string Category => "Query";

        protected override string ItemsProperty => "links";
        protected override string SuccessMessage(int count) => $"Retrieved {count} links.";

        protected override CommandParamSchema[] BaseParameters => new[]
        {
            new CommandParamSchema
            {
                Name = "include_nested",
                Type = "bool",
                Required = false,
                Description = "Also list links placed inside linked documents (listing only; query commands cannot yet target nested links)"
            }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_links\", \"parameters\": {} }",
            "{ \"command\": \"get_links\", \"parameters\": { \"include_nested\": true } }"
        };

        /// <summary>
        /// Strongly-typed row so ordering by id works without dynamic
        /// dispatch; serialized to the anonymous shape at the end.
        /// </summary>
        private sealed class LinkRow
        {
            public int Id;
            public string Name = "";
            public int TypeId;
            public bool Loaded;
            public string? Path;
            public bool IsNested;
            public string? ParentLink;
            public double OriginX, OriginY, OriginZ;
        }

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            bool includeNested = HandlerUtilities.GetStringOrNull(parameters, "include_nested") is not null
                && string.Equals(HandlerUtilities.GetStringOrNull(parameters, "include_nested"), "true", StringComparison.OrdinalIgnoreCase);

            var rows = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Select(l => Describe(l))
                .OrderBy(r => r.Id)
                .ToList();

            if (includeNested)
            {
                // Nested links are RevitLinkInstance elements living in the
                // parent link's own document. One level deep per loaded
                // parent.
                var nested = new List<LinkRow>();
                foreach (var parent in new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>())
                {
                    var parentDoc = HandlerUtilities.GetLinkDocumentSafe(doc, parent);
                    if (parentDoc is null) continue;

                    nested.AddRange(new FilteredElementCollector(parentDoc)
                        .OfClass(typeof(RevitLinkInstance))
                        .Cast<RevitLinkInstance>()
                        .Select(child => Describe(child, parent.Name)));
                }

                rows.AddRange(nested.OrderBy(r => r.Id));
            }

            return rows.Select(r => (object)new
            {
                id = r.Id,
                name = r.Name,
                type_id = r.TypeId,
                loaded = r.Loaded,
                path = r.Path,
                is_nested = r.IsNested,
                parent_link = r.ParentLink,
                origin = new { x = r.OriginX, y = r.OriginY, z = r.OriginZ }
            });
        }

        private static LinkRow Describe(RevitLinkInstance instance, string? parentName = null)
        {
            string? path = null;
            bool isNested;
            try
            {
                var type = instance.Document.GetElement(instance.GetTypeId()) as RevitLinkType;
                isNested = type?.IsNestedLink ?? false;
                var absPath = type?.GetExternalFileReference()?.GetAbsolutePath();
                if (absPath is not null)
                    path = ModelPathUtils.ConvertModelPathToUserVisiblePath(absPath);
            }
            catch
            {
                isNested = false;
            }

            var transform = instance.GetTotalTransform();

            return new LinkRow
            {
                Id = instance.Id.IntegerValue,
                Name = instance.Name,
                TypeId = instance.GetTypeId().IntegerValue,
                Loaded = HandlerUtilities.GetLinkDocumentSafe(instance.Document, instance) is not null,
                Path = path,
                IsNested = isNested,
                ParentLink = parentName,
                OriginX = transform.Origin.X,
                OriginY = transform.Origin.Y,
                OriginZ = transform.Origin.Z
            };
        }
    }
}
