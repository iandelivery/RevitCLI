# SSE execution and task lifecycle

## Sub-features

- SSE frames on `POST /api/execute` with `Accept: text/event-stream`, in
  order: `accepted {task_id,status:"pending",command}` → `progress
  {task_id,progress,progress_message}` (0 at start, message "Execution
  started", then handler-reported percent+message) → `heartbeat {}` every
  15 s on long runs → terminal `completed|failed {task_id,status,result}`
  (the broadcast frame) followed by the **final frame**: the raw
  `CommandResponse` (`task_id,status,message,data`) with no `result`
  envelope, same event name. A completed command therefore appears twice.
  A timeout ends with event `failed`, `status:"timeout"`.
- Sync JSON mode (no SSE Accept) — response body is the `CommandResponse`
  itself; a `Failed` task answers HTTP 500 with the error JSON; a sync
  **timeout** answers HTTP **200** with `{status:"error",message:"Revit
  API execution timed out."}`.
- Async mode (`async:true`) — immediate `{task_id,status:"pending",
  message:"Task submitted. Poll GET /api/task/{task_id} for status."}`
  receipt; poll `GET /api/task/{id}` until terminal; the task endpoint
  returns `{task_id,command,status,progress,request_id,started_at,
  completed_at,result}` with `result` ONLY when terminal, 404 after
  retention (completed 900 s = `task_retention_seconds`, failed/timeout
  3600 s fixed). `GET /api/task` lists the newest 50
  `{tasks[{task_id,command,status,progress,created_at}],count}`. Cleanup
  runs at the end of the next command execution, so a 404 needs a later
  command to trigger it.
- `--request-id` dedup — a repeated `X-Request-Id` replays the retained
  task's status (terminal included) as plain JSON (200,
  `application/json`, no frames) without re-executing; the bridge logs
  `request_dedup request_id=<id> task_id=<tid>`.
- Timeout semantics — a command exceeding its budget terminates as
  `failed` with `"Revit API execution timed out."` (client exit 3).
  `--timeout` is sent as `timeout_seconds` and enforced only in the
  client's polling fallback.

## How to get to it (user POV)

Every command the client runs goes through this pipe; `--no-wait` +
polling is how long batch jobs are submitted; `--request-id` is the safe
retry key.

## Driving it with the revit-cli client

```powershell
# frame sequence, visible in the raw stream (no client). JSON from a file,
# bounded with --max-time (curl stays attached after the final frame):
'{"command":"ping"}' | Set-Content "$env:TEMP\ping.json" -NoNewline
curl.exe -s -N --max-time 4 -D "$EV\sse-headers.txt" -X POST "$B/api/execute" `
  -H "Content-Type: application/json" -H "Accept: text/event-stream" `
  -H "Authorization: Bearer $key" --data-binary "@$env:TEMP\ping.json" | Set-Content "$EV\raw-sse.txt"
# raw-sse.txt: event: accepted / progress / completed / completed (last = raw CommandResponse)
# sse-headers.txt: 200, Content-Type: text/event-stream, Cache-Control: no-cache

# no-wait + poll:
Run "nowait" ($U + @("--no-wait","create_wall","--start-x","0","--start-y","0",
                     "--end-x","3000","--end-y","0","--level-id","<LEVEL_ID>"))
# exit 0; "task_id: <GUID>" on STDOUT (the --no-wait receipt); the SSE
# accepted task_id on a normal run goes to stderr
Run "poll"  ($U + @("task","-ti","<GUID>"))          # single GET: status completed + result
Run "undo"  ($U + @("undo"))                          # clean up the wall

# dedup: same X-Request-Id twice (raw, so the Content-Type switch is visible)
1..2 | ForEach-Object {
  curl.exe -s -i --max-time 4 -X POST "$B/api/execute" -H "Content-Type: application/json" `
    -H "Accept: text/event-stream" -H "X-Request-Id: verify-dedup" `
    -H "Authorization: Bearer $key" --data-binary "@$env:TEMP\ping.json" | Set-Content "$EV\dedup-$_.txt" }
# dedup-1: text/event-stream + frames; dedup-2: application/json, 0 frames, snapshot with result
# bridge-log.txt: exactly ONE "request_dedup request_id=verify-dedup" line

# task list + 404:
Run "tasks" ($U + @("task"))                          # {tasks[<=50 newest first],count}
Run "task-404" ($U + @("task","-ti","00000000-0000-0000-0000-000000000000"))   # 404 body, client exit 1
# body: {error:"Task '<id>' not found.", hint:"Finished tasks are kept for 900 seconds; failed and timed-out tasks for 3600 seconds."}
```

## Gotchas

- The final SSE frame re-sends the raw ResultJson — a completed command
  appears in the stream data twice (broadcast + final); assert on the
  last frame. The broadcast frame carries `{task_id,status,result}`; the
  final frame is the bare CommandResponse.
- Sync and async execute return 429 when the queue is at
  `max_command_queue_size`; the SSE path has no check and never does.
- Heartbeats are 15 s apart — only observable with a genuinely long
  command (or the log); do not fake them.
- Dedup replay arrives as plain JSON on the SSE path; the client's
  Content-Type dispatch handles it (prints snapshot, exits by embedded
  status).
- Retention means "poll much later" journeys fail with 404 — that is the
  contract, record it as such.
