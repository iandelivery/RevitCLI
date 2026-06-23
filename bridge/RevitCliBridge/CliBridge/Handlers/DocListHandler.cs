using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class DocListHandler : BridgeCommandBase
    {
        public override string CommandName => "doc_list";
        public override string Description => "Lists all open documents in Revit";
        public override string Category => "Document";

        public override CommandParamSchema[] Parameters => Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"doc_list\", \"parameters\": {} }"
        };

        protected override string Execute(UIApplication app, QueuedCommand cmd)
        {
            var activeDoc = app.ActiveUIDocument?.Document;
            var allDocs = app.Application.Documents
                .Cast<Document>()
                .ToList();

            var docList = allDocs.Select(doc =>
            {
                var entry = new
                {
                    title = doc.Title,
                    path = doc.PathName,
                    is_active = doc.Equals(activeDoc),
                    is_family = doc.IsFamilyDocument,
                    is_modified = doc.IsModified,
                    is_workshared = doc.IsWorkshared,
                    is_readonly = doc.IsReadOnly,
                    is_readonly_file = doc.IsReadOnlyFile,
                    is_linked = doc.IsLinked,
                    is_detached = doc.IsWorkshared && doc.IsDetached,
                    file_size = GetFileSize(doc.PathName),
                    last_write_time = GetLastWriteTime(doc.PathName)
                };
                return entry;
            }).ToList();

            var data = new
            {
                total_count = docList.Count,
                active_count = docList.Count(d => d.is_active),
                modified_count = docList.Count(d => d.is_modified),
                documents = docList
            };

            return CommandResponse.Success(cmd.TaskId, data,
                $"{docList.Count} document(s) open, {docList.Count(d => d.is_modified)} modified.").ToJson();
        }

        private long? GetFileSize(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var fi = new FileInfo(path);
                return fi.Exists ? fi.Length : (long?)null;
            }
            catch { return null; }
        }

        private string? GetLastWriteTime(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var fi = new FileInfo(path);
                return fi.Exists ? fi.LastWriteTime.ToString("o") : null;
            }
            catch { return null; }
        }
    }
}
