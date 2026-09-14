using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Piping
{
    /// <summary>
    /// Queries pipe instances in the active document. Supports optional
    /// filtering by level, system type, and/or diameter (approximate match).
    /// Returns paginated results with coordinates in millimeters.
    /// </summary>
    public class GetPipesHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_pipes";
        public override string Description => "Retrieves pipe instances, optionally filtered by level, system type, or diameter";
        public override string Category => "Query";
        public override string[] Aliases => new[] { "pipes", "list_pipes" };

        protected override string ItemsProperty => "pipes";
        protected override string SuccessMessage(int count) => $"Retrieved {count} pipe(s).";

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
                Description = "Filter by piping system type element ID"
            },
            new CommandParamSchema
            {
                Name = "diameter_mm",
                Type = "double",
                Required = false,
                Description = "Filter by closer nominal diameter (exact value match within a small tolerance)"
            }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_pipes\", \"parameters\": {} }",
            "{ \"command\": \"get_pipes\", \"parameters\": { \"level_id\": 3001 } }",
            "{ \"command\": \"get_pipes\", \"parameters\": { \"diameter_mm\": 100, \"limit\": 100 } }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");
            int? systemTypeId = HandlerUtilities.GetIntOrNull(parameters, "system_type_id");
            double? diameterMm = HandlerUtilities.GetDoubleOrNull(parameters, "diameter_mm");

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .Cast<Pipe>();

            if (levelId.HasValue && levelId.Value > 0)
                collector = collector.Where(p => p.LevelId.IntegerValue == levelId.Value);

            if (systemTypeId.HasValue && systemTypeId.Value > 0)
            {
                collector = collector.Where(p =>
                {
                    var stId = p.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();
                    return stId is not null && stId.IntegerValue == systemTypeId.Value;
                });
            }

            if (diameterMm.HasValue && diameterMm.Value > 0)
            {
                double targetMm = diameterMm.Value;
                collector = collector.Where(p =>
                {
                    double dmm = p.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble().FeetToMillimeter() ?? 0.0;
                    return System.Math.Abs(dmm - targetMm) < 1.0;
                });
            }

            return collector
                .OrderBy(p => p.Id.IntegerValue)
                .Select(p => PipeUtils.Default.Snapshot(p, doc))
                .ToList();
        }
    }
}