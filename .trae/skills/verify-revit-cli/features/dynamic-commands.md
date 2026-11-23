# Dynamic command execution (flags, units, SSE, tasks, dedup)

## Sub-features

- Dynamic dispatch — unknown-to-cobra commands resolve via
  `GET /api/commands/{name}` (per-process cache), then flags parse against
  the schema: `--start-x` ↔ `start_x` (dash/underscore), `-l` short flags,
  coercion for `int`/`double`/`bool` and comma-split `int[]`-style arrays;
  schema defaults apply. **Object/array params (e.g. `views`) stay raw
  strings — nested JSON params are NOT parsed client-side; pass them via
  `raw -j` when the schema demands an object.**
- Required validation — every missing required flag → per-param
  `Error: --X is required` on stderr, exit 1, **no HTTP call**.
- `--dry-run` — only on schemas with `supports_dry_run`; hoisted to a
  top-level `dry_run:true`; on non-supporting commands → exit 1.
- `--units` — repeatable `kind=unit` pairs or the `project` sentinel;
  validated after URL resolution + schema fetch, before execute; injected
  as top-level `units`. Malformed (`bogus`, `length=yard` against the mock's
  contract) or mixed specs → exit 1 client-side; **bridge-side rejection**
  (kind/unit unknown to the bridge) returns a `failed` event whose message
  lists the allowed kinds/units → exit 4.
- SSE execution — `accepted` (task_id on stderr) → `progress` →
  `heartbeat` (ignored) → `completed` (result on stdout, exit 0) |
  `failed` (stderr; exit 4, or 3 for the exact timeout message).
- `--no-wait` — sends `async:true`, drops the SSE Accept, prints
  `task_id:` and exits 0; `task -ti <id>` polls `GET /api/task/{id}` to
  completion.
- `--request-id` — sent as `X-Request-Id`; the bridge (and mock) dedups:
  a second request with the same id replays the first task's status as
  plain JSON instead of executing again.
- `raw -j '<json>'` — send an arbitrary payload: every top-level key other
  than `command`/`parameters` is forwarded (the escape hatch for nested
  object params and exotic top-level fields).

## How to get to it (user POV)

The core loop: "create a wall in feet", "submit and poll", "retry safely".
Everything an agent actually runs goes through here.

## Driving it with the terminal + mock bridge

```powershell
Run "wall" @("--url","http://127.0.0.1:$PORT","create_wall",
    "--start-x","0","--start-y","0","--end-x","5000","--end-y","0","-l","3001")
# exit 0; echo shows start_x/end_x/level_id + element_id  -> flag-mapping oracle

Run "wall-dry" (...,"--dry-run")      # echo dry_run:true, NO element_id
Run "wall-missing" @(...,"create_wall","-l","3001")   # exit 1, Error: --start_x is required
Run "wall-neg" (...,"--end-x","-5",...)               # -5 parsed as value (known past regression)

Run "units-ft" @("--url","http://127.0.0.1:$PORT","--units","length=ft","create_wall",...)
# bridge.log execute line shows units={"length": "ft"}; echo carries units
Run "units-project" @("--url",...,"--units","project","get_units")   # canonical matrix echo
Run "units-bogus" @("--units","bogus","ping")          # exit 1, before connecting (no log line)
Run "units-badkind" @("--url",...,"--units","mass","kg","ping")
# bridge rejects: failed event "Unknown unit kind 'mass' in 'units'. Allowed kinds: length, area, volume, angle." -> exit 4

Run "nowait" @("--url","http://127.0.0.1:$PORT","--no-wait","create_wall",...)
# exit 0; stderr has task_id: mock-task-N
Run "task-poll" @("--url","http://127.0.0.1:$PORT","task","-ti","mock-task-N")  # completed result
Run "task-list" @("--url","http://127.0.0.1:$PORT","task")                     # recent tasks

Run "dedup-1" @("--url","http://127.0.0.1:$PORT","--request-id","dedup-probe","create_wall",...)
Run "dedup-2" @("--url","http://127.0.0.1:$PORT","--request-id","dedup-probe","create_wall",...)
# dedup-2 replays dedup-1's task (bridge.log: request_dedup line); only ONE execute happened

Run "raw-nested" @("--url","http://127.0.0.1:$PORT","raw","-j",
    '{"command":"create_wall","parameters":{"start_x":0,"start_y":0,"end_x":9,"end_y":0,"level_id":3001}}')
# exit 0; proves the raw escape hatch

Run "fail"   (against a --fail-command mock)  # exit 4 + exact failure message on stderr
Run "timeout" (against a --slow-command mock) # exit 3 ("Revit API execution timed out.")
```

## Gotchas

- The echo oracle proves what the client SENT — model-mutating claims need
  a real bridge (see the bridge skill) or `--dry-run` observation.
- `--units` validation order matters: a bogus spec on a dead port still
  exits 1 only after URL resolution — with `--url` to a live mock the
  rejection happens pre-connect (no execute line in the log).
- `raw -j` needs valid JSON with a `"command"` key — quoting in PowerShell
  means single-quote the JSON; inline `$var` interpolation still requires
  a .ps1 script.
- Dedup replay arrives as plain JSON on the SSE path (Content-Type
  dispatch) — the client prints it and exits by the embedded status.
