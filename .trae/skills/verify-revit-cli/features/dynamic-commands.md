# Dynamic command execution (flags, units, SSE, tasks, dedup)

## Sub-features

- Dynamic dispatch — unknown-to-cobra commands resolve via
  `GET /api/commands/{name}` (`FetchCommand`, process cache) **before** the
  execute — so a reachable mock logs that GET; a dead port fails here with
  "Unknown command" exit 1 and never reaches the units check. Flags parse
  against the schema: `--start-x` ↔ `start_x` (dash/underscore), `-l` short
  flags, coercion for `int`/`double`/`bool` and comma-split typed arrays
  (`int[]`, `string[]`); schema defaults apply. **`object`/`object[]` params
  (e.g. `views`, `moves`) stay raw strings — nested JSON params are NOT
  parsed client-side; pass them via `raw -j`.**
- Required validation — every missing required flag → per-param
  `Error: --X is required` on stderr, exit 1, **no execute call** (the
  schema GET has already happened).
- `--dry-run` — only on schemas with `supports_dry_run`; hoisted to a
  top-level `dry_run:true`; on non-supporting commands the bridge rejects
  (exit 4). `--dry-run` written **before the command name is silently
  dropped** (`FirstPositional` skips it) and the command executes for real —
  product gap, 2026-11-26; always put `--dry-run` after the command.
- `--units` — repeatable `kind=unit` pairs or the `project` sentinel; only
  the `kind=unit` shape is checked client-side (after URL resolution and the
  schema fetch, before execute) — a shapeless spec (`bogus`) or a mix of
  `project` with pairs → exit 1; an unknown kind/unit (`length=yard`) is
  **sent** and the bridge's `failed` event → exit 4. Injected as top-level
  `units`.
- SSE execution — `accepted` (task_id on stderr) → `progress` →
  `heartbeat` (ignored) → `completed` (result on stdout, exit 0) |
  `failed` (stderr; exit 4, or 3 for the exact timeout message).
- `--no-wait` — sends `async:true`, drops the SSE Accept, prints
  `task_id:` on **stdout** and exits 0; `task -ti <id>` is a **single**
  `GET /api/task/{id}` snapshot (exit 0 for any 2xx, even a failed task) —
  poll in a loop if you need completion.
- `--request-id` — sent as `X-Request-Id`; the bridge (and mock) dedups:
  a second request with the same id replays the first task's status as
  plain JSON instead of executing again.
- `raw -j '<json>'` — send an arbitrary payload: every top-level key other
  than `command`/`parameters` is forwarded (the escape hatch for nested
  object params and exotic top-level fields).
- `execute_raw` — `--code`/`-c <text>`, `--file <path>` or `--stdin`
  (exactly one; more → exit 1), `--language`/`-l`; `raw-mode` GET, or
  `--enable`/`--disable` POST. Both are builtins: a non-2xx is exit 1. The
  mock serves no `execute_raw` schema, so these only drive against a real
  bridge.

## How to get to it (user POV)

The core loop: "create a wall in feet", "submit and poll", "retry safely".
Everything an agent actually runs goes through here.

## Driving it with the terminal + mock bridge

```powershell
$w = @("--start_x","0","--start_y","0","--end_x","5000","--end_y","0","-l","3001")
Run "wall" @("--url","http://127.0.0.1:$PORT","create_wall") + $w
# exit 0; echo shows start_x/end_x/level_id + element_id  -> flag-mapping oracle

Run "wall-dry" @("--url","http://127.0.0.1:$PORT","create_wall") + $w + @("--dry-run")
# echo dry_run:true, NO element_id
Run "wall-missing" @("--url","http://127.0.0.1:$PORT","create_wall","-l","3001")   # exit 1, Error: --start_x is required
Run "wall-neg" @("--url","http://127.0.0.1:$PORT","create_wall","--start_x","0","--start_y","0","--end-x=-5000","--end_y","0","-l","3001")
# -5000 parsed as the value of --end-x (known past regression)

Run "units-ft" @("--url","http://127.0.0.1:$PORT","--units","length=ft","create_wall") + $w
# bridge.log execute line shows units={"length": "ft"}; echo carries units
Run "units-project" @("--url","http://127.0.0.1:$PORT","--units","project","get_units")   # canonical matrix echo
Run "units-bogus" @("--url","http://127.0.0.1:$PORT","--units","bogus","ping")          # exit 1; GET /api/commands/ping logged, NO execute
Run "units-yard" @("--url","http://127.0.0.1:$PORT","--units","length=yard","ping")     # exit 4: "Unit 'yard' is not allowed for kind 'length'..."
Run "units-badkind" @("--url","http://127.0.0.1:$PORT","--units","mass=kg","ping")      # exit 4: "Unknown unit kind 'mass' ..."

Run "nowait" @("--url","http://127.0.0.1:$PORT","--no-wait","create_wall") + $w
# exit 0; STDOUT has task_id: mock-task-N
Run "task-poll" @("--url","http://127.0.0.1:$PORT","task","-ti","mock-task-N")  # one snapshot: completed result
Run "task-list" @("--url","http://127.0.0.1:$PORT","task")                     # recent tasks

Run "dedup-1" @("--url","http://127.0.0.1:$PORT","--request-id","dedup-probe","create_wall") + $w
Run "dedup-2" @("--url","http://127.0.0.1:$PORT","--request-id","dedup-probe","create_wall") + $w
# dedup-2 replays dedup-1's task (bridge.log: request_dedup line); only ONE execute happened

Run "dry-before-cmd" @("--url","http://127.0.0.1:$PORT","--dry-run","create_wall") + $w
# exit 0 and the echo has NO dry_run — the flag was dropped and the command ran for real (product gap)

Run "raw-nested" @("--url","http://127.0.0.1:$PORT","raw","-j",
    '{"command":"create_wall","parameters":{"start_x":0,"start_y":0,"end_x":9,"end_y":0,"level_id":3001}}')
# exit 0; proves the raw escape hatch

Run "fail"   (against a --fail-command mock)  # exit 4 + exact failure message on stderr
Run "timeout" (against a --slow-command mock) # exit 3 ("Revit API execution timed out.")
```

## Gotchas

- The echo oracle proves what the client SENT — model-mutating claims need
  a real bridge (see the bridge skill) or `--dry-run` observation.
- `--units` validation order: a bogus spec exits 1 only after URL
  resolution and the schema GET (so the mock log shows that GET but no
  execute); `length=yard` is **not** rejected client-side — it is a bridge
  rejection, exit 4.
- A dead port + dynamic command = "Unknown command" exit 1 (the schema GET
  failed), not exit 2 — only `ping`/`--url` + builtin transport failures
  give 2.
- `raw -j` needs valid JSON with a `"command"` key — quoting in PowerShell
  means single-quote the JSON or load it from a file (`\"` inside a
  single-quoted string reaches the exe as a literal backslash); inline
  `$var` interpolation still requires a .ps1 script.
- Dedup replay arrives as plain JSON on the SSE path (Content-Type
  dispatch) — the client prints it and exits by the embedded status.
