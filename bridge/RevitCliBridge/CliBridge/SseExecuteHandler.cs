using Newtonsoft.Json;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Models;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace RevitCliBridge
{
    /// <summary>
    /// Handles the SSE mode execute request: the client opts in with
    /// Accept: text/event-stream, the server keeps the connection open and
    /// pushes real-time events (accepted, progress, heartbeat, completed or
    /// failed) until the task completes, the client disconnects, or the
    /// timeout fires. One instance per server; the server token cancels
    /// pending streams when the bridge stops.
    /// </summary>
    public class SseExecuteHandler
    {
        private readonly CancellationToken _serverToken;

        public SseExecuteHandler(CancellationToken serverToken)
        {
            _serverToken = serverToken;
        }

        public async Task HandleAsync(HttpListenerRequest request, HttpListenerResponse response, string requestId)
        {
            string body = await HttpIo.ReadRequestBodyAsync(request, response);
            if (string.IsNullOrEmpty(body) && response.StatusCode == 413) return;

            CliLogger.Info($"Received SSE CLI request: {body}");

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

            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");
            response.Headers.Add("X-Accel-Buffering", "no");
            response.StatusCode = 200;

            var taskInfo = TaskRegistry.CreateTask(input.TaskId, input.Command);

            var sseCts = CancellationTokenSource.CreateLinkedTokenSource(_serverToken);
            bool clientDisconnected = false;

            taskInfo.OnSseEvent += async (eventName, dataJson) =>
            {
                if (clientDisconnected) return;
                try
                {
                    await HttpIo.WriteSseEventAsync(response.OutputStream, eventName, dataJson);
                }
                catch (Exception)
                {
                    clientDisconnected = true;
                    sseCts.Cancel();
                }
            };

            try
            {
                await HttpIo.WriteSseEventAsync(response.OutputStream, "accepted",
                    JsonConvert.SerializeObject(new { task_id = input.TaskId, status = "pending", command = input.Command }));
            }
            catch (Exception)
            {
                clientDisconnected = true;
                taskInfo.ClearSseSubscribers();
                return;
            }

            var queuedCommand = new QueuedCommand
            {
                TaskId = input.TaskId,
                Command = input.Command,
                Parameters = input.Parameters,
                DryRun = input.DryRun,
                RequestId = requestId
            };
            TaskRegistry.CommandQueue.Enqueue(queuedCommand);
            TaskRegistry.RevitEvent?.Raise();

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
                            await HttpIo.WriteSseEventAsync(response.OutputStream, "heartbeat", "{}");
                        }
                        catch
                        {
                            heartbeatCts.Cancel();
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }, heartbeatCts.Token);

            int timeoutSeconds = input.TimeoutSeconds ?? CliBridgeConfigLoader.Config.TimeoutSeconds;
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), sseCts.Token);

            var completedTask = await Task.WhenAny(taskInfo.Tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                TaskRegistry.SetFailed(input.TaskId,
                    CommandResponse.Error(input.TaskId, "Revit API execution timed out").ToJson());
            }

            if (!clientDisconnected)
            {
                try
                {
                    var finalTask = TaskRegistry.GetTask(input.TaskId);
                    if (finalTask != null)
                    {
                        var finalEvent = finalTask.Status == CliTaskStatus.Completed ? "completed" : "failed";
                        var finalData = finalTask.ResultJson ?? "{}";
                        await HttpIo.WriteSseEventAsync(response.OutputStream, finalEvent, finalData);
                    }
                }
                catch { }
            }

            heartbeatCts.Cancel();
            try { await heartbeatTask; } catch { }

            taskInfo.ClearSseSubscribers();

            try { heartbeatCts.Dispose(); } catch { }
            try { sseCts.Dispose(); } catch { }
        }
    }
}
