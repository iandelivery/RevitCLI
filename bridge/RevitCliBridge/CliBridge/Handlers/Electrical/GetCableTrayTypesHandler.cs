using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Electrical
{
    /// <summary>
    /// Lists all cable tray types (CableTrayType) available in the active
    /// document. Read-only command — useful for discovering the
    /// <c>cable_tray_type_id</c> to pass to <c>create_cable_tray</c>.
    /// </summary>
    public class GetCableTrayTypesHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_cable_tray_types";
        public override string Description => "Lists all cable tray types available in the active document";
        public override string Category => "Query";
        public override string[] Aliases => new[] { "cabletray_types", "list_cable_tray_types" };

        protected override string ItemsProperty => "types";
        protected override string SuccessMessage(int count) => $"Retrieved {count} cable tray type(s).";

        protected override CommandParamSchema[] BaseParameters => System.Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_cable_tray_types\", \"parameters\": {} }",
            "{ \"command\": \"get_cable_tray_types\", \"parameters\": { \"limit\": 50 } }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(CableTrayType))
                .Cast<CableTrayType>()
                .OrderBy(t => t.Id.IntegerValue)
                .Select(t =>
                {
                    // Read default width/height from the type parameters when present.
                    double widthFt = t.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? 0.0;
                    double heightFt = t.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? 0.0;
                    return (object)new
                    {
                        type_id = t.Id.IntegerValue,
                        name = t.Name,
                        default_width_mm = widthFt.FeetToMillimeter(),
                        default_height_mm = heightFt.FeetToMillimeter()
                    };
                });
        }
    }
}
