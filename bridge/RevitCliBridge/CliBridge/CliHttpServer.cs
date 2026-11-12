using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
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
        private SseExecuteHandler? _sseHandler;

        private static readonly SemaphoreSlim _concurrencyLimiter = new SemaphoreSlim(10, 10);

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

            _cancellationTokenSource = new CancellationTokenSource();
            _sseHandler = new SseExecuteHandler(_cancellationTokenSource.Token);

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
            string requestId = context.Request.Headers["X-Request-Id"];
            if (string.IsNullOrWhiteSpace(requestId))
                requestId = Guid.NewGuid().ToString("N").Substring(0, 8);
            context.Response.Headers["X-Request-Id"] = requestId;

            try
            {
                var request = context.Request;
                var response = context.Response;
                var path = request.Url?.AbsolutePath ?? "";

                CliLogger.Info("request_received",
                    ("request_id", requestId),
                    ("method", request.HttpMethod),
                    ("path", path));

                if (AuthPolicy.IsAuthRequired(path))
                {
                    var authHeader = request.Headers["Authorization"];
                    if (!CliBridgeConfigLoader.ValidateToken(authHeader))
                    {
                        CliLogger.Warn("auth_rejected",
                            ("request_id", requestId), ("path", path));
                        response.StatusCode = 401;
                        response.AddHeader("WWW-Authenticate", "Bearer");
                        await HttpIo.WriteJsonResponseAsync(response, new
                        {
                            error = "Unauthorized. Provide 'Authorization: Bearer <api_key>' header.",
                            auth_enabled = CliBridgeConfigLoader.IsAuthEnabled
                        });
                        return;
                    }
                }

                if (path == "/api/execute" && request.HttpMethod == "POST")
                {
                    bool isSseRequested = request.Headers["Accept"]?.Contains("text/event-stream") == true;

                    if (isSseRequested)
                    {
                        await _sseHandler!.HandleAsync(request, response, requestId);
                        responseHandled = true;
                        return;
                    }

                    await HandleExecuteRequestAsync(request, response, requestId);
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
                else if (path == "/api/metrics" && request.HttpMethod == "GET")
                {
                    await HandleMetricsRequestAsync(response);
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
                else if (path == "/api/documents" && request.HttpMethod == "GET")
                {
                    await HandleDocumentsRequestAsync(response);
                }
                else if (path == "/api/commands" && request.HttpMethod == "GET")
                {
                    await HandleCommandsSchemaAsync(request, response);
                }
                else if (path == "/api/catalog" && request.HttpMethod == "GET")
                {
                    await HandleCatalogRequestAsync(request, response);
                }
                else if (path.StartsWith("/api/commands/") && request.HttpMethod == "GET")
                {
                    var commandName = path.Substring("/api/commands/".Length);
                    await HandleCommandSchemaAsync(request, commandName, response);
                }
                else if (path == "/api/raw-mode" && request.HttpMethod == "GET")
                {
                    await HandleRawModeGetAsync(response);
                }
                else if (path == "/api/raw-mode" && request.HttpMethod == "POST")
                {
                    await HandleRawModeSetAsync(request, response);
                }
                else if (path == "/api/auth/status" && request.HttpMethod == "GET")
                {
                    await HandleAuthStatusRequestAsync(response);
                }
                else
                {
                    response.StatusCode = 404;
                    await HttpIo.WriteJsonResponseAsync(response, new { error = "Not found" });
                }
            }
            catch (Exception ex)
            {
                CliLogger.Error("request_error",
                    ("request_id", requestId), ("error", ex.Message));
                if (!responseHandled)
                {
                    try
                    {
                        context.Response.StatusCode = 500;
                        await HttpIo.WriteJsonResponseAsync(context.Response, new { error = ex.Message });
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

        private async Task HandleExecuteRequestAsync(HttpListenerRequest request, HttpListenerResponse response, string requestId)
        {
            string body = await HttpIo.ReadRequestBodyAsync(request, response);
            if (string.IsNullOrEmpty(body) && response.StatusCode == 413) return;

            CliLogger.Info($"Received CLI request: {body}");

            RevitCommandInput? input;
            try
            {
                input = JsonConvert.DeserializeObject<RevitCommandInput>(body);
                if (input is null || string.IsNullOrEmpty(input.Command))
                {
                    response.StatusCode = 400;
                    await HttpIo.WriteJsonResponseAsync(response,
                        CommandResponse.Error("unknown", "Invalid request: missing 'command' field."));
                    return;
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 400;
                await HttpIo.WriteJsonResponseAsync(response,
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
                DryRun = input.DryRun,
                RequestId = requestId
            };

            var config = CliBridgeConfigLoader.Config;
            if (TaskRegistry.CommandQueue.Count >= config.MaxCommandQueueSize)
            {
                response.StatusCode = 429;
                await HttpIo.WriteJsonResponseAsync(response,
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
                await HttpIo.WriteJsonResponseAsync(response, acceptResult);
            }
            else
            {
                int timeoutSeconds = input.TimeoutSeconds ?? CliBridgeConfigLoader.Config.TimeoutSeconds;
                var completedTask = await Task.WhenAny(taskInfo.Tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

                if (completedTask == taskInfo.Tcs.Task)
                {
                    var finalTask = TaskRegistry.GetTask(input.TaskId);
                    if (finalTask != null && finalTask.Status == CliTaskStatus.Failed)
                        response.StatusCode = 500;
                    await HttpIo.WriteJsonResponseAsync(response, taskInfo.Tcs.Task.Result);
                }
                else
                {
                    TaskRegistry.SetFailed(input.TaskId,
                        CommandResponse.Error(input.TaskId, "Revit API execution timed out.").ToJson());
                    await HttpIo.WriteJsonResponseAsync(response,
                        CommandResponse.Error(input.TaskId, "Revit API execution timed out.").ToJson());
                }
            }
        }

        private async Task HandleCliTaskStatusRequestAsync(string taskId, HttpListenerResponse response)
        {
            var task = TaskRegistry.GetTask(taskId);
            if (task == null)
            {
                response.StatusCode = 404;
                await HttpIo.WriteJsonResponseAsync(response, new { error = $"Task '{taskId}' not found." });
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

            await HttpIo.WriteJsonResponseAsync(response, result);
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

            await HttpIo.WriteJsonResponseAsync(response, new { tasks = tasks, count = tasks.Count });
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
            await HttpIo.WriteJsonResponseAsync(response, status);
        }

        /// <summary>
        /// GET /api/metrics — returns aggregate command metrics (count, errors,
        /// avg duration) for observability. Requires auth like other /api endpoints.
        /// </summary>
        private async Task HandleMetricsRequestAsync(HttpListenerResponse response)
        {
            await HttpIo.WriteJsonResponseAsync(response, MetricsCollector.GetSnapshot());
        }

        private async Task HandleHealthCheckAsync(HttpListenerResponse response)
        {
            var health = new { status = "ok", timestamp = DateTime.Now.ToString("o") };
            await HttpIo.WriteJsonResponseAsync(response, health);
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
            await HttpIo.WriteJsonResponseAsync(response, identity);
        }

        /// <summary>
        /// GET /api/raw-mode — query whether raw execution is currently enabled.
        /// </summary>
        private async Task HandleRawModeGetAsync(HttpListenerResponse response)
        {
            var result = new
            {
                allow_raw_execution = CliBridgeConfigLoader.Config.AllowRawExecution
            };
            await HttpIo.WriteJsonResponseAsync(response, result);
        }

        /// <summary>
        /// POST /api/raw-mode — enable or disable raw execution at runtime.
        /// Request body: {"enabled": true|false}
        /// </summary>
        private async Task HandleRawModeSetAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            string body = await HttpIo.ReadRequestBodyAsync(request, response);
            if (string.IsNullOrEmpty(body) && response.StatusCode == 413) return;

            try
            {
                var payload = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
                if (payload == null || !payload.TryGetValue("enabled", out var enabledObj))
                {
                    response.StatusCode = 400;
                    await HttpIo.WriteJsonResponseAsync(response, new { error = "Request body must contain 'enabled' field." });
                    return;
                }

                bool enabled;
                if (enabledObj is bool b)
                {
                    enabled = b;
                }
                else
                {
                    response.StatusCode = 400;
                    await HttpIo.WriteJsonResponseAsync(response, new { error = "'enabled' must be a boolean (true/false)." });
                    return;
                }

                CliBridgeConfigLoader.SetAllowRawExecution(enabled);
                CliLogger.Info($"Raw execution mode changed: allow_raw_execution = {enabled}");

                await HttpIo.WriteJsonResponseAsync(response, new
                {
                    allow_raw_execution = enabled,
                    message = enabled
                        ? "Raw execution enabled. execute_raw command is now available."
                        : "Raw execution disabled. execute_raw command is now blocked."
                });
            }
            catch (Exception ex)
            {
                response.StatusCode = 400;
                await HttpIo.WriteJsonResponseAsync(response, new { error = $"Invalid request: {ex.Message}" });
            }
        }

        /// <summary>
        /// GET /api/auth/status — reports whether API key authentication is
        /// active. Public endpoint (no Bearer token required) so clients can
        /// discover auth requirements before sending authenticated requests.
        /// </summary>
        private async Task HandleAuthStatusRequestAsync(HttpListenerResponse response)
        {
            await HttpIo.WriteJsonResponseAsync(response, new
            {
                auth_enabled = CliBridgeConfigLoader.IsAuthEnabled,
                scheme = "Bearer",
                message = CliBridgeConfigLoader.IsAuthEnabled
                    ? "API key authentication is enabled. Send 'Authorization: Bearer <api_key>' header."
                    : "Authentication is disabled. All endpoints are open."
            });
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
                await HttpIo.WriteJsonResponseAsync(response, new { error = $"Failed to generate llms.txt: {ex.Message}" });
            }
        }

        /// <summary>
        /// GET /api/documents — lists every open document and the active one.
        /// Reads live Revit state from the HTTP thread, like /api/llms.txt.
        /// The client's `list` command calls this to fill the DOCUMENT column.
        /// </summary>
        private async Task HandleDocumentsRequestAsync(HttpListenerResponse response)
        {
            try
            {
                var uiApp = CliBridgeStateManager.UIApplication;

                var documents = new List<object>();
                string? activePath = null;

                if (uiApp != null)
                {
                    var activeDoc = uiApp.ActiveUIDocument?.Document;
                    var allDocs = uiApp.Application?.Documents;

                    if (allDocs != null)
                    {
                        foreach (Document doc in allDocs)
                        {
                            string? path = doc.PathName;
                            if (string.IsNullOrEmpty(path))
                                path = null;

                            bool isActive = activeDoc != null &&
                                doc.PathName == activeDoc.PathName &&
                                doc.Title == activeDoc.Title;

                            documents.Add(new
                            {
                                title = doc.Title,
                                path,
                                is_active = isActive
                            });

                            if (isActive)
                                activePath = path;
                        }
                    }
                }

                response.StatusCode = 200;
                await HttpIo.WriteJsonResponseAsync(response, new
                {
                    documents,
                    active_path = activePath
                });
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Error listing documents: {ex.Message}");
                response.StatusCode = 500;
                await HttpIo.WriteJsonResponseAsync(response, new { error = $"Failed to list documents: {ex.Message}" });
            }
        }

        /// <summary>
        /// GET /api/commands — returns schema for all registered commands.
        /// Supports ETag/If-None-Match for efficient re-fetching. The schema
        /// and ETag are cached in <see cref="CommandSchemaCache"/> and only
        /// rebuilt when handlers are registered or unregistered.
        /// </summary>
        private async Task HandleCommandsSchemaAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            var etag = CommandSchemaCache.GetEtag();

            var ifNoneMatch = request.Headers["If-None-Match"];
            if (ifNoneMatch != null && ifNoneMatch.Trim('"') == etag)
            {
                response.StatusCode = 304;
                response.Headers["ETag"] = $"\"{etag}\"";
                return;
            }

            response.Headers["ETag"] = $"\"{etag}\"";
            var schema = new CommandSchema
            {
                Version = "2.0.0",
                ServerInfo = new ServerInfo
                {
                    BridgeVersion = CommandSchemaCache.BridgeVersion,
                    Port = _port,
                    Features = new ServerFeatures
                    {
                        DryRun = true,
                        ExecuteRaw = CliBridgeConfigLoader.Config.AllowRawExecution
                    }
                },
                Commands = new List<CommandDef>(CommandSchemaCache.GetDefs())
            };
            await HttpIo.WriteJsonResponseAsync(response, schema);
        }

        /// <summary>
        /// Drops the cached command schema. Called when handlers are
        /// registered at runtime (e.g. plugin loading).
        /// </summary>
        public static void InvalidateSchemaCache()
        {
            CommandSchemaCache.Invalidate();
        }

        /// <summary>
        /// GET /api/commands/{name} — returns schema for a single command.
        /// Supports ETag/If-None-Match so clients can cheaply revalidate
        /// per-command schemas without re-downloading the full payload.
        /// </summary>
        private async Task HandleCommandSchemaAsync(HttpListenerRequest request, string commandName, HttpListenerResponse response)
        {
            var handler = CommandRouter.GetHandler(commandName);
            if (handler == null)
            {
                response.StatusCode = 404;
                await HttpIo.WriteJsonResponseAsync(response, new { error = $"Command '{commandName}' not found." });
                return;
            }

            var commandDef = CommandSchemaCache.BuildDef(handler);
            var etag = CommandSchemaCache.ComputeCommandEtag(commandDef);

            var ifNoneMatch = request.Headers["If-None-Match"];
            if (ifNoneMatch != null && ifNoneMatch.Trim('"') == etag)
            {
                response.StatusCode = 304;
                response.Headers["ETag"] = $"\"{etag}\"";
                return;
            }

            response.Headers["ETag"] = $"\"{etag}\"";
            await HttpIo.WriteJsonResponseAsync(response, commandDef);
        }

        /// <summary>
        /// GET /api/catalog — returns a lightweight command index containing
        /// only names, categories, and one-line summaries. Designed for AI
        /// agent discovery: ~3-5 KB vs ~50-150 KB for the full schema.
        /// Supports ETag/If-None-Match; the ETag is shared with the full
        /// schema cache (same underlying data, same invalidation lifecycle).
        /// </summary>
        private async Task HandleCatalogRequestAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            var etag = CommandSchemaCache.GetEtag();

            var ifNoneMatch = request.Headers["If-None-Match"];
            if (ifNoneMatch != null && ifNoneMatch.Trim('"') == etag)
            {
                response.StatusCode = 304;
                response.Headers["ETag"] = $"\"{etag}\"";
                return;
            }

            var defs = CommandSchemaCache.GetDefs();
            var catalog = new CommandCatalog
            {
                CatalogVersion = CommandSchemaCache.BridgeVersion,
                CommandCount = defs.Count,
                Commands = defs.Select(d => new CatalogEntry
                {
                    Name = d.Name,
                    Category = d.Category,
                    Summary = d.Description
                }).ToList()
            };

            response.Headers["ETag"] = $"\"{etag}\"";
            await HttpIo.WriteJsonResponseAsync(response, catalog);
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
