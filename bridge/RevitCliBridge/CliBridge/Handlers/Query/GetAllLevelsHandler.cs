using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class GetAllLevelsHandler : DocumentCommandBase
    {
        public override string CommandName => "get_levels";
        public override string Description => "Retrieves all levels in the active document";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_levels\", \"parameters\": {} }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            var collector = new FilteredElementCollector(doc).OfClass(typeof(Level));

            var levels = collector
                .Cast<Level>()
                .Select(l => new
                {
                    id = l.Id.IntegerValue,
                    name = l.Name,
                    elevation = l.Elevation
                })
                .ToList();

            var result = new { count = levels.Count, levels = levels };
            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {levels.Count} levels.").ToJson();
        }
    }
}