using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers.Views
{
    public class MoveViewportHandler : DocumentCommandBase
    {
        public override string CommandName => "move_viewport";
        public override string Description => "Moves viewports on a sheet to new box centers";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;
        public override string[] Aliases => new[] { "viewport_move" };

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema
            {
                Name = "moves", Type = "object", Required = true, Description = "Array of moves",
                Properties = new[]
                {
                    new CommandParamSchema { Name = "viewport_id", Type = "int", Required = true, Description = "Viewport element ID (list with get_sheet_viewports)" },
                    new CommandParamSchema { Name = "x", Type = "double", Required = true, Description = "New box center X in mm on sheet" },
                    new CommandParamSchema { Name = "y", Type = "double", Required = true, Description = "New box center Y in mm on sheet" }
                }
            }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"move_viewport\", \"parameters\": { \"moves\": [ { \"viewport_id\": 12345, \"x\": 300, \"y\": 200 } ] } }",
            "{ \"command\": \"move_viewport\", \"parameters\": { \"moves\": [ { \"viewport_id\": 12345, \"x\": 300, \"y\": 200 }, { \"viewport_id\": 12346, \"x\": 600, \"y\": 200 } ] } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            if (!SheetPlacementParser.TryParse(
                    parameters.TryGetValue("moves", out var rawMoves) ? rawMoves : null,
                    "moves", "viewport_id", true, out var moves, out var parseError))
                return CommandResponse.Error(cmd.TaskId, parseError!).ToJson();

            var viewports = new Viewport[moves.Count];
            for (var i = 0; i < moves.Count; i++)
            {
                var viewport = doc.GetElement(new ElementId((int)moves[i].Id)) as Viewport;
                if (viewport is null)
                    return CommandResponse.Error(cmd.TaskId, $"Element with ID {moves[i].Id} is not a viewport.").ToJson();
                if (viewport.SheetId == ElementId.InvalidElementId)
                    return CommandResponse.Error(cmd.TaskId, $"Viewport {moves[i].Id} is not on a sheet.").ToJson();
                viewports[i] = viewport;
            }

            List<string> resolvedFailures;

            using (var t = new DryRunTransaction(doc, "CLI Move Viewports", cmd.DryRun))
            {
                t.ConfigureFailureHandling();

                try
                {
                    for (var i = 0; i < moves.Count; i++)
                    {
                        var move = moves[i];
                        viewports[i].SetBoxCenter(new XYZ(
                            move.X!.Value.MillimeterToFeet(),
                            move.Y!.Value.MillimeterToFeet(),
                            0));
                    }

                    doc.Regenerate();
                }
                catch (Exception ex)
                {
                    return CommandResponse.Error(cmd.TaskId, $"Failed to move viewport: {ex.Message}").ToJson();
                }

                t.Commit();

                resolvedFailures = t.ResolvedFailures;
            }

            var moved = new List<object>();
            foreach (var viewport in viewports)
            {
                var center = viewport.GetBoxCenter();
                moved.Add(new
                {
                    viewport_id = viewport.Id.IntegerValue,
                    x = center.X.FeetToMillimeter(),
                    y = center.Y.FeetToMillimeter()
                });
            }

            var result = new
            {
                count = moved.Count,
                moved
            };

            return CommandResponse.SuccessWithResolvedFailures(
                cmd.TaskId, result, $"Moved {moved.Count} viewports.", resolvedFailures).ToJson();
        }
    }
}
