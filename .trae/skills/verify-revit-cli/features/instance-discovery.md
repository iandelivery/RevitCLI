# Instance discovery, list enrichment, targeting

## Sub-features

- Registry discovery — reads `revit-*.json` files from
  `<data-dir>\instances`; each carries `pid, version, port, document?,
  started_at, hostname, commands_count, api_key?`.
- Liveness + dead-PID cleanup — every discovered PID is checked via
  `tasklist`; a dead PID means the registry file is **deleted** and the
  entry dropped. A malformed registry file is skipped but NOT deleted.
  If `tasklist` itself fails the PID counts as alive. Sort: version
  desc, pid asc.
- `list` enrichment — per instance, concurrent `GET /api/documents`
  (2 s timeout, auth header) merges `documents`, `active_path`,
  `reachable`. DOCUMENT column: unreachable → `- (unreachable)`;
  active path ( + ` (N open)` when more docs); docs without active →
  `- (N open)`; else `-`. `--json` prints the enriched array with the
  registry's `api_key` **stripped** (blanked before encoding, `omitempty`
  drops the key — fixed PR #122, live 2026-11-26): the only keys are
  `pid, version, port, started_at, hostname, commands_count, reachable`
  plus `document`/`documents`/`active_path` when known. The registry
  FILE still holds the key and `APIKeyFor` still reads it for auth.
  Empty list still exits 0 with a plain-text notice, also under `--json`.
- Targeting precedence — `--url` > `--pid` (exact registry match) >
  `--revit` (first matching version, no ambiguity error) > auto-select when
  exactly one live instance. Multiple live instances without a selector →
  instance table on **stdout** (`fmt.Printf`), only the "Multiple Revit
  instances detected." header and the "Use --pid ..." hint on stderr, exit 1;
  zero instances → guidance + exit 1. `--pid` with no match → exit 1 "No
  running Revit instance with PID N found."; `--revit` outside 2019-2099
  or a non-positive `--pid` → exit 1 before discovery.

## How to get to it (user POV)

"Which Revits are running and which will my commands hit?" — `list` answers
it; the selectors pin one.

## Driving it with the terminal + mock bridge

The mock writes its own registry file (live PID). Plant extra entries for
the guard journeys:

```powershell
Run "list"      @("list")                          # mock row; DOCUMENT shows MockModel.rvt (live /api/documents)
Run "list-json" @("list","--json")                 # enriched JSON array incl. reachable:true; NO api_key key on any row
Run "ping-auto" @("ping")                          # exit 0 — single-instance auto-selection
Run "ping-pid"  @("--pid","<mockpid>","ping")      # exit 0
Run "ping-revit" @("--revit","2022","ping")        # exit 0 (mock registers as 2022)
Run "ping-pid-miss" @("--pid","999999","ping")     # exit 1 "No running Revit instance with PID 999999 found."

# api_key canary (fixed PR #122, live 2026-11-26): plant a registry file
# with a key; PID 4 is alive, port 1 has no listener -> reachable:false
Set-Content "$env:REVIT_CLI_DATA_DIR\instances\revit-2023-4.json" '{"pid":4,"version":2023,"port":25918,"started_at":"2026-11-26T00:00:00Z","hostname":"localhost","commands_count":0,"api_key":"LEAK-CANARY"}'
Run "list-json-canary" @("list","--json")
# output contains NEITHER "LEAK-CANARY" NOR "api_key"; the registry file still contains the canary
Remove-Item "$env:REVIT_CLI_DATA_DIR\instances\revit-2023-4.json"

# multi-instance guard: PID 4 (System) is always alive
Set-Content "$env:REVIT_CLI_DATA_DIR\instances\revit-2020-4.json" '{"pid":4,"version":2020,"port":25913}'
Run "ping-multi" @("ping")                          # exit 1; table on stdout, header + "Use --pid" hint on stderr
Remove-Item "$env:REVIT_CLI_DATA_DIR\instances\revit-2020-4.json"

# dead-PID cleanup
Set-Content "$env:REVIT_CLI_DATA_DIR\instances\revit-2021-999999.json" '{"pid":999999,"version":2021,"port":25914}'
Run "list-stale" @("list")                          # 2021 row absent AND the file is gone from instances\
Test-Path "$env:REVIT_CLI_DATA_DIR\instances\revit-2021-999999.json"   # False

# unreachable enrichment: restart nothing — point a fake-but-alive entry at a dead port
Set-Content "$env:REVIT_CLI_DATA_DIR\instances\revit-2022-4.json" '{"pid":4,"version":2022,"port":25919}'
Run "list-unreachable" @("list")                    # DOCUMENT shows `- (unreachable)` for that row
Remove-Item "$env:REVIT_CLI_DATA_DIR\instances\revit-2022-4.json"
```

With `--no-documents` on the mock, `list` shows `- (unreachable)` for the
mock itself and still exits 0 — the pre-documents bridge behavior.

## Gotchas

- Fake registry entries must use a **live** PID (4 on Windows) or they are
  cleaned before the assertion runs.
- `--revit` picks the first match silently when several instances share a
  version — pin with `--pid` in every drive that mutates state.
- Discovery happens inside the CLI process: isolation depends on
  `REVIT_CLI_DATA_DIR` being set in the exact shell that runs the exe.
  Without it the data dir cascades `%LOCALAPPDATA%\revit-cli` →
  `%USERPROFILE%\.revit-cli` → exe dir (creating dirs as a side effect).
- `list --json` prints plain text (not JSON) when zero instances — guard
  the `ConvertFrom-Json`.
- `client/internal/client/discovery/instances.go` holds an unused
  `ResolveBaseURL` that errors on `--revit` ambiguity; the live path is
  `instance.ResolveURL`, which picks the first match. Do not "fix" the
  doc to match the dead code.
