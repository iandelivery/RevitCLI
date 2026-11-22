using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Modification
{
    public class MoveElementHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["translation_x"] = UnitKinds.Length,
                ["translation_y"] = UnitKinds.Length,
                ["translation_z"] = UnitKinds.Length,
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["z"] = UnitKinds.Length
            };

        public override string CommandName => "move_element";
        public override string Description => "Moves an element by a translation vector (dx, dy, dz)";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Element ID to move" },
            new CommandParamSchema { Name = "dx", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Translation X (request length unit, default mm)" },
            new CommandParamSchema { Name = "dy", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Translation Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "dz", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Translation Z (request length unit, default mm)", Default = 0 }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"move_element\", \"parameters\": { \"element_id\": 12345, \"dx\": 1000, \"dy\": 0 } }",
            "{ \"command\": \"move_element\", \"parameters\": { \"element_id\": 12345, \"dx\": 500, \"dy\": 500, \"dz\": 200 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<MoveElementParams>(cmd, out var error);
            if (p is null) return error!;

            var element = doc.GetElement(new ElementId(p.ElementId));
            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {p.ElementId} not found.").ToJson();

            using (var t = new DryRunTransaction(doc, "CLI Move Element", cmd.DryRun))
            {
                t.ConfigureFailureHandling();

                var translation = new XYZ(p.Dx, p.Dy, p.Dz);
                ElementTransformUtils.MoveElement(doc, element.Id, translation);

                t.Commit();

                var location = GetElementLocation(element);

                var result = new
                {
                    element_id = p.ElementId,
                    translation_x = p.Dx,
                    translation_y = p.Dy,
                    translation_z = p.Dz,
                    new_location = location
                };

                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Element moved successfully.", t.ResolvedFailures).ToJson();
            }
        }

        private object? GetElementLocation(Element element)
        {
            if (element.Location is LocationPoint point)
            {
                var p = point.Point;
                return new
                {
                    x = p.X,
                    y = p.Y,
                    z = p.Z
                };
            }
            else if (element.Location is LocationCurve curve)
            {
                var c = curve.Curve;
                var p1 = c.GetEndPoint(0);
                var p2 = c.GetEndPoint(1);
                return new
                {
                    start = new { x = p1.X, y = p1.Y, z = p1.Z },
                    end = new { x = p2.X, y = p2.Y, z = p2.Z }
                };
            }
            return null;
        }
    }

    /// <summary>
    /// Typed parameter bag for <see cref="MoveElementHandler"/>.
    /// </summary>
    public class MoveElementParams
    {
        [Param("element_id", Required = true)]
        public int ElementId { get; set; }

        [Param("dx", Required = true)]
        public double Dx { get; set; }

        [Param("dy", Required = true)]
        public double Dy { get; set; }

        [Param("dz", Default = 0.0)]
        public double Dz { get; set; }
    }
}
