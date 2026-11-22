using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Modification
{
    public class MirrorElementHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["z"] = UnitKinds.Length
            };

        public override string CommandName => "mirror_element";
        public override string Description => "Mirrors an element across a plane defined by origin and normal";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Element ID to mirror" },
            new CommandParamSchema { Name = "normal_x", Type = "double", Required = false, Description = "Mirror plane normal X" },
            new CommandParamSchema { Name = "normal_y", Type = "double", Required = false, Description = "Mirror plane normal Y" },
            new CommandParamSchema { Name = "normal_z", Type = "double", Required = false, Description = "Mirror plane normal Z" },
            new CommandParamSchema { Name = "origin_x", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Mirror plane origin X (request length unit, default mm; defaults to element location)" },
            new CommandParamSchema { Name = "origin_y", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Mirror plane origin Y (request length unit, default mm; defaults to element location)" },
            new CommandParamSchema { Name = "origin_z", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Mirror plane origin Z (request length unit, default mm; defaults to 0)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"mirror_element\", \"parameters\": { \"element_id\": 12345, \"normal_x\": 1, \"normal_y\": 0, \"normal_z\": 0 } }",
            "{ \"command\": \"mirror_element\", \"parameters\": { \"element_id\": 12345, \"normal_x\": 0, \"normal_y\": 1, \"normal_z\": 0, \"origin_x\": 5000, \"origin_y\": 0 } }"
        };

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

            using (var t = new DryRunTransaction(doc, "CLI Mirror Element", cmd.DryRun))
            {
                t.ConfigureFailureHandling();

                ElementTransformUtils.MirrorElement(doc, element.Id, plane);

                t.Commit();

                var result = new
                {
                    element_id = elementId.Value,
                    plane_origin = new
                    {
                        x = origin.X,
                        y = origin.Y,
                        z = origin.Z
                    },
                    plane_normal = new
                    {
                        i = normal.X,
                        j = normal.Y,
                        k = normal.Z
                    }
                };

                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Element mirrored successfully.", t.ResolvedFailures).ToJson();
            }
        }

        private XYZ GetMirrorOrigin(Element element, double? originX, double? originY, double? originZ)
        {
            if (originX is not null && originY is not null)
            {
                return new XYZ(originX.Value, originY.Value, originZ ?? 0);
            }

            if (element.Location is LocationPoint point)
                return point.Point;

            if (element.Location is LocationCurve curve)
                return curve.Curve.GetEndPoint(0);

            return XYZ.Zero;
        }
    }
}
