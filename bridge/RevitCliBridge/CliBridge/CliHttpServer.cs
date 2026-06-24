using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RevitCliBridge.Models;

namespace RevitCliBridge
{
    public class CliHttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly int _port;
        private CancellationTokenSource _cancellationTokenSource;
        private Task? _serverTask;
        private volatile bool _isRunning;

        // Concurrency limiter to prevent unbounded task spawning.
        private static readonly SemaphoreSlim _concurrencyLimiter = new SemaphoreSlim(10, 10);

        // Identity info for the /api/identity endpoint.
        private int _revitVersion;
        private int _processId;

        public int Port => _port;
        public bool IsRunning => _isRunning;

        public CliHttpServer(int port)
        {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Set identity information for this bridge instance.
        /// Called before Start() so the /api/identity endpoint can return it.
        /// </summary>
        public void SetIdentity(int revitVersion, int processId)
        {
            _revitVersion = revitVersion;
            _processId = processId;
        }

        public void Start()
        {
            if (_isRunning) return;

            // Create a fresh CTS in case Stop() was called previously.
            _cancellationTokenSource = new CancellationTokenSource();

            _listener.Start();
            _isRunning = true;

            _serverTask = Task.Run(() => ListenLoopAsync(_cancellationTokenSource.Token));
            CliLogger.Info($"CLI HTTP server started on http://localhost:{_port}/");
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationTokenSource.Cancel();

            try
            {
                _serverTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }

            try
            {
                _listener.Stop();
            }
            catch (Exception ex)
            {
                CliLogger.Warn($"Error stopping HTTP listener: {ex.Message}");
            }

            CliLogger.Info("CLI HTTP server stopped.");
        }

        private async Task ListenLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(async () =>
                    {
                        await _concurrencyLimiter.WaitAsync(cancellationToken);
                        try
                        {
                            await HandleRequestAsync(context);
                        }
                        finally
                        {
                            _concurrencyLimiter.Release();
                        }
                    }, cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        CliLogger.Error($"HTTP listener error: {ex.Message}");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            bool responseHandled = false;
            try
            {
                var request = context.Request;
                var response = context.Response;
                var path = request.Url?.AbsolutePath ?? "";

                if (path == "/api/execute" && request.HttpMethod == "POST")
                {
                    // Check if client requests SSE mode
                    bool isSseRequested = request.Headers["Accept"]?.Contains("text/event-stream") == true;

                    if (isSseRequested)
                    {
                        await HandleSseExecuteRequestAsync(request, response);
                        // SSE handles its own response lifecycle — skip finally Close().
                        responseHandled = true;
                        return;
                    }

                    await HandleExecuteRequestAsync(request, response);
                }
                else if (path == "/api/task" && request.HttpMethod == "GET")
                {
                    await HandleTaskListRequestAsync(response);
                }
                else if (path.StartsWith("/api/task/") && request.HttpMethod == "GET")
                {
                    var taskId = path.Substring("/api/task/".Length);
                    await HandleCliTaskStatusRequestAsync(taskId, response);
                }
                else if (path == "/api/status" && request.HttpMethod == "GET")
                {
                    await HandleStatusRequestAsync(response);
                }
                else if (path == "/api/health" && request.HttpMethod == "GET")
                {
                    await HandleHealthCheckAsync(response);
                }
                else if (path == "/api/identity" && request.HttpMethod == "GET")
                {
                    await HandleIdentityRequestAsync(response);
                }
                else if (path == "/api/llms.txt" && request.HttpMethod == "GET")
                {
                    await HandleLlmsTxtRequestAsync(response);
                }
                else if (path == "/api/commands" && request.HttpMethod == "GET")
                {
                    await HandleCommandsSchemaAsync(request, response);
                }
                else if (path.StartsWith("/api/commands/") && request.HttpMethod == "GET")
                {
                    var commandName = path.Substring("/api/commands/".Length);
                    await HandleCommandSchemaAsync(commandName, response);
                }
                else
                {
                    response.StatusCode = 404;
                    await WriteJsonResponseAsync(response, new { error = "Not found" });
                }
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Error handling HTTP request: {ex.Message}");
                if (!responseHandled)
                {
                    try
                    {
                        context.Response.StatusCode = 500;
                        await WriteJsonResponseAsync(context.Response, new { error = ex.Message });
                    }
                    catch { }
                }
            }
            finally
            {
                if (!responseHandled)
                {
                    try { context.Response.Close(); } catch { }
                }
            }
        }

        // Maximum request body size (10 MB).
        private const int MaxRequestBodySize = 10 * 1024 * 1024;

        private async Task<string> ReadRequestBodyAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.ContentLength64 > MaxRequestBodySize)
            {
                response.StatusCode = 413;
                await WriteJsonResponseAsync(response, new { error = "Request body too large." });
                return string.Empty;
            }

            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private async Task HandleExecuteRequestAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            string body = await ReadRequestBodyAsync(request, response);
            if (string.IsNullOrEmpty(body) && response.StatusCode == 413) return;

            CliLogger.Info($"Received CLI request: {body}");

            RevitCommandInput? input;
            try
            {
                input = JsonConvert.DeserializeObject<RevitCommandInput>(body);
                if (input is null || string.IsNullOrEmpty(input.Command))
                {
                    response.StatusCode = 400;
                    await WriteJsonResponseAsync(response,
                        CommandResponse.Error("unknown", "Invalid request: missing 'command' field."));
                    return;
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 400;
                await WriteJsonResponseAsync(response,
                    CommandResponse.Error("unknown", $"Invalid JSON: {ex.Message}"));
                return;
            }

            if (string.IsNullOrEmpty(input.TaskId))
                input.TaskId = Guid.NewGuid().ToString();

            bool asyncMode = input.Async ?? false;

            var taskInfo = TaskRegistry.CreateTask(input.TaskId, input.Command);

            var queuedCommand = new QueuedCommand
            {
                TaskId = input.TaskId,
                Command = input.Command,
                Parameters = input.Parameters,
                DryRun = input.DryRun
            };

            // Enforce max queue size to prevent unbounded command accumulation.
            var config = CliBridgeConfigLoader.Config;
            if (TaskRegistry.CommandQueue.Count >= config.MaxCommandQueueSize)
            {
                response.StatusCode = 429;
                await WriteJsonResponseAsync(response,
                    CommandResponse.Error(input.TaskId, $"Command queue is full ({config.MaxCommandQueueSize}). Try again later."));
                return;
            }

            TaskRegistry.CommandQueue.Enqueue(queuedCommand);

            if (TaskRegistry.RevitEvent != null)
            {
                TaskRegistry.RevitEvent.Raise();
            }

            if (asyncMode)
            {
                var acceptResult = new
                {
                    task_id = input.TaskId,
                    status = "pending",
                    message = "Task submitted. Poll GET /api/task/{task_id} for status."
                };
                await WriteJsonResponseAsync(response, acceptResult);
            }
            else
            {
                int timeoutSeconds = input.TimeoutSeconds ?? CliBridgeConfigLoader.Config.TimeoutSeconds;
                var completedTask = await Task.WhenAny(taskInfo.Tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

                if (completedTask == taskInfo.Tcs.Task)
                {
                    await WriteJsonResponseAsync(response, taskInfo.Tcs.Task.Result);
                }
                else
                {
                    TaskRegistry.SetFailed(input.TaskId,
                        CommandResponse.Error(input.TaskId, "Revit API execution timed out.").ToJson());
                    await WriteJsonResponseAsync(response,
                        CommandResponse.Error(input.TaskId, "Revit API execution timed out.").ToJson());
                }
            }
        }

        /// <summary>
        /// Handle SSE mode execute request.
        /// Client triggers this mode by Accept: text/event-stream header.
        /// Server keeps the connection open and pushes real-time events until the task completes.
        /// </summary>
        private async Task HandleSseExecuteRequestAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            // 1. Parse request body
            string body = await ReadRequestBodyAsync(request, response);
            if (string.IsNullOrEmpty(body) && response.StatusCode == 413) return;

            CliLogger.Info($"Received SSE CLI request: {body}");

            RevitCommandInput input;
            try
            {
                input = JsonConvert.DeserializeObject<RevitCommandInput>(body);
                if (input == null || string.IsNullOrEmpty(input.Command))
                {
                    response.StatusCode = 400;
                    await WriteJsonResponseAsync(response,
                        CommandResponse.Error("unknown", "Invalid request: missing 'command' field."));
                    return;
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 400;
                await WriteJsonResponseAsync(response,
                    CommandResponse.Error("unknown", $"Invalid JSON: {ex.Message}"));
                return;
            }

            if (string.IsNullOrEmpty(input.TaskId))
                input.TaskId = Guid.NewGuid().ToString();

            // 2. Set SSE response headers
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");
            response.Headers.Add("X-Accel-Buffering", "no");
            response.StatusCode = 200;

            // 3. Create task and attach SSE broadcast
            var taskInfo = TaskRegistry.CreateTask(input.TaskId, input.Command);

            var sseCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
            bool clientDisconnected = false;

            taskInfo.OnSseEvent += async (eventName, dataJson) =>
            {
                if (clientDisconnected) return;
                try
                {
                    await WriteSseEventAsync(response.OutputStream, eventName, dataJson);
                }
                catch (Exception)
                {
                    clientDisconnected = true;
                    sseCts.Cancel();
                }
            };

            // 4. Send initial "accepted" event
            try
            {
                await WriteSseEventAsync(response.OutputStream, "accepted",
                    JsonConvert.SerializeObject(new { task_id = input.TaskId, status = "pending", command = input.Command }));
            }
            catch (Exception)
            {
                clientDisconnected = true;
                taskInfo.ClearSseSubscribers();
                return;
            }

            // 5. Enqueue command and wake Revit main thread up
            var queuedCommand = new QueuedCommand
            {
                TaskId = input.TaskId,
                Command = input.Command,
                Parameters = input.Parameters,
                DryRun = input.DryRun
            };
            TaskRegistry.CommandQueue.Enqueue(queuedCommand);
            TaskRegistry.RevitEvent?.Raise();

            // 6. Heartbeat mechanism: send heartbeat every 15 seconds to prevent connection from being closed by proxy/firewall
            var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(sseCts.Token);
            var heartbeatTask = Task.Run(async () =>
            {
                try
                {
                    while (!heartbeatCts.IsCancellationRequested)
                    {
                        await Task.Delay(15000, heartbeatCts.Token);
                        if (clientDisconnected) return;
                        try
                        {
                            await WriteSseEventAsync(response.OutputStream, "heartbeat", "{}");
                        }
                        catch
                        {
                            heartbeatCts.Cancel();
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }, heartbeatCts.Token);

            // 7. Keep connection alive until task completion or timeout
            int timeoutSeconds = input.TimeoutSeconds ?? CliBridgeConfigLoader.Config.TimeoutSeconds;
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), sseCts.Token);

            var completedTask = await Task.WhenAny(taskInfo.Tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                TaskRegistry.SetFailed(input.TaskId,
                    CommandResponse.Error(input.TaskId, "Revit API execution timed out").ToJson());
            }

            // 8. Send a final SSE event to ensure the client receives the
            // terminal state, even if the broadcast was missed.
            if (!clientDisconnected)
            {
                try
                {
                    var finalTask = TaskRegistry.GetTask(input.TaskId);
                    if (finalTask != null)
                    {
                        var finalEvent = finalTask.Status == CliTaskStatus.Completed ? "completed" : "failed";
                        var finalData = finalTask.ResultJson ?? "{}";
                        await WriteSseEventAsync(response.OutputStream, finalEvent, finalData);
                    }
                }
                catch { /* client may have disconnected */ }
            }

            // 9. Cleanup
            heartbeatCts.Cancel();
            try { await heartbeatTask; } catch { }

            taskInfo.ClearSseSubscribers();

            try { heartbeatCts.Dispose(); } catch { }
            try { sseCts.Dispose(); } catch { }
        }

        private async Task HandleCliTaskStatusRequestAsync(string taskId, HttpListenerResponse response)
        {
            var task = TaskRegistry.GetTask(taskId);
            if (task == null)
            {
                response.StatusCode = 404;
                await WriteJsonResponseAsync(response, new { error = $"Task '{taskId}' not found." });
                return;
            }

            var result = new Dictionary<string, object>
            {
                ["task_id"] = task.TaskId,
                ["command"] = task.Command,
                ["status"] = task.Status.ToString().ToLowerInvariant(),
                ["progress"] = task.Progress
            };

            if (task.ProgressMessage != null)
                result["progress_message"] = task.ProgressMessage;

            if (task.StartedAt.HasValue)
                result["started_at"] = task.StartedAt.Value.ToString("o");

            if (task.CompletedAt.HasValue)
                result["completed_at"] = task.CompletedAt.Value.ToString("o");

            if (task.ResultJson != null && (task.Status == CliTaskStatus.Completed || task.Status == CliTaskStatus.Failed))
            {
                try
                {
                    result["result"] = JObject.Parse(task.ResultJson);
                }
                catch
                {
                    result["result"] = task.ResultJson;
                }
            }

            await WriteJsonResponseAsync(response, result);
        }

        private async Task HandleTaskListRequestAsync(HttpListenerResponse response)
        {
            var tasks = TaskRegistry.Tasks.Values
                .OrderByDescending(t => t.CreatedAt)
                .Take(50)
                .Select(t => new
                {
                    task_id = t.TaskId,
                    command = t.Command,
                    status = t.Status.ToString().ToLowerInvariant(),
                    progress = t.Progress,
                    created_at = t.CreatedAt.ToString("o")
                })
                .ToList();

            await WriteJsonResponseAsync(response, new { tasks = tasks, count = tasks.Count });
        }

        private async Task HandleStatusRequestAsync(HttpListenerResponse response)
        {
            var status = new
            {
                server_running = _isRunning,
                total_tasks = TaskRegistry.Tasks.Count,
                pending_tasks = TaskRegistry.Tasks.Count(t => t.Value.Status == CliTaskStatus.Pending),
                running_tasks = TaskRegistry.Tasks.Count(t => t.Value.Status == CliTaskStatus.Running),
                queue_size = TaskRegistry.CommandQueue.Count
            };
            await WriteJsonResponseAsync(response, status);
        }

        private async Task HandleHealthCheckAsync(HttpListenerResponse response)
        {
            var health = new { status = "ok", timestamp = DateTime.Now.ToString("o") };
            await WriteJsonResponseAsync(response, health);
        }

        /// <summary>
        /// GET /api/identity — returns identity info for this bridge instance.
        /// Enables the CLI client to verify which Revit instance it's talking to.
        /// </summary>
        private async Task HandleIdentityRequestAsync(HttpListenerResponse response)
        {
            var identity = new
            {
                version = _revitVersion,
                pid = _processId,
                port = _port,
                hostname = "localhost",
                commands_count = CommandRouter.GetAllHandlers().Count()
            };
            await WriteJsonResponseAsync(response, identity);
        }

        /// <summary>
        /// GET /api/llms.txt — returns a text reference file describing the raw
        /// Revit API elements, parameters, and classes available in this instance.
        /// Enables AI agents to discover uncovered API structures and pipe them
        /// into the raw/execute_raw fallback commands.
        /// </summary>
        private async Task HandleLlmsTxtRequestAsync(HttpListenerResponse response)
        {
            try
            {
                var uiApp = CliBridgeStateManager.UIApplication;
                string content;

                if (uiApp != null)
                {
                    content = LlmsTxtGenerator.Generate(uiApp, _revitVersion, _port, _processId);
                }
                else
                {
                    // No UIApplication yet (no command has been executed).
                    // Return a minimal static version.
                    content = $"# Revit CLI Bridge - API Reference\n" +
                              $"# Instance: Revit {_revitVersion} | Port: {_port} | PID: {_processId}\n" +
                              $"# Note: Full reference requires at least one command execution.\n\n" +
                              $"## Registered Commands\n";
                    foreach (var handler in CommandRouter.GetAllHandlers())
                    {
                        content += $"  {handler.CommandName} — {handler.Description}\n";
                    }
                    content += "\n## Usage\n";
                    content += "Execute any command first to populate the full reference.\n";
                    content += "  revit-cli.exe ping\n";
                }

                byte[] buffer = Encoding.UTF8.GetBytes(content);
                response.ContentType = "text/plain; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                response.StatusCode = 200;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Error generating llms.txt: {ex.Message}");
                response.StatusCode = 500;
                await WriteJsonResponseAsync(response, new { error = $"Failed to generate llms.txt: {ex.Message}" });
            }
        }

        /// <summary>
        /// GET /api/commands — returns schema for all registered commands.
        /// Supports ETag/If-None-Match for efficient re-fetching.
        /// </summary>
        private async Task HandleCommandsSchemaAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            var schema = BuildCommandSchema();

            // Compute ETag from schema content hash
            var schemaJson = JsonConvert.SerializeObject(schema, Formatting.None);
            var etag = ComputeEtag(schemaJson);

            // Check If-None-Match header — return 304 if schema unchanged
            var ifNoneMatch = request.Headers["If-None-Match"];
            if (ifNoneMatch != null && ifNoneMatch.Trim('"') == etag)
            {
                response.StatusCode = 304;
                response.Headers["ETag"] = $"\"{etag}\"";
                // Do not call response.Close() here — the finally block in
                // HandleRequestAsync will close the response safely.
                return;
            }

            response.Headers["ETag"] = $"\"{etag}\"";
            await WriteJsonResponseAsync(response, schema);
        }

        private static string ComputeEtag(string content)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
                return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }

        /// <summary>
        /// GET /api/commands/{name} — returns schema for a single command.
        /// </summary>
        private async Task HandleCommandSchemaAsync(string commandName, HttpListenerResponse response)
        {
            var handler = CommandRouter.GetHandler(commandName);
            if (handler == null)
            {
                response.StatusCode = 404;
                await WriteJsonResponseAsync(response, new { error = $"Command '{commandName}' not found." });
                return;
            }

            var commandDef = BuildCommandDef(handler);
            await WriteJsonResponseAsync(response, commandDef);
        }

        private CommandSchema BuildCommandSchema()
        {
            var schema = new CommandSchema
            {
                ServerInfo = new ServerInfo
                {
                    Port = _port,
                    Features = new ServerFeatures
                    {
                        DryRun = true,
                        ExecuteRaw = false
                    }
                }
            };

            foreach (var handler in CommandRouter.GetAllHandlers())
            {
                schema.Commands.Add(BuildCommandDef(handler));
            }

            return schema;
        }

        private static CommandDef BuildCommandDef(IBridgeCommand handler)
        {
            return new CommandDef
            {
                Name = handler.CommandName,
                Description = handler.Description,
                Category = handler.Category,
                Aliases = handler.Aliases.Length > 0 ? handler.Aliases : null,
                SupportsDryRun = handler.SupportsDryRun,
                Parameters = handler.Parameters.Length > 0 ? handler.Parameters : null,
                Examples = handler.Examples.Length > 0 ? handler.Examples : null
            };
        }

        private static async Task WriteJsonResponseAsync(HttpListenerResponse response, object data, string contentType = "application/json")
        {
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            response.ContentType = contentType;
            response.ContentLength64 = buffer.Length;
            // Preserve any status code already set by the caller (e.g. 400, 404, 500).
            // Only default to 200 if no non-200 code has been assigned.
            if (response.StatusCode == 0)
                response.StatusCode = 200;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private static async Task WriteJsonResponseAsync(HttpListenerResponse response, string json, string contentType = "application/json")
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            response.ContentType = contentType;
            response.ContentLength64 = buffer.Length;
            // Preserve any status code already set by the caller (e.g. 400, 404, 500).
            // Only default to 200 if no non-200 code has been assigned.
            if (response.StatusCode == 0)
                response.StatusCode = 200;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Write event frame to SSE stream.
        /// Format: "event: {name}\ndata: {json}\n\n"
        /// </summary>
        private static async Task WriteSseEventAsync(Stream outputStream, string eventName, string dataJson)
        {
            var frame = $"event: {eventName}\ndata: {dataJson}\n\n";
            var buffer = Encoding.UTF8.GetBytes(frame);
            await outputStream.WriteAsync(buffer, 0, buffer.Length);
            await outputStream.FlushAsync();
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                CliLogger.Warn($"Error during Dispose->Stop: {ex.Message}");
            }

            try
            {
                _cancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                CliLogger.Warn($"Error disposing CTS: {ex.Message}");
            }

            try
            {
                _listener?.Close();
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Failed to close HTTP listener - http.sys prefix may not be released: {ex.Message}");
                throw;
            }
        }
    }
}
