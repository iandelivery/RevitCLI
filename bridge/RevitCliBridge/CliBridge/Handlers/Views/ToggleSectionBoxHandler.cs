using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Views
{
    public class ToggleSectionBoxHandler : DocumentCommandBase
    {
        public override string CommandName => "toggle_section_box";
        public override string Description => "Enables or disables the section box on a 3D view";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "view_id", Type = "int", Required = false, Description = "3D view element ID (defaults to the active view)" },
            new CommandParamSchema { Name = "enable", Type = "bool", Required = false, Description = "Enable or disable the section box (defaults to true)", Default = true }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"toggle_section_box\", \"parameters\": { \"view_id\": 12345 } }",
            "{ \"command\": \"toggle_section_box\", \"parameters\": { \"view_id\": 12345, \"enable\": false } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ToggleSectionBoxParams>(cmd, out var error);
            if (p is null) return error!;

            var viewOrError = SectionBoxUtilities.ResolveTargetView(app, doc, p.ViewId, cmd);
            if (viewOrError.View is null) return viewOrError.ErrorJson!;
            var view = viewOrError.View;

            using (var t = new DryRunTransaction(doc, "CLI Toggle Section Box", cmd.DryRun))
            {
                t.ConfigureFailureHandling();
                view.IsSectionBoxActive = p.Enable;
                t.Commit();
            }

            var result = new
            {
                view_id = view.Id.IntegerValue,
                view_name = view.Name,
                active = p.Enable
            };
            return CommandResponse.Success(cmd.TaskId, result,
                p.Enable ? "Section box enabled." : "Section box disabled.").ToJson();
        }
    }

    /// <summary>
    /// Typed parameter bag for <see cref="ToggleSectionBoxHandler"/>.
    /// </summary>
    public class ToggleSectionBoxParams
    {
        [Param("view_id")]
        public int? ViewId { get; set; }

        [Param("enable", Default = true)]
        public bool Enable { get; set; }
    }
}
