using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;

namespace RevitCliBridge.Handlers
{
    public class DocOpenHandler : DocumentCommandBase
    {
        public override string CommandName => "doc_open";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            string? path = HandlerUtilities.GetStringOrNull(parameters, "path");
            if (string.IsNullOrEmpty(path))
                return CommandResponse.Error(cmd.TaskId, "Missing 'path' parameter.").ToJson();

            if (!File.Exists(path))
                return CommandResponse.Error(cmd.TaskId, $"File not found: {path}").ToJson();

            string ext = Path.GetExtension(path).ToLower();
            if (ext != ".rvt" && ext != ".rte" && ext != ".rfa")
                return CommandResponse.Error(cmd.TaskId, $"Unsupported file type: {ext}. Expected .rvt, .rte, or .rfa.").ToJson();

            bool detach = parameters.ContainsKey("detach");
            bool audit = parameters.ContainsKey("audit");

            var activeDoc = app.ActiveUIDocument?.Document;
            if (activeDoc is not null && activeDoc.IsModifiable)
                return CommandResponse.Error(cmd.TaskId, "Cannot open document while the active document has uncommitted transactions.").ToJson();

            try
            {
                UIDocument uiDoc;

                if (detach || audit)
                {
                    var modelPath = new FilePath(path);
                    var openOptions = new OpenOptions();

                    if (detach)
                        openOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;

                    if (audit)
                        openOptions.Audit = true;

                    uiDoc = app.OpenAndActivateDocument(modelPath, openOptions, false);
                }
                else
                {
                    uiDoc = app.OpenAndActivateDocument(path);
                }

                if (uiDoc is null || uiDoc.Document is null)
                    return CommandResponse.Error(cmd.TaskId, "Failed to open document.").ToJson();

                var data = new
                {
                    title = doc.Title,
                    path = doc.PathName,
                    is_family = doc.IsFamilyDocument,
                    is_workshared = doc.IsWorkshared,
                    is_readonly = doc.IsReadOnly,
                    is_modified = doc.IsModified
                };

                return CommandResponse.Success(cmd.TaskId, data, $"Document opened: {doc.Title}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Cannot open document: {ex.Message}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.FileArgumentNotFoundException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"File not found: {ex.Message}").ToJson();
            }
            catch (Exception ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Failed to open document: {ex.Message}", ex.ToString()).ToJson();
            }
        }
    }
}
