using Autodesk.Revit.UI;
using System;
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

        public string GetName() => "AI_CLI_Command_Handler";

        public void Cancel() { }
    }
}
