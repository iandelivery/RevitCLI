using Autodesk.Revit.UI;
using System;
using Newtonsoft.Json.Linq;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge
{
    public class CliCommandHandler : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            CliBridgeStateManager.SetUIApplication(app);

            while (TaskRegistry.CommandQueue.TryDequeue(out var queuedCommand))
            {
                TaskRegistry.SetRunning(queuedCommand.TaskId);

                string resultJson = "{}";
                try
                {
                    resultJson = CommandRouter.Execute(app, queuedCommand);
                    if (IsErrorResult(resultJson))
                        TaskRegistry.SetFailed(queuedCommand.TaskId, resultJson);
                    else
                        TaskRegistry.SetCompleted(queuedCommand.TaskId, resultJson);
                }
                catch (Exception ex)
                {
                    resultJson = CommandResponse.Error(
                        queuedCommand.TaskId,
                        $"Command execution failed: {ex.Message}",
                        ex.ToString()).ToJson();
                    TaskRegistry.SetFailed(queuedCommand.TaskId, resultJson);
                }
            }

            TaskRegistry.CleanupOldTasks();
        }

        /// <summary>
        /// Reports whether a handler result is a CommandResponse error
        /// (top-level "status": "error"). Non-JSON results are treated as
        /// successful — the shape is controlled by CommandResponse.ToJson.
        /// </summary>
        private static bool IsErrorResult(string resultJson)
        {
            try
            {
                return JObject.Parse(resultJson)?["status"]?.ToString() == "error";
            }
            catch
            {
                return false;
            }
        }

        public string GetName() => "AI_CLI_Command_Handler";

        public void Cancel() { }
    }
}
