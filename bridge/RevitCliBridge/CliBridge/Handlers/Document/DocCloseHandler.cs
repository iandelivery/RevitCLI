using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class DocCloseHandler : DocumentCommandBase
    {
        public override string CommandName => "doc_close";
        public override string Description => "Closes documents (cannot close the active document via API)";
        public override string Category => "Document";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "all", Type = "bool", Required = false, Description = "Close all non-active documents" },
            new CommandParamSchema { Name = "save", Type = "bool", Required = false, Description = "Save documents before closing" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"doc_close\", \"parameters\": { \"all\": true } }",
            "{ \"command\": \"doc_close\", \"parameters\": { \"all\": true, \"save\": true } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            bool save = parameters.ContainsKey("save");
            bool closeAll = parameters.ContainsKey("all");

            var activeDoc = app.ActiveUIDocument?.Document;
            var allDocs = app.Application.Documents
                .Cast<Document>()
                .ToList();

            if (closeAll)
            {
                return CloseAllDocuments(cmd.TaskId, app, activeDoc, allDocs, save);
            }

            if (activeDoc is null)
                return CommandResponse.Error(cmd.TaskId, "No active document to close.").ToJson();

            return CommandResponse.Error(cmd.TaskId,
                "Cannot close the active document via API. Use 'doc_close --all' to close other documents, or close the active document manually in Revit.").ToJson();
        }

        private string CloseAllDocuments(string taskId, UIApplication app, Document? activeDoc, List<Document> allDocs, bool save)
        {
            var closedDocs = new List<object>();
            int failedCount = 0;

            foreach (var doc in allDocs)
            {
                if (doc.Equals(activeDoc))
                    continue;

                if (doc.IsLinked)
                    continue;

                try
                {
                    string docTitle = doc.Title;
                    string docPath = doc.PathName;
                    bool wasModified = doc.IsModified;

                    bool closed = doc.Close(save);

                    if (closed)
                    {
                        closedDocs.Add(new { title = docTitle, path = docPath, was_modified = wasModified });
                    }
                    else
                    {
                        failedCount++;
                    }
                }
                catch (Exception)
                {
                    failedCount++;
                }
            }

            var data = new
            {
                closed_count = closedDocs.Count,
                failed_count = failedCount,
                skipped_active = activeDoc is not null,
                closed_documents = closedDocs
            };

            string message = closedDocs.Count > 0
                ? $"Closed {closedDocs.Count} document(s)."
                : "No documents were closed.";

            if (failedCount > 0)
                message += $" {failedCount} document(s) could not be closed.";

            return CommandResponse.Success(taskId, data, message).ToJson();
        }
    }
}
