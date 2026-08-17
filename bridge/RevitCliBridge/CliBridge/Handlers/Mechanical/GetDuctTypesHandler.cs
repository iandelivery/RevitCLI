using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Mechanical
{
    /// <summary>
    /// Lists all duct types (DuctType) available in the active document.
    /// Read-only command — useful for discovering the <c>duct_type_id</c>
    /// to pass to <c>create_duct</c>. Includes the shape (round /
    /// rectangular / oval) for each type.
    /// </summary>
    public class GetDuctTypesHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_duct_types";
        public override string Description => "Lists all duct types available in the active document";
        public override string Category => "Query";
        public override string[] Aliases => new[] { "duct_types", "list_duct_types" };

        protected override string ItemsProperty => "types";
        protected override string SuccessMessage(int count) => $"Retrieved {count} duct type(s).";

        protected override CommandParamSchema[] BaseParameters => System.Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_duct_types\", \"parameters\": {} }",
            "{ \"command\": \"get_duct_types\", \"parameters\": { \"limit\": 50 } }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DuctType))
                .Cast<DuctType>()
                .OrderBy(t => t.Id.IntegerValue)
                .Select(t => (object)new
                {
                    type_id = t.Id.IntegerValue,
                    name = t.Name,
                    shape = t.Shape.ToString().ToLowerInvariant()
                });
        }
    }
}
