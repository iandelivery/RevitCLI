using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Electrical
{
    /// <summary>
    /// Queries cable tray instances in the active document. Supports optional
    /// filtering by level and/or type, and returns paginated results with
    /// coordinates in millimeters.
    /// </summary>
    public class GetCableTraysHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_cable_trays";
        public override string Description => "Retrieves cable tray instances, optionally filtered by level or type";
        public override string Category => "Query";
        public override string[] Aliases => new[] { "cabletrays", "list_cable_trays" };

        protected override string ItemsProperty => "cable_trays";
        protected override string SuccessMessage(int count) => $"Retrieved {count} cable tray(s).";

        protected override CommandParamSchema[] BaseParameters => new[]
        {
            new CommandParamSchema
            {
                Name = "level_id",
                Type = "int",
                Required = false,
                Description = "Filter by level element ID"
            },
            new CommandParamSchema
            {
                Name = "type_id",
                Type = "int",
                Required = false,
                Description = "Filter by cable tray type element ID"
            }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_cable_trays\", \"parameters\": {} }",
            "{ \"command\": \"get_cable_trays\", \"parameters\": { \"level_id\": 3001 } }",
            "{ \"command\": \"get_cable_trays\", \"parameters\": { \"type_id\": 12345, \"limit\": 100 } }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");
            int? typeId = HandlerUtilities.GetIntOrNull(parameters, "type_id");

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_CableTray)
                .WhereElementIsNotElementType()
                .Cast<CableTray>();

            if (levelId.HasValue && levelId.Value > 0)
                collector = collector.Where(ct => ct.LevelId.IntegerValue == levelId.Value);

            if (typeId.HasValue && typeId.Value > 0)
                collector = collector.Where(ct => ct.GetTypeId().IntegerValue == typeId.Value);

            // OrderBy(id) ensures stable pagination.
            return collector
                .OrderBy(ct => ct.Id.IntegerValue)
                .Select(ct => CableTrayUtils.Snapshot(ct, doc))
                .ToList();
        }
    }
}
