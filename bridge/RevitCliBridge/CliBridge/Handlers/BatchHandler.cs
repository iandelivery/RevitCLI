using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class BatchHandler : DocumentCommandBase
    {
        public override string CommandName => "batch";
        public override string Description => "Executes multiple commands in a single transaction group with optional rollback on error";
        public override string Category => "System";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema
            {
                Name = "operations", Type = "object", Required = true, Description = "Array of operations to execute",
                Properties = new[]
                {
                    new CommandParamSchema { Name = "command", Type = "string", Required = true, Description = "Command name" },
                    new CommandParamSchema { Name = "parameters", Type = "object", Required = false, Description = "Command parameters" }
                }
            },
            new CommandParamSchema { Name = "name", Type = "string", Required = false, Description = "Transaction group name", Default = "CLI Batch" },
            new CommandParamSchema { Name = "rollback_on_error", Type = "bool", Required = false, Description = "Roll back all operations if any fails", Default = true },
            new CommandParamSchema { Name = "assimilate", Type = "bool", Required = false, Description = "Assimilate transaction group after commit", Default = true }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"batch\", \"parameters\": { \"operations\": [ { \"command\": \"create_wall\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"end_x\": 5000, \"end_y\": 0, \"level_id\": 3001 } }, { \"command\": \"create_wall\", \"parameters\": { \"start_x\": 5000, \"start_y\": 0, \"end_x\": 5000, \"end_y\": 4000, \"level_id\": 3001 } } ] } }",
            "{ \"command\": \"batch\", \"parameters\": { \"operations\": [ { \"command\": \"create_wall\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"end_x\": 5000, \"end_y\": 0, \"level_id\": 3001 } }, { \"command\": \"set_parameter\", \"parameters\": { \"element_id\": \"$0\", \"parameter_name\": \"Comments\", \"value\": \"New wall\" } } } ] }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            var operations = ParseOperations(parameters);
            if (operations is null || operations.Count == 0)
                return CommandResponse.Error(cmd.TaskId, "No operations provided. Use 'operations' array.").ToJson();

            string name = HandlerUtilities.GetStringOrNull(parameters, "name") ?? "CLI Batch";

            bool rollbackOnError = true;
            if (parameters.TryGetValue("rollback_on_error", out var roe) && roe is not null)
            {
                try { rollbackOnError = Convert.ToBoolean(roe); }
                catch { }
            }

            bool assimilate = true;
            if (parameters.TryGetValue("assimilate", out var assim) && assim is not null)
            {
                try { assimilate = Convert.ToBoolean(assim); }
                catch { }
            }

            var results = new List<BatchOperationResult>();
            var resultCache = new Dictionary<int, JToken>();

            using (var tg = new TransactionGroup(doc, name))
            {
                tg.Start();

                for (int i = 0; i < operations.Count; i++)
                {
                    var op = operations[i];

                    var resolvedParams = ResolveReferences(op.Parameters, resultCache);

                    var subCmd = new QueuedCommand
                    {
                        TaskId = $"{cmd.TaskId}_{i}",
                        Command = op.Command,
                        Parameters = resolvedParams
                    };

                    string resultJson;
                    try
                    {
                        TaskRegistry.SetProgress(cmd.TaskId,
                            (int)((i / (double)operations.Count) * 100),
                            $"Executing {op.Command} ({i + 1}/{operations.Count})...");

                        resultJson = CommandRouter.Execute(app, subCmd);
                    }
                    catch (Exception ex)
                    {
                        resultJson = CommandResponse.Error(subCmd.TaskId,
                            $"Command '{op.Command}' threw exception: {ex.Message}",
                            ex.ToString()).ToJson();
                    }

                    var resultToken = JToken.Parse(resultJson);
                    resultCache[i] = resultToken;

                    var status = resultToken["status"]?.ToString() ?? "error";
                    var message = resultToken["message"]?.ToString() ?? "";
                    var data = resultToken["data"];

                    var opResult = new BatchOperationResult
                    {
                        Index = i,
                        Command = op.Command,
                        Status = status,
                        Message = message,
                        Data = data
                    };
                    results.Add(opResult);

                    if (status == "error" && rollbackOnError)
                    {
                        tg.RollBack();

                        return CommandResponse.Error(cmd.TaskId,
                            $"Operation {i} ('{op.Command}') failed. Transaction group '{name}' rolled back. " +
                            $"All {i} previously committed operations have been undone.",
                            JsonConvert.SerializeObject(new
                            {
                                name,
                                rollback = true,
                                failed_at_index = i,
                                failed_command = op.Command,
                                failed_message = message,
                                rolled_back_count = i,
                                results
                            })).ToJson();
                    }
                }

                if (assimilate)
                    tg.Assimilate();
                else
                    tg.Commit();
            }

            int successCount = results.Count(r => r.Status == "success");
            int failCount = results.Count - successCount;

            var batchResult = new
            {
                name,
                total = operations.Count,
                succeeded = successCount,
                failed = failCount,
                rollback = false,
                results
            };

            return CommandResponse.Success(cmd.TaskId, batchResult,
                $"Batch '{name}' completed: {successCount}/{operations.Count} succeeded.").ToJson();
        }

        private List<BatchOperation> ParseOperations(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("operations", out var opsVal) || opsVal is null)
                return new List<BatchOperation>();

            var opsArray = opsVal as JArray;
            if (opsArray is null || opsArray.Count == 0)
                return new List<BatchOperation>();

            var operations = new List<BatchOperation>();
            foreach (var opToken in opsArray)
            {
                var opObj = opToken as JObject;
                if (opObj is null) continue;

                string? command = opObj["command"]?.ToString();
                if (string.IsNullOrEmpty(command)) continue;

                var opParams = opObj["parameters"] as JObject;
                operations.Add(new BatchOperation
                {
                    Command = command!,
                    Parameters = opParams is not null
                        ? opParams.ToObject<Dictionary<string, object>>()
                        : new Dictionary<string, object>()
                });
            }

            return operations;
        }

        private Dictionary<string, object>? ResolveReferences(
            Dictionary<string, object>? parameters,
            Dictionary<int, JToken> resultCache)
        {
            if (parameters is null || resultCache.Count == 0)
                return parameters;

            var resolved = new Dictionary<string, object>();
            foreach (var kvp in parameters)
            {
                var value = ResolveValue(kvp.Value, resultCache);
                resolved[kvp.Key] = value;
            }
            return resolved;
        }

        private object ResolveValue(object value, Dictionary<int, JToken> resultCache)
        {
            if (value is string str && str.StartsWith("$"))
            {
                var refResult = ResolveReference(str, resultCache);
                if (refResult is not null)
                    return refResult;
            }

            if (value is JObject jobj)
            {
                var dict = new Dictionary<string, object>();
                foreach (var prop in jobj.Properties())
                {
                    dict[prop.Name] = ResolveValue(prop.Value, resultCache);
                }
                return dict;
            }

            if (value is JArray jarr)
            {
                var list = new List<object>();
                foreach (var item in jarr)
                {
                    list.Add(ResolveValue(item, resultCache));
                }
                return list;
            }

            if (value is JValue jval)
                return jval.Value ?? value;

            return value;
        }

        private object? ResolveReference(string reference, Dictionary<int, JToken> resultCache)
        {
            if (!reference.StartsWith("$"))
                return null;

            string path = reference.Substring(1);

            int dotIndex = path.IndexOf('.');
            int index;
            string? remainingPath = null;

            if (dotIndex >= 0)
            {
                if (!int.TryParse(path.Substring(0, dotIndex), out index))
                    return null;
                remainingPath = path.Substring(dotIndex + 1);
            }
            else
            {
                if (!int.TryParse(path, out index))
                    return null;
            }

            if (!resultCache.TryGetValue(index, out var resultToken))
                return null;

            if (remainingPath is null)
            {
                var elementId = resultToken.SelectToken("data.element_id");
                if (elementId is not null)
                    return elementId.ToObject<object>();

                var id = resultToken.SelectToken("data.id");
                if (id is not null)
                    return id.ToObject<object>();

                return null;
            }

            var targetToken = resultToken.SelectToken(remainingPath)
                ?? resultToken.SelectToken($"data.{remainingPath}");

            return targetToken?.ToObject<object>();
        }

        private class BatchOperation
        {
            public string Command { get; set; } = string.Empty;
            public Dictionary<string, object>? Parameters { get; set; }
        }

        private class BatchOperationResult
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("command")]
            public string Command { get; set; } = string.Empty;

            [JsonProperty("status")]
            public string Status { get; set; } = string.Empty;

            [JsonProperty("message")]
            public string Message { get; set; } = string.Empty;

            [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
            public JToken? Data { get; set; }
        }
    }
}
