using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    public class DocSaveHandler : DocumentCommandBase
    {
        public override string CommandName => "doc_save";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (doc.IsLinked)
                return CommandResponse.Error(cmd.TaskId, "Cannot save a linked document.").ToJson();

            if (doc.IsModifiable)
                return CommandResponse.Error(cmd.TaskId, "Cannot save while there are uncommitted transactions.").ToJson();

            if (string.IsNullOrEmpty(doc.PathName))
                return CommandResponse.Error(cmd.TaskId, "Document has never been saved. Use 'doc_save_as' instead.").ToJson();

            if (doc.IsReadOnlyFile)
                return CommandResponse.Error(cmd.TaskId, "Document file is read-only. Use 'doc_save_as' to save to a different location.").ToJson();

            bool compact = parameters.ContainsKey("compact");
            int? previewViewId = HandlerUtilities.GetIntOrNull(parameters, "preview_view_id");

            try
            {
                if (compact || previewViewId is not null)
                {
                    var saveOptions = new SaveOptions();
                    saveOptions.Compact = compact;

                    if (previewViewId is not null)
                        saveOptions.PreviewViewId = new ElementId(previewViewId.Value);

                    doc.Save(saveOptions);
                }
                else
                {
                    doc.Save();
                }

                var data = new
                {
                    title = doc.Title,
                    path = doc.PathName,
                    is_modified = doc.IsModified
                };

                return CommandResponse.Success(cmd.TaskId, data, "Document saved successfully.").ToJson();
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
