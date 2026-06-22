using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class MirrorElementHandler : DocumentCommandBase
    {
        public override string CommandName => "mirror_element";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? elementId = HandlerUtilities.GetIntOrNull(parameters, "element_id");
            double? originX = HandlerUtilities.GetDoubleOrNull(parameters, "origin_x");
            double? originY = HandlerUtilities.GetDoubleOrNull(parameters, "origin_y");
            double? originZ = HandlerUtilities.GetDoubleOrNull(parameters, "origin_z");
            double? normalX = HandlerUtilities.GetDoubleOrNull(parameters, "normal_x");
            double? normalY = HandlerUtilities.GetDoubleOrNull(parameters, "normal_y");
            double? normalZ = HandlerUtilities.GetDoubleOrNull(parameters, "normal_z");

            if (elementId is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: element_id.").ToJson();

            if (normalX is null && normalY is null && normalZ is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: mirror plane normal (normal_x, normal_y, normal_z).").ToJson();

            var element = doc.GetElement(new ElementId(elementId.Value));
            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId.Value} not found.").ToJson();

            var origin = GetMirrorOrigin(element, originX, originY, originZ);
            var normal = new XYZ(normalX ?? 0, normalY ?? 0, normalZ ?? 0).Normalize();

            var plane = Plane.CreateByNormalAndOrigin(normal, origin);

            using (Transaction t = new Transaction(doc, "CLI Mirror Element"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                ElementTransformUtils.MirrorElement(doc, element.Id, plane);

                t.Commit();

                var result = new
                {
                    element_id = elementId.Value,
                    plane_origin = new
                    {
                        x = origin.X.FeetToMillimeter(),
                        y = origin.Y.FeetToMillimeter(),
                        z = origin.Z.FeetToMillimeter()
                    },
                    plane_normal = new
                    {
                        x = normal.X,
                        y = normal.Y,
                        z = normal.Z
                    }
                };

                return CommandResponse.Success(cmd.TaskId, result, "Element mirrored successfully.").ToJson();
            }
        }

        private XYZ GetMirrorOrigin(Element element, double? originX, double? originY, double? originZ)
        {
            if (originX is not null && originY is not null)
            {
                return new XYZ(originX.Value.MillimeterToFeet(), originY.Value.MillimeterToFeet(), (originZ ?? 0).MillimeterToFeet());
            }

            if (element.Location is LocationPoint point)
                return point.Point;

            if (element.Location is LocationCurve curve)
                return curve.Curve.GetEndPoint(0);

            return XYZ.Zero;
        }
    }
}
