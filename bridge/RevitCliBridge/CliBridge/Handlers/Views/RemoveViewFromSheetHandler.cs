using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Views
{
    public class RemoveViewFromSheetHandler : DocumentCommandBase
    {
        public override string CommandName => "remove_view_from_sheet";
        public override string Description => "Removes views from a sheet by deleting their viewports; the views stay in the project";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;
        public override string[] Aliases => new[] { "delete_viewport" };

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "sheet_id", Type = "int", Required = true, Description = "Sheet element ID" },
            new CommandParamSchema { Name = "view_ids", Type = "int[]", Required = true, Description = "View element IDs to remove from the sheet" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"remove_view_from_sheet\", \"parameters\": { \"sheet_id\": 12345, \"view_ids\": [678] } }",
            "{ \"command\": \"remove_view_from_sheet\", \"parameters\": { \"sheet_id\": 12345, \"view_ids\": [678, 789] } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            int? sheetId = HandlerUtilities.GetIntOrNull(parameters, "sheet_id");
            if (!sheetId.HasValue)
                return CommandResponse.Error(cmd.TaskId, "Missing 'sheet_id' parameter.").ToJson();

            var sheet = SheetViewportHelpers.ResolveSheet(doc, sheetId.Value, out var sheetReason);
            if (sheet is null)
                return CommandResponse.Error(cmd.TaskId, sheetReason!).ToJson();

            var viewIds = HandlerUtilities.GetIntArrayOrNull(parameters, "view_ids");
            if (viewIds is null || viewIds.Length == 0)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: view_ids.").ToJson();

            var viewportsByViewId = SheetViewportHelpers.GetViewports(doc, sheet)
                .ToDictionary(vp => vp.ViewId.IntegerValue);

            var results = new List<object>();
            var toDelete = new List<ElementId>();

            foreach (var viewId in viewIds.Distinct())
            {
                if (doc.GetElement(new ElementId(viewId)) is null)
                {
                    results.Add(new { view_id = viewId, removed = false, reason = $"View with ID {viewId} not found." });
                    continue;
                }

                if (!viewportsByViewId.TryGetValue(viewId, out var viewport))
                {
                    results.Add(new { view_id = viewId, removed = false, reason = $"View {viewId} is not placed on sheet {sheet.SheetNumber}." });
                    continue;
                }

                toDelete.Add(viewport.Id);
                results.Add(new { view_id = viewId, removed = true, viewport_id = viewport.Id.IntegerValue });
            }

            var removedCount = toDelete.Count;
            List<string> resolvedFailures;

            if (toDelete.Count > 0)
            {
                using (var t = new DryRunTransaction(doc, "CLI Remove Views from Sheet", cmd.DryRun))
                {
                    t.ConfigureFailureHandling();

                    foreach (var viewportId in toDelete)
                        doc.Delete(viewportId);

                    t.Commit();

                    resolvedFailures = t.ResolvedFailures;
                }
            }
            else
            {
                resolvedFailures = new List<string>();
            }

            var result = new
            {
                sheet_id = sheetId.Value,
                removed_count = removedCount,
                results
            };

            return CommandResponse.SuccessWithResolvedFailures(
                cmd.TaskId, result, $"Removed {removedCount} views from sheet {sheet.SheetNumber}. The views stay in the project.", resolvedFailures).ToJson();
        }
    }
}
