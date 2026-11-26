using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers.General
{
    public class UndoHandler : DocumentCommandBase
    {
        public override string CommandName => "undo";
        public override string Description => "Undoes recent transactions in the document";
        public override string Category => "Modify";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "count", Type = "int", Required = false, Description = "Number of undo commands to post (1 to 20; Revit accepts one queued undo at a time, so repeat the command)", Default = 1 }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"undo\", \"parameters\": {} }",
            "{ \"command\": \"undo\", \"parameters\": { \"count\": 3 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? steps = HandlerUtilities.GetIntOrNull(parameters, "count")
                ?? HandlerUtilities.GetIntOrNull(parameters, "steps")
                ?? 1;

            if (steps.Value < 1 || steps.Value > 20)
                return CommandResponse.Error(cmd.TaskId, "Steps must be between 1 and 20.").ToJson();

            int successCount = 0;

            for (int i = 0; i < steps.Value; i++)
            {
                var undoCommandId = RevitCommandId.LookupPostableCommandId(PostableCommand.Undo);
                if (!app.CanPostCommand(undoCommandId))
                    break;
                try
                {
                    app.PostCommand(undoCommandId);
                    successCount++;
                }
                catch
                {
                    break;
                }
            }

            var result = new
            {
                steps_requested = steps.Value,
                steps_posted = successCount
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Undo posted: {successCount} of {steps.Value} step(s). Revit accepts one queued undo at a time; repeat the command to undo more steps.").ToJson();
        }
    }
}
