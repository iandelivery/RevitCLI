# Instance discovery, list enrichment, targeting

## Sub-features

- Registry discovery — reads `revit-*.json` files from
  `<data-dir>\instances`; each carries `pid, version, port, document?,
  started_at, hostname, commands_count, api_key?`. Malformed JSON files are
  skipped (not deleted).
- Liveness + dead-PID cleanup — every discovered PID is checked via
  `tasklist`; a dead PID means the registry file is **deleted** and the
  entry dropped. If `tasklist` itself fails, the PID counts as alive.
  Sort: version desc, pid asc.
- `list` enrichment — per instance, concurrent `GET /api/documents`
  (2 s timeout, auth header) merges `documents`, `active_path`,
  `reachable` (both omitted when unreachable). DOCUMENT column:
  unreachable → `- (unreachable)`; active path ( + ` (N open)` when more
  docs; the mock reports `D:\models\MockModel.rvt` — assert on a substring);
  docs without active → `- (N open)`; else `-`. `--json` prints the enriched
  array — which **embeds every registry field, `api_key` included** (a
  product/security note, 2026-11-26: do not paste `list --json` into
  tickets); empty list still exits 0 with a plain-text notice.
- Targeting precedence — `--url` > `--pid` (exact registry match) >
  `--revit` (first matching version, range 2019-2099, no ambiguity error) >
  auto-select when exactly one live instance. Multiple live instances
  without a selector → **instance table on stdout** (only the header and
  the `Use --pid` hint go to stderr) + exit 1; zero instances → guidance +
  exit 1.
- API key resolution — memory → `<data-dir>\servers\apikey_<host>_<port>.txt`
  → registry (`APIKeyFor`, matched by port). The disk file wins over the
  registry and is memoized for the process, so a stale key file from an
  earlier session shadows the registry key and every protected command
  answers 401.

## How to get to it (user POV)

"Which Revits are running and which will my commands hit?" — `list` answers
it; the selectors pin one.

## Driving it with the terminal + mock bridge

The mock writes its own registry file (live PID). Plant extra entries for
the guard journeys:

```powershell
Run "list"      @("list")                          # mock row; DOCUMENT shows MockModel.rvt (live /api/documents)
Run "list-json" @("list","--json")                 # enriched JSON array incl. reachable:true
Run "ping-auto" @("ping")                          # exit 0 — single-instance auto-selection
Run "ping-pid"  @("--pid","<mockpid>","ping")      # exit 0
Run "ping-revit" @("--revit","2022","ping")        # exit 0 (mock registers as 2022)

# multi-instance guard: PID 4 (System) is always alive
Set-Content "$env:REVIT_CLI_DATA_DIR\instances\revit-2020-4.json" '{"pid":4,"version":2020,"port":25913}'
$out = & $cli ping 2> "$EV\ping-multi.err"; "exit=$LASTEXITCODE"
# exit 1; $out (stdout) holds the instance table; the .err file holds header + "Use --pid"
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
- A `revit-2020-4.json` left behind by an aborted session makes every
  later `ping` a multi-instance exit 1 — list `instances\` first when a
  previously working drive suddenly exits 1.
- `--revit` picks the first match silently when several instances share a
  version — pin with `--pid` in every drive that mutates state.
- Discovery happens inside the CLI process: isolation depends on
  `REVIT_CLI_DATA_DIR` being set in the exact shell that runs the exe.
- `list --json` prints plain text (not JSON) when zero instances — guard
  the `ConvertFrom-Json`.
- `list --json` leaks `api_key` for any instance whose registry carries one
  (real bridges do). Never paste it into evidence that leaves the machine;
  redact the field first.
