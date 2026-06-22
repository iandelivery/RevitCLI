using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class RotateElementHandler : DocumentCommandBase
    {
        public override string CommandName => "rotate_element";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? elementId = HandlerUtilities.GetIntOrNull(parameters, "element_id");
            double? axisX = HandlerUtilities.GetDoubleOrNull(parameters, "axis_x");
            double? axisY = HandlerUtilities.GetDoubleOrNull(parameters, "axis_y");
            double? axisZ = HandlerUtilities.GetDoubleOrNull(parameters, "axis_z");
            double? angleDegrees = HandlerUtilities.GetDoubleOrNull(parameters, "angle");

            if (elementId is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: element_id.").ToJson();

            if (angleDegrees is null)
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
            var angleRadians = angleDegrees.Value * Math.PI / 180.0;

            using (Transaction t = new Transaction(doc, "CLI Rotate Element"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                ElementTransformUtils.RotateElement(doc, element.Id, axis, angleRadians);

                t.Commit();

                var result = new
                {
                    element_id = elementId.Value,
                    angle_degrees = angleDegrees.Value,
                    axis_origin = new
                    {
                        x = axisOrigin.X.FeetToMillimeter(),
                        y = axisOrigin.Y.FeetToMillimeter(),
                        z = axisOrigin.Z.FeetToMillimeter()
                    },
                    axis_direction = new
                    {
                        x = axisDirection.X,
                        y = axisDirection.Y,
                        z = axisDirection.Z
                    }
                };

                return CommandResponse.Success(cmd.TaskId, result, "Element rotated successfully.").ToJson();
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
