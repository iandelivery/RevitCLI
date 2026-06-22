using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    public class UndoHandler : DocumentCommandBase
    {
        public override string CommandName => "undo";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? steps = HandlerUtilities.GetIntOrNull(parameters, "steps") ?? 1;

            if (steps.Value < 1 || steps.Value > 20)
                return CommandResponse.Error(cmd.TaskId, "Steps must be between 1 and 20.").ToJson();

            int successCount = 0;

            try
            {
                for (int i = 0; i < steps.Value; i++)
                {
                    var undoCommandId = RevitCommandId.LookupPostableCommandId(PostableCommand.Undo);
                    if (app.CanPostCommand(undoCommandId))
                    {
                        app.PostCommand(undoCommandId);
                        successCount++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Undo failed: {ex.Message}").ToJson();
            }

            var result = new
            {
                steps_requested = steps.Value,
                steps_executed = successCount
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Undo executed: {successCount} step(s).").ToJson();
        }
    }
}
