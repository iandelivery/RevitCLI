using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Mechanical
{
    /// <summary>
    /// Queries duct instances in the active document. Supports optional
    /// filtering by level, system type, and/or shape. Returns paginated
    /// results with lengths and coordinates in the request length unit
    /// (default mm).
    /// </summary>
    public class GetDuctsHandler : PaginatedQueryHandler<object>
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["z"] = UnitKinds.Length,
                ["diameter"] = UnitKinds.Length,
                ["width"] = UnitKinds.Length,
                ["height"] = UnitKinds.Length,
                ["length"] = UnitKinds.Length
            };

        public override string CommandName => "get_ducts";
        public override string Description => "Retrieves duct instances, optionally filtered by level, system type, or shape";
        public override string Category => "Query";
        public override string[] Aliases => new[] { "ducts", "list_ducts" };
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        protected override string ItemsProperty => "ducts";
        protected override string SuccessMessage(int count) => $"Retrieved {count} duct(s).";

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
                Name = "system_type_id",
                Type = "int",
                Required = false,
                Description = "Filter by mechanical system type element ID"
            },
            new CommandParamSchema
            {
                Name = "shape",
                Type = "string",
                Required = false,
                Description = "Filter by shape: round, rectangular, or oval"
            }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_ducts\", \"parameters\": {} }",
            "{ \"command\": \"get_ducts\", \"parameters\": { \"level_id\": 3001 } }",
            "{ \"command\": \"get_ducts\", \"parameters\": { \"shape\": \"round\", \"limit\": 100 } }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");
            int? systemTypeId = HandlerUtilities.GetIntOrNull(parameters, "system_type_id");
            string? shapeStr = HandlerUtilities.GetStringOrNull(parameters, "shape");

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType()
                .Cast<Duct>();

            if (levelId.HasValue && levelId.Value > 0)
                collector = collector.Where(d => ElementIdCompat.ValueOf(d.LevelId) == levelId.Value);

            if (systemTypeId.HasValue && systemTypeId.Value > 0)
            {
                collector = collector.Where(d =>
                {
                    var stId = d.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)?.AsElementId();
                    return stId is not null && ElementIdCompat.ValueOf(stId) == systemTypeId.Value;
                });
            }

            if (!string.IsNullOrEmpty(shapeStr))
            {
                var shapeFilter = shapeStr!.ToLowerInvariant();
                collector = collector.Where(d =>
                {
                    var shape = d.DuctType?.Shape ?? ConnectorProfileType.Round;
                    return shape.ToString().ToLowerInvariant() == shapeFilter;
                });
            }

            return collector
                .OrderBy(d => ElementIdCompat.ValueOf(d.Id))
                .Select(d => DuctUtils.Default.Snapshot(d, doc))
                .ToList();
        }
    }
}
