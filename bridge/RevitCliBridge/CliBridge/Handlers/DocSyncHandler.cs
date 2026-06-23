using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    public class DocSyncHandler : DocumentCommandBase
    {
        public override string CommandName => "doc_sync";
        public override string Description => "Synchronizes the active document with the central model";
        public override string Category => "Document";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "comment", Type = "string", Required = false, Description = "Synchronize comment" },
            new CommandParamSchema { Name = "compact", Type = "bool", Required = false, Description = "Compact the central model" },
            new CommandParamSchema { Name = "save_local_before", Type = "bool", Required = false, Description = "Save local file before synchronizing", Default = true },
            new CommandParamSchema { Name = "save_local_after", Type = "bool", Required = false, Description = "Save local file after synchronizing", Default = true },
            new CommandParamSchema { Name = "relinquish", Type = "bool", Required = false, Description = "Relinquish borrowed elements after sync" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"doc_sync\", \"parameters\": {} }",
            "{ \"command\": \"doc_sync\", \"parameters\": { \"comment\": \"Updated walls\", \"relinquish\": true } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (!doc.IsWorkshared)
                return CommandResponse.Error(cmd.TaskId, "Document is not workshared. Synchronize with central requires a workshared document.").ToJson();

            if (doc.IsModifiable)
                return CommandResponse.Error(cmd.TaskId, "Cannot synchronize while there are uncommitted transactions.").ToJson();

            string? comment = HandlerUtilities.GetStringOrNull(parameters, "comment");
            bool relinquish = parameters.ContainsKey("relinquish");

            bool saveLocal = true;
            if (parameters.TryGetValue("save_local", out var saveLocalObj) && saveLocalObj is not null)
            {
                try { saveLocal = Convert.ToBoolean(saveLocalObj); }
                catch { }
            }

            bool waitForLock = true;
            if (parameters.TryGetValue("wait_for_lock", out var waitObj) && waitObj is not null)
            {
                try { waitForLock = Convert.ToBoolean(waitObj); }
                catch { }
            }

            try
            {
                var transactOptions = new TransactWithCentralOptions();
                if (!waitForLock)
                    transactOptions.SetLockCallback(new NoWaitLockCallback());

                var syncOptions = new SynchronizeWithCentralOptions();
                syncOptions.SaveLocalAfter = saveLocal;

                if (comment is not null)
                    syncOptions.Comment = comment;

                if (relinquish)
                {
                    var relinquishOptions = new RelinquishOptions(true);
                    syncOptions.SetRelinquishOptions(relinquishOptions);
                }
                else
                {
                    var relinquishOptions = new RelinquishOptions(false);
                    syncOptions.SetRelinquishOptions(relinquishOptions);
                }

                doc.SynchronizeWithCentral(transactOptions, syncOptions);

                var data = new
                {
                    title = doc.Title,
                    path = doc.PathName,
                    is_modified = doc.IsModified,
                    comment = comment ?? "",
                    relinquished = relinquish,
                    saved_local = saveLocal
                };

                return CommandResponse.Success(cmd.TaskId, data, "Synchronized with central successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Cannot synchronize: {ex.Message}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.RevitServerCommunicationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Central model communication error: {ex.Message}").ToJson();
            }
            catch (Exception ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Failed to synchronize with central: {ex.Message}", ex.ToString()).ToJson();
            }
        }

        private class NoWaitLockCallback : ICentralLockedCallback
        {
            public bool ShouldWaitForLockAvailability() => false;
        }
    }
}
