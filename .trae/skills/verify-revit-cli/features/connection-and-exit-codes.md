# Connection commands and the exit-code contract

## Sub-features

- `ping` — builtin that forwards `{"command":"ping"}` over SSE; consumes
  `accepted` (prints `task_id:` to stderr) → `progress` (stderr, `\r`) →
  `completed` (result JSON on stdout, exit 0) or `failed` (stderr; exit 4,
  or exit 3 when the message is exactly `"Revit API execution timed out."`).
- `health` / `status` — plain GET pretty-print JSON, exit 0; these never use
  the SSE path and never produce codes 2-4 — connect failure is exit 1.
- Dead port — `ping` (SSE transport failure) exits **2**; `health`/`status`
  (builtin GET path) exit **1**. Error text goes to stderr on both.
- Auth rejection — a 401 on the schema fetch (`dispatchDynamic`) or on
  execute (SSE path, `ping` included) prints key guidance and exits **2**.
  The builtins `health`/`status`/`raw-mode`/`commands`/`schema`/`task` go
  through `httpGet` → `exitCodeFromStatus`, which maps **any** non-2xx to
  **1** with no guidance (`builtin/system.go:46-51`). The mock does not
  enforce auth; prove these paths only against a real bridge with a wrong
  key, or skip and record them as untested.
- Usage errors on the global flags — `--timeout 0` → exit 1 before any
  request; `--revit 2100` (outside 2019-2099) → exit 1.

## How to get to it (user POV)

The first thing after install: point the CLI at a bridge and run `ping`;
`health`/`status` are the diagnostics when ping misbehaves.

## Driving it with the terminal + mock bridge

```powershell
Run "ping"   @("--url","http://127.0.0.1:$PORT","ping")    # exit 0; stdout result JSON
Run "health" @("--url","http://127.0.0.1:$PORT","health")  # exit 0; {"status":"ok"}
Run "status" @("--url","http://127.0.0.1:$PORT","status")  # exit 0; {"server_running":true,...}
Run "ping-dead"   @("--url","http://127.0.0.1:25999","ping")   # exit 2
Run "health-dead" @("--url","http://127.0.0.1:25999","health") # exit 1
Run "status-dead" @("--url","http://127.0.0.1:25999","status") # exit 1
Run "timeout-0"   @("--url","http://127.0.0.1:$PORT","--timeout","0","ping")     # exit 1
Run "revit-2100"  @("--revit","2100","ping")                                    # exit 1
Run "revit-auto"  @("--revit","2022","ping")                                    # exit 0 (mock registers as 2022)
```

Timeout path (exit 3): start a second mock with `--slow-command ping`, then
`ping` against it returns a `failed` event carrying the exact bridge timeout
message → exit 3. Failure path (exit 4): `--fail-command create_wall` mock +
a **complete** create_wall drive (`--start_x 0 --start_y 0 --end_x 5000
--end_y 0 -l 3001`); an incomplete one exits 1 on the required-flag check
before any request reaches the mock.

## Gotchas

- `ping` prints the result on stdout after a `\n`; progress lines live on
  stderr with `\r` — capture with `2>&1` and assert on the exit code, not on
  stdout purity.
- `127.0.0.1` always works; `localhost` works only when the dialer's
  `[::1]` attempt is refused slowly enough for the IPv4 fallback — the
  mock binds **127.0.0.1 only**. Keep `--url` drives on `127.0.0.1`.
- Exit codes are the contract (`sseclient.go:22-28`); any doc or assertion
  that says "failed = exit 1" is wrong — that is usage-error territory.
  But "non-2xx on a builtin GET = exit 1" is also the contract.
