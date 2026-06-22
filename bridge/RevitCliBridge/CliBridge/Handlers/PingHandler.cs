using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class PingHandler : BridgeCommandBase
    {
        public override string CommandName => "ping";
        protected override string Execute(UIApplication app, QueuedCommand cmd)
        {
            return CommandResponse.Success(cmd.TaskId,
                new { revit_version = app.Application.VersionNumber, message = "pong" }, "pong").ToJson();
        }
    }
}