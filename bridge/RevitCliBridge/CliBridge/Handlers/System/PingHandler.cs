using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;

namespace RevitCliBridge.Handlers
{
    public class PingHandler : BridgeCommandBase
    {
        public override string CommandName => "ping";
        public override string Description => "Tests bridge connectivity and returns version info";
        public override string Category => "System";

        public override CommandParamSchema[] Parameters => Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"ping\", \"parameters\": {} }"
        };

        protected override string Execute(UIApplication app, QueuedCommand cmd)
        {
            return CommandResponse.Success(cmd.TaskId,
                new { revit_version = app.Application.VersionNumber, message = "pong" }, "pong").ToJson();
        }
    }
}