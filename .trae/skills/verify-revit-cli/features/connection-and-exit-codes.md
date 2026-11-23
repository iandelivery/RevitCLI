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
- Auth rejection — a 401 on schema fetch or execute prints key guidance and
  exits 2 (mock does not enforce auth; prove this path only against a real
  bridge with a rotated key, or skip and record it as untested).

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
```

Timeout path (exit 3): start a second mock with `--slow-command ping`, then
`ping` against it returns a `failed` event carrying the exact bridge timeout
message → exit 3. Failure path (exit 4): `--fail-command create_wall` mock +
any create_wall drive.

## Gotchas

- `ping` prints the result on stdout after a `\n`; progress lines live on
  stderr with `\r` — capture with `2>&1` and assert on the exit code, not on
  stdout purity.
- Both loopback spellings work (`localhost` and `127.0.0.1`); the real
  bridge binds both prefixes, and so does the mock.
- Exit codes are the contract (`sseclient.go:22-28`); any doc or assertion
  that says "failed = exit 1" is wrong — that is usage-error territory.
