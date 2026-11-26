# SSE execution and task lifecycle

## Sub-features

- SSE frames on `POST /api/execute` with `Accept: text/event-stream`, in
  order: `accepted {task_id,status:"pending",command}` → `progress` (0 at
  start, then handler-reported percent+message) → `heartbeat {}` every
  15 s on long runs → terminal `completed|failed` carrying
  `{task_id,status,result}`. The terminal frame appears **twice**: once
  broadcast from the task, once as the handler's final frame with the raw
  ResultJson.
- Sync JSON mode (no SSE Accept) — response body is the `CommandResponse`
  itself; a task that ends `Failed` answers HTTP 500, but a **timed-out**
  sync task answers HTTP 200 with the timeout error JSON.
- Async mode (`async:true`) — immediate `{task_id,status:"pending"}`
  receipt; poll `GET /api/task/{id}` until terminal; the task endpoint
  returns `result` ONLY when terminal, 404 after retention (completed
  900 s, failed/timeout 3600 s — the defaults of `task_retention_seconds`;
  cleanup is lazy and runs on the next command, so a 404 lands then, not at
  wall-clock expiry).
- `--request-id` dedup — a repeated `X-Request-Id` replays the task
  snapshot as plain JSON (200) without re-executing, for any retained task
  (terminal included); the bridge logs `request_dedup`.
- Timeout semantics — a command exceeding its budget terminates as
  `failed` with `"Revit API execution timed out."` (client exit 3). The
  first terminal status wins: `TaskInfo.TrySetStatus` holds one lock over
  the check, the write and the broadcast, so a timed-out task cannot flip
  to completed.

## How to get to it (user POV)

Every command the client runs goes through this pipe; `--no-wait` +
polling is how long batch jobs are submitted; `--request-id` is the safe
retry key.

## Driving it with the revit-cli client

```powershell
# frame sequence, visible in the raw stream (no client). Body from a file:
# in pwsh a \" inside -d arrives as a literal backslash (400).
$p = "$env:TEMP\ping.json"; Set-Content $p '{"command":"ping"}' -NoNewline
curl.exe -s -N --max-time 20 -X POST "http://127.0.0.1:$PORT/api/execute" `
  -H "Content-Type: application/json" -H "Accept: text/event-stream" `
  -H "Authorization: Bearer $key" "--data-binary@$p" | Set-Content "$EV\raw-sse.txt"
# raw-sse.txt: event: accepted -> progress -> completed -> completed
(([regex]::Matches((Get-Content "$EV\raw-sse.txt" -Raw), '^event: (\S+)', 'Multiline')).Groups[1].Value) -join ' -> '

# no-wait + poll (task_id is on STDOUT):
Run "nowait" ($U + @("--no-wait","create_wall","--start_x","0","--start_y","0",
                     "--end_x","3000","--end_y","0","--level_id","<LEVEL_ID>"))
# exit 0; stdout "task_id: <GUID>"
Run "poll"  ($U + @("task","-ti","<GUID>"))          # one GET snapshot: status completed + result, exit 0
Run "undo"  ($U + @("undo"))                          # clean up the wall

# dedup: same X-Request-Id twice (raw, so the replay is visible as a status):
curl.exe -s -i --max-time 20 -X POST "http://127.0.0.1:$PORT/api/execute" `
  -H "Content-Type: application/json" -H "Accept: text/event-stream" `
  -H "Authorization: Bearer $key" -H "X-Request-Id: verify-dedup" "--data-binary@$p"   # SSE stream
curl.exe -s -i --max-time 20 -X POST "http://127.0.0.1:$PORT/api/execute" `
  -H "Content-Type: application/json" -H "Accept: text/event-stream" `
  -H "Authorization: Bearer $key" -H "X-Request-Id: verify-dedup" "--data-binary@$p"   # HTTP 200, application/json, no event: lines
# bridge-log.txt has ONE command_executed for the ping and one request_dedup line

# task list + 404:
Run "tasks" ($U + @("task"))                          # recent tasks (last 50)
Run "task-404" ($U + @("task","-ti","00000000-0000-0000-0000-000000000000"))   # 404 body with retention hint, client exit 1
```

## Gotchas

- The final SSE frame re-sends the raw ResultJson — a completed command
  appears in the stream data twice (broadcast + final); assert on the
  last frame.
- Heartbeats are 15 s apart — only observable with a genuinely long
  command (or the log); do not fake them. Always give raw SSE probes
  `--max-time`, else a silent stream holds the drive open.
- Dedup replay arrives as plain JSON even with the SSE Accept header; the
  client's Content-Type dispatch handles it (prints snapshot, exits by
  embedded status).
- Retention means "poll much later" journeys fail with 404 — that is the
  contract, record it as such.
