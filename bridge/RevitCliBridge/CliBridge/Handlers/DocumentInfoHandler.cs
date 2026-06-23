using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System.Collections.Generic;
using System.IO;

namespace RevitCliBridge.Handlers
{
    public class DocumentInfoHandler : DocumentCommandBase
    {
        public override string CommandName => "document_info";
        public override string Description => "Retrieves metadata about the active document";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"document_info\", \"parameters\": {} }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            string documentType = doc.IsFamilyDocument ? "Family"
                : (doc.PathName?.EndsWith(".rte") ?? false) ? "Template"
                : "Project";

            long? fileSize = null;
            string? lastWriteTime = null;
            if (!string.IsNullOrEmpty(doc.PathName))
            {
                try
                {
                    var fi = new FileInfo(doc.PathName);
                    if (fi.Exists)
                    {
                        fileSize = fi.Length;
                        lastWriteTime = fi.LastWriteTime.ToString("o");
                    }
                }
                catch { }
            }

            var info = new
            {
                title = doc.Title,
                path = doc.PathName,
                document_type = documentType,
                is_family = doc.IsFamilyDocument,
                is_modifiable = doc.IsModifiable,
                is_modified = doc.IsModified,
                is_workshared = doc.IsWorkshared,
                is_readonly = doc.IsReadOnly,
                is_readonly_file = doc.IsReadOnlyFile,
                is_detached = doc.IsWorkshared && doc.IsDetached,
                is_linked = doc.IsLinked,
                file_size = fileSize,
                last_write_time = lastWriteTime,
                application_version = doc.Application.VersionNumber
            };

            return CommandResponse.Success(cmd.TaskId, info, "Document info retrieved.").ToJson();
        }
    }
}
