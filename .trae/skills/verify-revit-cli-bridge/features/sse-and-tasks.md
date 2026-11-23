# SSE execution and task lifecycle

## Sub-features

- SSE frames on `POST /api/execute` with `Accept: text/event-stream`, in
  order: `accepted {task_id,status:"pending",command}` → `progress` (0 at
  start, then handler-reported percent+message) → `heartbeat {}` every
  15 s on long runs → terminal `completed|failed` carrying
  `{task_id,status,result}`.
- Sync JSON mode (no SSE Accept) — response body is the `CommandResponse`
  itself; failed sync tasks answer HTTP 500 with the error JSON.
- Async mode (`async:true`) — immediate `{task_id,status:"pending"}`
  receipt; poll `GET /api/task/{id}` until terminal; the task endpoint
  returns `result` ONLY when terminal, 404 after retention (completed
  900 s, failed/timeout 3600 s).
- `--request-id` dedup — a repeated `X-Request-Id` replays the live
  task's status as plain JSON (200) without re-executing; the bridge logs
  `request_dedup`.
- Timeout semantics — a command exceeding its budget terminates as
  `failed` with `"Revit API execution timed out."` (client exit 3).

## How to get to it (user POV)

Every command the client runs goes through this pipe; `--no-wait` +
polling is how long batch jobs are submitted; `--request-id` is the safe
retry key.

## Driving it with the revit-cli client

```powershell
# frame sequence, visible in the raw stream (no client) :
curl.exe -s -N -X POST "http://127.0.0.1:$PORT/api/execute" `
  -H "Content-Type: application/json" -H "Accept: text/event-stream" `
  -H "Authorization: Bearer $key" -d '{\"command\":\"ping\"}' | Set-Content "$EV\raw-sse.txt"
# raw-sse.txt shows: event: accepted / progress / completed with result payloads

# no-wait + poll:
Run "nowait" ($U + @("--no-wait","create_wall","--start-x","0","--start-y","0",
                     "--end-x","3000","--end-y","0","--level-id","<LEVEL_ID>"))
# exit 0; stderr "task_id: <GUID>"
Run "poll"  ($U + @("task","-ti","<GUID>"))          # status completed + result
Run "undo"  ($U + @("undo"))                          # clean up the wall

# dedup: same X-Request-Id twice
Run "dedup-1" ($U + @("--request-id","verify-dedup","create_wall",...))
Run "dedup-2" ($U + @("--request-id","verify-dedup","create_wall",...))
# dedup-2 prints dedup-1's task snapshot; bridge-log.txt has ONE
# command_executed for the wall and one request_dedup line
Run "undo" ($U + @("undo"))

# task list + 404:
Run "tasks" ($U + @("task"))                          # recent tasks incl. above
Run "task-404" ($U + @("task","-ti","00000000-0000-0000-0000-000000000000"))   # 404 body, client exit 1
```

## Gotchas

- The final SSE frame re-sends the raw ResultJson — a completed command
  appears in the stream data twice (broadcast + final); assert on the
  last frame.
- Heartbeats are 15 s apart — only observable with a genuinely long
  command (or the log); do not fake them.
- Dedup replay arrives as plain JSON on the SSE path; the client's
  Content-Type dispatch handles it (prints snapshot, exits by embedded
  status).
- Retention means "poll much later" journeys fail with 404 — that is the
  contract, record it as such.
