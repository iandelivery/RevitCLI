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
            // Store the UIApplication reference for endpoints that need it
            // (e.g. /api/llms.txt which accesses the active document).
            CliBridgeStateManager.SetUIApplication(app);

            while (TaskRegistry.CommandQueue.TryDequeue(out var queuedCommand))
            {
                TaskRegistry.SetRunning(queuedCommand.TaskId);

                string resultJson = "{}";
                try
                {
                    resultJson = CommandRouter.Execute(app, queuedCommand);
                    // Handlers report failures by returning an error response
                    // (top-level status "error") without throwing. Only mark
                    // the task Completed for success responses so the SSE
                    // stream and task polling emit "failed" (clients rely on
                    // this for non-zero exit codes).
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

            // Clean up old completed tasks to prevent unbounded memory growth.
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
