using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;

namespace RevitCliBridge.Handlers
{
    public class DocSaveAsHandler : DocumentCommandBase
    {
        public override string CommandName => "doc_save_as";
        public override string Description => "Saves the active document to a new file path";
        public override string Category => "Document";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "path", Type = "string", Required = true, Description = "File path to save as" },
            new CommandParamSchema { Name = "overwrite", Type = "bool", Required = false, Description = "Overwrite existing file" },
            new CommandParamSchema { Name = "compact", Type = "bool", Required = false, Description = "Compact the file on save" },
            new CommandParamSchema { Name = "save_as_central", Type = "bool", Required = false, Description = "Save as central model (workshared only)" },
            new CommandParamSchema { Name = "preview_view_id", Type = "int", Required = false, Description = "View element ID for preview thumbnail" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"doc_save_as\", \"parameters\": { \"path\": \"C:\\\\Projects\\\\Building_v2.rvt\" } }",
            "{ \"command\": \"doc_save_as\", \"parameters\": { \"path\": \"C:\\\\Projects\\\\Building_v2.rvt\", \"overwrite\": true, \"compact\": true } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (doc.IsLinked)
                return CommandResponse.Error(cmd.TaskId, "Cannot save a linked document.").ToJson();

            if (doc.IsModifiable)
                return CommandResponse.Error(cmd.TaskId, "Cannot save while there are uncommitted transactions.").ToJson();

            string? path = HandlerUtilities.GetStringOrNull(parameters, "path");
            if (string.IsNullOrEmpty(path))
                return CommandResponse.Error(cmd.TaskId, "Missing 'path' parameter.").ToJson();

            bool overwrite = parameters.ContainsKey("overwrite");
            bool compact = parameters.ContainsKey("compact");
            bool saveAsCentral = parameters.ContainsKey("save_as_central");
            int? previewViewId = HandlerUtilities.GetIntOrNull(parameters, "preview_view_id");

            if (!overwrite && File.Exists(path))
                return CommandResponse.Error(cmd.TaskId, $"File already exists: {path}. Use --overwrite to replace.").ToJson();

            try
            {
                var saveAsOptions = new SaveAsOptions();
                saveAsOptions.OverwriteExistingFile = overwrite;
                saveAsOptions.Compact = compact;

                if (previewViewId is not null)
                    saveAsOptions.PreviewViewId = new ElementId(previewViewId.Value);

                if (saveAsCentral && doc.IsWorkshared)
                {
                    var worksharingOptions = new WorksharingSaveAsOptions();
                    worksharingOptions.SaveAsCentral = true;
                    saveAsOptions.SetWorksharingOptions(worksharingOptions);
                }

                doc.SaveAs(path, saveAsOptions);

                var data = new
                {
                    title = doc.Title,
                    path = doc.PathName,
                    is_modified = doc.IsModified,
                    is_workshared = doc.IsWorkshared
                };

                return CommandResponse.Success(cmd.TaskId, data, $"Document saved as: {path}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Cannot save document: {ex.Message}").ToJson();
            }
            catch (Exception ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Failed to save document: {ex.Message}", ex.ToString()).ToJson();
            }
        }
    }
}
