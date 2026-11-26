# Dynamic command execution (flags, units, SSE, tasks, dedup)

## Sub-features

- Dynamic dispatch — unknown-to-cobra commands resolve via
  `GET /api/commands/{name}` (per-process cache), then flags parse against
  the schema: `--start-x` ↔ `start_x` (dash/underscore), `-l` short flags,
  coercion for `int`/`double`/`bool` and comma-split `int[]`-style arrays;
  schema defaults apply (injected as the raw schema value in REQUEST
  units, no conversion — the mock's `height: 3000` lands as `3000` in the
  payload). **Object/array params (e.g. `views`, `view_ids`) stay raw
  strings — nested JSON params are NOT parsed client-side; pass them via
  `raw -j` when the schema demands an object or an int array.** Name
  rewriting (alias / domain path / underscore reversal) happens only on
  the bridge: as a typed command name, `wall_create` is "Unknown command"
  client-side (the fallback matches `cmd.Name` only).
- Required validation — every missing required flag → per-param
  `Error: --X is required` on stderr, exit 1; the schema GET has already
  happened, only `POST /api/execute` is skipped.
- `--dry-run` — only on schemas with `supports_dry_run`; hoisted to a
  top-level `dry_run:true`; on non-supporting commands → `Error: command
  '<name>' does not support --dry-run` + `Usage:` line, exit 1, after the
  schema GET, before execute, and only after the required checks pass.
  **`--dry-run` written before the command name is a usage error** (fixed
  PR #122): `Error: --dry-run must come after the command name`, exit 1,
  and ZERO HTTP requests (the check runs right after URL resolution, so
  pass `--url`). A `--dry-run` consumed as a global flag's value
  (`--request-id --dry-run create_wall`) is NOT a leading flag.
- `--units` — repeatable `kind=unit` pairs or the `project` sentinel;
  validated after URL resolution + schema fetch, before execute; injected
  as top-level `units`. A shapeless spec (`bogus`) or a mix of `project`
  with pairs → exit 1 client-side; an unknown kind/unit (`length=yard`,
  `mass=kg`) is **sent** and the bridge's `failed` event → exit 4.
  (`--units mass kg` is NOT a pair: cobra splits it into `--units mass`
  plus a command named `kg`.) Units errors beat required-flag errors.
- SSE execution — `accepted` (task_id on stderr) → `progress` →
  `heartbeat` (ignored) → `completed` (result on stdout, exit 0) |
  `failed` (stderr; exit 4, or 3 for the exact timeout message).
- `--no-wait` — sends `async:true`, drops the SSE Accept, prints
  `task_id:` on STDOUT and exits 0; `task -ti <id>` is a single
  `GET /api/task/{id}` (no polling loop).
- `--request-id` — sent as `X-Request-Id`; the bridge (and mock) dedups:
  a second request with the same id replays the first task's status as
  plain JSON instead of executing again.
- `raw -j '<json>'` — send an arbitrary payload: every top-level key other
  than `command`/`parameters` is forwarded (the escape hatch for nested
  object params and exotic top-level fields). In a pwsh script, pass the
  JSON from a file as ONE token (`(Get-Content $file -Raw)`); inline
  `\"` reaches the exe with literal backslashes.

## How to get to it (user POV)

The core loop: "create a wall in feet", "submit and poll", "retry safely".
Everything an agent actually runs goes through here.

## Driving it with the terminal + mock bridge

```powershell
Run "wall" @("--url","http://127.0.0.1:$PORT","create_wall",
    "--start-x","0","--start-y","0","--end-x","5000","--end-y","0","-l","3001")
# exit 0; echo shows start_x/end_x/level_id + element_id  -> flag-mapping oracle

Run "wall-dry" (...,"--dry-run")      # echo dry_run:true, NO element_id
Run "wall-missing" @(...,"create_wall","-l","3001")   # exit 1, Error: --start_x is required (schema GET logged, no execute)
Run "wall-neg" (...,"--end-x","-5",...)               # -5 parsed as value (known past regression)

# leading --dry-run (fixed PR #122, live 2026-11-26):
$n0 = (Get-Content "$EV\bridge.log").Count
Run "dry-leading" @("--url","http://127.0.0.1:$PORT","--dry-run","create_wall",...)
# exit 1; stderr exactly "Error: --dry-run must come after the command name"
"requests: " + (((Get-Content "$EV\bridge.log").Count - $n0) / 2)    # 0 (the mock logs 2 lines per request)
Run "dry-unsupported" @("--url","http://127.0.0.1:$PORT","get_units","--dry-run")
# exit 1; "Error: command 'get_units' does not support --dry-run" + "Usage: revit-cli get_units"; schema GET logged, no execute

Run "units-ft" @("--url","http://127.0.0.1:$PORT","--units","length=ft","create_wall",...)
# bridge.log execute line shows units={"length": "ft"}; echo carries units
Run "units-project" @("--url",...,"--units","project","get_units")   # canonical matrix echo
Run "units-bogus" @("--url",...,"--units","bogus","ping")          # exit 1 "invalid --units value: bogus ..." (schema GET logged, no execute)
Run "units-badkind" @("--url",...,"--units","mass=kg","ping")
# SENT to the mock: failed event "Unknown unit kind 'mass' in 'units'. Allowed kinds: length, area, volume, angle." -> exit 4
Run "units-badunit" @("--url",...,"--units","length=yard","ping")  # exit 4 "Unit 'yard' is not allowed for kind 'length'. Allowed units: mm, cm, m, ft, in."

Run "nowait" @("--url","http://127.0.0.1:$PORT","--no-wait","create_wall",...)
# exit 0; "task_id: mock-task-N" on stdout
Run "task-poll" @("--url","http://127.0.0.1:$PORT","task","-ti","mock-task-N")  # completed result (single GET)
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
- Validation order for a dynamic command: URL resolve → leading
  `--dry-run` check (zero HTTP) → schema GET → `--units` shape (exit 1) →
  required flags (exit 1) → dry-run support (exit 1) → execute. So a
  bogus spec or a missing flag on a dead port reports `Unknown command`
  instead (the schema GET failed first). "Pre-connect" holds only for
  built-ins like `ping`.
- The mock writes two log lines per request (one with status, one
  without) — divide the line-count delta by 2 to count requests.
- `raw -j` needs valid JSON with a `"command"` key — in a .ps1 script,
  single-quote a literal or read the JSON from a file; inline `$var`
  interpolation still requires a .ps1 script.
- Dedup replay arrives as plain JSON on the SSE path (Content-Type
  dispatch) — the client prints it and exits by the embedded status.
- Exit code 2 is reserved for connection/auth failures
  (`sseclient.go:22-28`); a `--pid` that matches nothing is exit 1.
