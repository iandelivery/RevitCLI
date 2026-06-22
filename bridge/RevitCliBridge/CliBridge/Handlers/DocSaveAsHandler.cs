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
