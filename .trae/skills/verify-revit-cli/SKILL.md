---
name: verify-revit-cli
description: Drive the revit-cli Go client (client/skills/revit-cli/revit-cli.exe) the way a user does and prove behavior with evidence — built-ins, instance discovery, schema discovery, dynamic command execution over SSE, units flag, semantic exit codes. Use when asked to verify, prove, regression-test, or demo revit-cli client behavior against a mock bridge (no Revit needed) or a real bridge.
---

# Verify revit-cli (the Go client)

> **Sync**: fully regenerated 2026-11-23 from a source interview of main
> `11e58b3` (client fact sheet: main.go, builtin/, discovery/, instance/,
> sseclient.go, auth/, build.ps1). No facts inherited from prior skill
> versions; the harness (`helpers/mock_bridge.py`) was rewritten to the
> bridge's route/SSE/units protocol as read from the bridge source.

The app under test is the Go CLI client `client\skills\revit-cli\revit-cli.exe`
(built by `client\build.ps1`, version stamped from `git describe`). It is a
one-shot command: parse args → resolve a bridge URL → HTTP/SSE → print → exit.

## Ground truth (from source, cite when asserting)

**Exit codes** (`internal/client/sseclient.go:22-28`): `0` success; `1` usage
error (unknown command, bad flags, missing required params, instance
resolution failure); `2` bridge unreachable / auth rejected; `3` timeout
(`"Revit API execution timed out."`); `4` command failed (any other
`failed`/error response).

**Global flags** (`cmd/revit-cli/main.go:71-131`, manual parsing, both
`--flag value` and `--flag=value`): `--url` (highest precedence) > `--pid` >
`--revit` > single auto-discovered instance > exit-1 guidance table
(`internal/instance/discovery.go:112-155`). `--timeout` seconds (default 120,
min 1). `--no-wait` (bare token; sends `async:true`, drops SSE Accept,
prints `task_id:` and exits 0). `--request-id` (sent as `X-Request-Id`;
empty → auto-generated per invocation). `--units` repeatable: `project`
sentinel or `kind=unit` pairs; mixing both or malformed → exit 1 AFTER URL
resolution + schema fetch, BEFORE any execute request (`main.go:210-236,
249-254`).

**Isolation**: everything the client persists goes under `REVIT_CLI_DATA_DIR`
when set — instance registry scan (`instances\revit-*.json`), schema cache
(`cache\[servers\]schema-cache*.json` + `schema-etag*.txt`), auth keys
(`servers\apikey_*.txt`). Always point it at a scratch dir.

## Launch

From the repo root, once per session:

```powershell
# 1. Build the binary under test (go vet + build + move into skills dir).
& "d:\Repos\revit-cli-opensource\client\build.ps1"     # throws on failure

# 2. Scratch data dir + evidence dir.
$STAMP  = "verify-<date>"                               # unique per run
$SCRATCH= "d:\Repos\revit-cli-opensource\dist\verify-scratch\$STAMP"
$EV     = "d:\Repos\revit-cli-opensource\dist\verify-evidence\$STAMP"
New-Item -ItemType Directory -Force -Path "$SCRATCH","$EV" | Out-Null

# 3. Start the mock bridge as a LONG-RUNNING background process (it blocks).
#    Run it via a .ps1 script file (inline `$env:` in a shell command gets
#    eaten by the outer shell) with:
$env:REVIT_CLI_DATA_DIR = $SCRATCH
python "<repo>\.trae\skills\verify-revit-cli\helpers\mock_bridge.py" `
    --registry-dir "$SCRATCH\instances" `
    --log-file "$EV\bridge.log" `
    --pidfile "$SCRATCH\bridge.pid"
# stdout: "mock bridge listening on http://127.0.0.1:<port> pid=<n>"
```

Mock ports default to the first free port in **25911-25919** (outside the
real bridge ranges 5011-5049, so a mock can never be mistaken for Revit).
The mock also writes a registry file `revit-2022-<pid>.json` so
auto-discovery works. Parse the port from the listening line or the registry
file — never assume.

**Mock lifetime rule**: a mock started inside a short-lived shell command
dies with that command. Always launch it as a background/long-running
command (in Trae: Shell tool with `run_in_background`), and kill it via its
pidfile in Cleanup — never by process name.

## Doctor

Read-only; run before the first drive and after anything surprising:

```powershell
$cli = "d:\Repos\revit-cli-opensource\client\skills\revit-cli\revit-cli.exe"
& $cli --version                                # banner + v<tag>[-N-g<sha>]
& $cli configure check                          # "Data directory:" MUST equal $SCRATCH
& $cli --url "http://127.0.0.1:$PORT" health    # {"status":"ok"} exit 0
Get-ChildItem "$SCRATCH\instances"              # mock registry file present
```

If `Data directory:` is not the scratch dir, `REVIT_CLI_DATA_DIR` was lost
between commands — re-set it; do not drive against the real data dir.

## Drive

Harness = the terminal. Capture every drive with a transcript helper
(put this in a .ps1 file, not inline):

```powershell
function Run([string]$name, [string[]]$cliArgs) {
  $out  = & $cli @cliArgs 2>&1
  $code = $LASTEXITCODE
  $out  | Set-Content "$EV\$name.txt"
  Add-Content "$EV\$name.txt" "`n[exit code: $code]"
  Write-Host "==> $name exit=$code"
  return ,@($out, $code)
}
```

The strongest oracle: the mock's `completed` result **echoes the exact
request payload** — parameters, top-level `units`, `dry_run`, `request_id` —
and `bridge.log` records one line per HTTP request (method, path, body
fields, X-Request-Id). Cross-check transcripts against the log.

Cover the mapped features (one drive each minimum, see `features/`):

1. `features/connection-and-exit-codes.md` — ping/health/status + the full
   exit-code matrix incl. dead-port behavior (ping=2, health=1).
2. `features/instance-discovery.md` — registry discovery, list enrichment,
   dead-PID cleanup, selector precedence, multi-instance guard.
3. `features/schema-discovery.md` — commands/catalog/schema, cache TTL,
   ETag/304, stale fallback, dynamic `--help` with unit kinds, did-you-mean.
4. `features/dynamic-commands.md` — flag parsing, required validation,
   dry-run, `--units` injection and pre-connect rejection, SSE execution,
   `--no-wait` + `task -ti`, `--request-id` dedup, failure exit codes.
5. `features/configure-and-skill.md` — the read-only paths only
   (`configure check/port`, `skill show`, `skill install --list`).

Against a **real** bridge, the same drives work with `--pid`/`--revit`
instead of `--url`; only run document-mutating commands with `--dry-run`
first, and never run `configure setup/teardown` or `skill install` outside
scratch targets.

## Evidence

Everything lands in `dist\verify-evidence\<stamp>\` (gitignored, survives
cleanup):

| File | Proves |
|------|--------|
| `NN-*.txt` | Full transcript per drive: stdout+stderr + `[exit code: N]` |
| `bridge.log` | What the client actually sent (paths, bodies, request ids) |
| cache/registry listings | Client-side state changes (schema cache, registry cleanup) |

Proof standards: drive the **built binary** (not `go run`); assert exit codes
explicitly; prove cache behavior by request-count deltas in `bridge.log`,
not by output identity; prove discovery cleanup by the registry file
disappearing; the only mock is at the HTTP boundary.

## Cleanup

Kill what you started; keep the evidence:

```powershell
$mockPid = Get-Content "$SCRATCH\bridge.pid" -ErrorAction SilentlyContinue
if ($mockPid) { Stop-Process -Id $mockPid -Force -ErrorAction SilentlyContinue }
Remove-Item -Recurse -Force $SCRATCH -ErrorAction SilentlyContinue
Get-ChildItem $EV        # evidence must still be here
```

Run this after every failed iteration too.

## Helpers

- `helpers/mock_bridge.py` — the mock bridge (Python 3.8+, stdlib only).
  Implements the routes the client touches, per the bridge source:

  ```powershell
  python mock_bridge.py [--port N]          # default: first free in 25911-25919
                        [--fail-command NAME]   # that command returns a failed event
                        [--no-documents]        # 404 on /api/documents
                        [--slow-command NAME]   # that command exceeds the client budget -> timeout exit 3
                        [--registry-dir DIR] [--revit-version 2022]
                        [--log-file F] [--pidfile F]
  ```

  Routes: `/api/health`, `/api/status`, `/api/identity`, `/api/auth/status`,
  `/api/documents` (404 with `--no-documents`), `/api/llms.txt`,
  `/api/commands` (ETag/304), `/api/commands/{name}` (200/304/404),
  `/api/catalog`, `/api/task[/{id}]`, `/api/raw-mode` (GET/POST),
  `POST /api/execute` — SSE frames `accepted`/`progress`/`heartbeat`/
  `completed`/`failed`, sync JSON when no SSE Accept, `async:true` pending
  receipt, `X-Request-Id` dedup replay, units-contract validation with
  allowed-list errors, `dry_run` echo without `element_id`.

  Command schemas served: `ping`, `create_wall` (unit_kind length params +
  height default 3000), `get_units` (serves the project/canonical/allowed
  matrices), `elements`, `delete_elements` — plus whatever `--fail-command`
  / `--slow-command` name you pass.

## Known environment traps

- PowerShell inline `$var`/`$env:` in a shell command gets eaten by the
  outer shell — every script with variables goes in a `.ps1` file.
- A function that both `Write-Output`s diagnostics and returns a value
  pollutes the return — use `Write-Host` for progress lines.
- The schema cache TTL is 30 min: a second `commands` within TTL issues no
  request (prove via log line count), `--refresh` forces revalidation (304
  when the ETag matches).
