using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class MoveElementHandler : DocumentCommandBase
    {
        public override string CommandName => "move_element";
        public override string Description => "Moves an element by a translation vector (dx, dy, dz)";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Element ID to move" },
            new CommandParamSchema { Name = "dx", Type = "double", Required = true, Description = "Translation X in millimeters" },
            new CommandParamSchema { Name = "dy", Type = "double", Required = true, Description = "Translation Y in millimeters" },
            new CommandParamSchema { Name = "dz", Type = "double", Required = false, Description = "Translation Z in millimeters", Default = 0 }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"move_element\", \"parameters\": { \"element_id\": 12345, \"dx\": 1000, \"dy\": 0 } }",
            "{ \"command\": \"move_element\", \"parameters\": { \"element_id\": 12345, \"dx\": 500, \"dy\": 500, \"dz\": 200 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? elementId = HandlerUtilities.GetIntOrNull(parameters, "element_id");
            double? dx = HandlerUtilities.GetDoubleOrNull(parameters, "dx");
            double? dy = HandlerUtilities.GetDoubleOrNull(parameters, "dy");
            double? dz = HandlerUtilities.GetDoubleOrNull(parameters, "dz");

            if (elementId is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: element_id.").ToJson();

            if (dx is null || dy is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameters: dx and dy.").ToJson();

            var element = doc.GetElement(new ElementId(elementId.Value));
            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId.Value} not found.").ToJson();

            using (Transaction t = new Transaction(doc, "CLI Move Element"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                var translation = new XYZ(dx.Value.MillimeterToFeet(), dy.Value.MillimeterToFeet(), (dz ?? 0).MillimeterToFeet());
                ElementTransformUtils.MoveElement(doc, element.Id, translation);

                t.Commit();

                var location = GetElementLocation(element);

                var result = new
                {
                    element_id = elementId.Value,
                    translation_x = dx.Value,
                    translation_y = dy.Value,
                    translation_z = dz ?? 0,
                    new_location = location
                };

                return CommandResponse.Success(cmd.TaskId, result, "Element moved successfully.").ToJson();
            }
        }

        private object? GetElementLocation(Element element)
        {
            if (element.Location is LocationPoint point)
            {
                var p = point.Point;
                return new
                {
                    x = p.X.FeetToMillimeter(),
                    y = p.Y.FeetToMillimeter(),
                    z = p.Z.FeetToMillimeter()
                };
            }
            else if (element.Location is LocationCurve curve)
            {
                var c = curve.Curve;
                var p1 = c.GetEndPoint(0);
                var p2 = c.GetEndPoint(1);
                return new
                {
                    start = new { x = p1.X.FeetToMillimeter(), y = p1.Y.FeetToMillimeter(), z = p1.Z.FeetToMillimeter() },
                    end = new { x = p2.X.FeetToMillimeter(), y = p2.Y.FeetToMillimeter(), z = p2.Z.FeetToMillimeter() }
                };
            }
            return null;
        }
    }
}
