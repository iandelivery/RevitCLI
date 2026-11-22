using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Query
{
    public class GetElementLocationsHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["z"] = UnitKinds.Length,
                ["rotation"] = UnitKinds.Angle,
                ["length"] = UnitKinds.Length
            };

        public override string CommandName => "get_element_locations";
        public override string Description => "Resolves a representative location for each element (location point, curve midpoint, or bounding box center) in the request length unit (default mm); point-based elements include their rotation";
        public override string Category => "Query";
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_ids", Type = "int[]", Required = true, Description = "Element IDs to resolve locations for" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_element_locations\", \"parameters\": { \"element_ids\": [123, 456] } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var elementIds = HandlerUtilities.GetIntArrayOrNull(parameters, "element_ids");
            if (elementIds is null || elementIds.Length == 0)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: element_ids.").ToJson();

            var elements = new List<object>();

            foreach (var id in elementIds.Distinct())
            {
                var element = doc.GetElement(new ElementId(id));
                if (element is null)
                {
                    elements.Add(new { id, found = false });
                    continue;
                }

                double x, y, z;
                string locationType;
                double? rotation = null;
                double? length = null;

                switch (element.Location)
                {
                    case LocationPoint point:
                        x = point.Point.X;
                        y = point.Point.Y;
                        z = point.Point.Z;
                        locationType = "point";
                        rotation = point.Rotation;
                        break;

                    case LocationCurve curve:
                        var mid = curve.Curve.Evaluate(0.5, true);
                        x = mid.X;
                        y = mid.Y;
                        z = mid.Z;
                        locationType = "curve_midpoint";
                        length = curve.Curve.Length;
                        break;

                    default:
                        var box = element.get_BoundingBox(null);
                        if (box is null)
                        {
                            elements.Add(new { id, found = false });
                            continue;
                        }
                        x = (box.Min.X + box.Max.X) / 2.0;
                        y = (box.Min.Y + box.Max.Y) / 2.0;
                        z = (box.Min.Z + box.Max.Z) / 2.0;
                        locationType = "bbox_center";
                        break;
                }

                elements.Add(new
                {
                    id,
                    found = true,
                    location_type = locationType,
                    x,
                    y,
                    z,
                    rotation,
                    length
                });
            }

            var result = new { count = elements.Count, elements };
            return CommandResponse.Success(cmd.TaskId, result, "Element locations resolved.").ToJson();
        }
    }
}
