using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Modification
{
    public class RotateElementHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["angle"] = UnitKinds.Angle,
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["z"] = UnitKinds.Length
            };

        public override string CommandName => "rotate_element";
        public override string Description => "Rotates an element around an axis by a specified angle";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Element ID to rotate" },
            new CommandParamSchema { Name = "angle", Type = "double", Required = true, UnitKind = UnitKinds.Angle, Description = "Rotation angle (request angle unit, default degrees)" },
            new CommandParamSchema { Name = "axis_x", Type = "double", Required = false, Description = "Axis direction X (default 0)", Default = 0 },
            new CommandParamSchema { Name = "axis_y", Type = "double", Required = false, Description = "Axis direction Y (default 0)", Default = 0 },
            new CommandParamSchema { Name = "axis_z", Type = "double", Required = false, Description = "Axis direction Z (default 1 = vertical)", Default = 1 }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"rotate_element\", \"parameters\": { \"element_id\": 12345, \"angle\": 90 } }",
            "{ \"command\": \"rotate_element\", \"parameters\": { \"element_id\": 12345, \"angle\": 45, \"axis_z\": 1 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? elementId = HandlerUtilities.GetIntOrNull(parameters, "element_id");
            double? axisX = HandlerUtilities.GetDoubleOrNull(parameters, "axis_x");
            double? axisY = HandlerUtilities.GetDoubleOrNull(parameters, "axis_y");
            double? axisZ = HandlerUtilities.GetDoubleOrNull(parameters, "axis_z");
            double? angle = HandlerUtilities.GetDoubleOrNull(parameters, "angle");

            if (elementId is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: element_id.").ToJson();

            if (angle is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: angle.").ToJson();

            var element = doc.GetElement(new ElementId(elementId.Value));
            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId.Value} not found.").ToJson();

            var axisOrigin = GetElementBasePoint(element);
            if (axisOrigin is null)
                return CommandResponse.Error(cmd.TaskId, "Could not determine rotation axis origin from element location.").ToJson();

            var axisDirection = new XYZ(axisX ?? 0, axisY ?? 0, axisZ ?? 1);
            if (axisDirection.IsZeroLength())
                axisDirection = XYZ.BasisZ;

            var axis = Line.CreateUnbound(axisOrigin, axisDirection);

            using (var t = new DryRunTransaction(doc, "CLI Rotate Element", cmd.DryRun))
            {
                t.ConfigureFailureHandling();

                ElementTransformUtils.RotateElement(doc, element.Id, axis, angle.Value);

                t.Commit();

                var result = new
                {
                    element_id = elementId.Value,
                    angle = angle.Value,
                    axis_origin = new
                    {
                        x = axisOrigin.X,
                        y = axisOrigin.Y,
                        z = axisOrigin.Z
                    },
                    axis_direction = new
                    {
                        i = axisDirection.X,
                        j = axisDirection.Y,
                        k = axisDirection.Z
                    }
                };

                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Element rotated successfully.", t.ResolvedFailures).ToJson();
            }
        }

        private XYZ? GetElementBasePoint(Element element)
        {
            if (element.Location is LocationPoint point)
                return point.Point;

            if (element.Location is LocationCurve curve)
                return curve.Curve.GetEndPoint(0);

            return null;
        }
    }
}
