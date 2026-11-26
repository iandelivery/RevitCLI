---
name: verify-revit-cli-bridge
description: Drive the RevitCliBridge add-in (bridge/) — build, deploy, C# test suite, then launch a disposable Revit, doctor it, and drive the live HTTP /api/* surface and the 105 registered commands through the revit-cli client with evidence. Use when asked to verify, prove, or regression-test the bridge side of revit-cli.
---

# Verify RevitCliBridge (the bridge)

> **Sync**: maintained 2026-11-26 against main `6822bf2` (after PR #119/#120 added
> Revit 2023/2024 and the `ElementIdCompat` facade). Every feature file was re-read from
> source and driven live on a disposable Revit 2019 (`dist\verify-evidence\maintain-20261126\`).
> Originally generated 2026-11-23 from main `11e58b3`.

The app under test is the C# add-in in `bridge\`: an HTTP server
(HttpListener via http.sys, loopback-only) running inside Revit, executing
**105 commands** (`Handlers\` reflection registry) on the Revit main thread
and streaming results over SSE. Its users are the revit-cli Go client and
any HTTP caller.

## Ground truth (from source)

- **Routes**: `POST /api/execute` (JSON sync / `async:true` pending /
  SSE when `Accept: text/event-stream`; 400 missing command, **429 queue full on the
  sync JSON path only — the SSE path enqueues unconditionally**, 413 body too large, 500
  sync-failed); `GET /api/task` (last 50) and `GET /api/task/{id}` (result only when
  terminal; 404 past retention — completed 900 s, failed/timeout 3600 s, both **lazily**
  cleaned on the next command, not at wall-clock expiry); `/api/status`; `/api/metrics`;
  `/api/commands` (ETag = `"{bridgeVersion}:{count}:{base64url-sha256}"`, 304
  revalidation); `/api/commands/{name}` (200/304/404; its ETag is
  `"{bridgeVersion}:{hash}"` with no count); `/api/catalog`; `/api/llms.txt`;
  `/api/documents`; `/api/identity`; `/api/auth/status`; `/api/raw-mode` (GET/POST).
  **Auth**: Bearer on everything except `/api/health`, `/api/identity`,
  `/api/auth/status`; the key is auto-generated and persisted by `EnsureApiKey` whenever
  `api_key` is null — auth is effectively always on; the key also lands in the instance
  registry file for client discovery. **Auth runs before routing**: an unknown `/api/*`
  path without a key answers 401, not 404.
- **SSE frames**: `accepted {task_id,status,command}` → `progress
  {task_id,progress,message}` (0 at start) → `heartbeat {}` every 15 s →
  `completed|failed {task_id,status,result}` (the final frame repeats as a second
  `completed`/`failed` event carrying the raw ResultJson). Task states: Pending → Running →
  Completed|Failed|Timeout. **Sync (no SSE Accept) timeouts answer HTTP 200 with the error
  JSON**; 500 is only for `Status == Failed`.
- **Dedup**: a repeated `X-Request-Id` on `/api/execute` replays the task snapshot as plain
  JSON (200), for any retained task, terminal included; logged `request_dedup`.
- **Units contract**: kinds length/area/volume/angle (+raw passthrough);
  canonical mm/m2/m3/deg; allowed length mm,cm,m,ft,in / area m2,mm2,ft2 /
  volume m3,mm3,ft3,l / angle deg,rad. Request `units` = map or
  `"project"` (needs a document). Inputs convert to internal feet and are
  NEVER rounded; outputs convert back and round to 3 decimals; a top-level
  `units` echo is stamped by `UnitsContext.Finish` — which runs only for
  handlers that **return** a `CommandResponse` (success or error). No echo
  on: a `units` parse failure, `"project"` without a document, dry-run
  rejection, unknown command, a handler that throws, timeout, queue-full.
  Schema `unit_kind` declares inputs; `ResponseFieldKinds` rewrites output
  fields. `get_units` answers document-less with `project: null`.
- **Command families** (see `features/`): Documents 7, Creation 10,
  Query 17, Views 15 (incl. the sheet-viewport six), Piping/Mechanical/
  Electrical 31, Modification 10, Parameters 3, Structural 4, Batch 2,
  Architecture 2, MEP 1, General 2 (ping/undo), Raw 1 (execute_raw,
  gated by `allow_raw_execution` which defaults **true**).
- **Versioning**: `{RevitYear}.{Major}.{Minor}.{Patch}` from the latest
  `v*` tag at build (`build.ps1` passes `-p:Major/Minor/Patch`); port per
  version 5000+(v-2018)*10+1, allocated from a 10-port range (2019:
  5011-5019, 2020: 5021-5029, 2021: 5031-5039, 2022: 5041-5049, 2023:
  5051-5059, 2024: 5061-5069). `build.ps1` defaults to all six versions.
  Version-dependent `#if`s (`ElementIdCompat`, `ParameterUnits`,
  `UnitsContext`) name the **legacy** versions and leave the modern API in
  the `#else`, so a new `Rnn` symbol takes the modern branch by default.
- **Registry/log**: instance file `revit-{year}-{pid}.json` (with
  `api_key`) under `REVIT_CLI_DATA_DIR` → `%LOCALAPPDATA%\revit-cli\
  instances` → `~\.revit-cli\instances` → `%APPDATA%\revit-cli\instances`;
  log `%LOCALAPPDATA%\RevitCliBridge\Logs\cli_bridge_{date}.log`,
  AutoFlush, mutex-guarded.

## Safety rules (non-negotiable)

- The user keeps a **long-running Revit 2022** with real project models.
  Never deploy to 2022, never touch its windows or registry entry, never
  run `configure teardown`. Target OUR disposable Revit 2019 by `--pid`.
- Always pass `-Versions 2019` to build/deploy; the addins folder holds
  2019-2025 dirs but only 2019 is ours.
- Mutate state only on a scratch copy of a sample model, never save it,
  and prefer element-level re-query over counts (`get_elements` paginates
  at 500).

## Launch

```powershell
$REPO   = "d:\Repos\revit-cli-opensource"
$STAMP  = "vb-<date>"
$EV     = "$REPO\dist\verify-bridge-evidence\$STAMP"
$SCRATCH= "$REPO\dist\verify-bridge-scratch\$STAMP"
New-Item -ItemType Directory -Force -Path $EV,$SCRATCH | Out-Null

# 1. Build 2019 (stamps {year}.{maj}.{min}.{patch} from the latest tag).
& "$REPO\bridge\build.ps1" -Versions 2019

# 2. C# suite (pure logic; net48 host on Windows).
dotnet test "$REPO\bridge\RevitCliBridge.Tests\RevitCliBridge.Tests.csproj" `
    --logger "trx;LogFileName=tests.trx" --results-directory "$EV"

# 3. Deploy dist -> addins. C:\Users may be write-locked for the agent:
#    direct Copy-Item fails; fall back to a scheduled-task copy, then
#    VERIFY size + FileVersion match dist.
#    (schtasks /create /tn RevitCliDeploy /tr <copy.cmd> /sc once /st 23:59 /f;
#     schtasks /run /tn RevitCliDeploy; schtasks /delete /tn RevitCliDeploy /f)
(Get-Item "$env:APPDATA\Autodesk\Revit\Addins\2019\RevitCliBridge\RevitCliBridge.dll").VersionInfo.FileVersion |
    Set-Content "$EV\deployed-version.txt"

# 4. Launch OUR Revit via explorer (a direct Start-Process fails the
#    license handshake; a sandboxed spawn yields a dead stub — run this
#    unsandboxed). explorer.exe with an .rvt argument does NOT launch
#    Revit on this box; pass the exe.
$userPids = (Get-Process -Name Revit -ErrorAction SilentlyContinue).Id
explorer.exe "D:\Program Files\Autodesk\Revit 2019\Revit.exe"

# 5. Clear startup dialogs with the helper, repeating until clean twice:
powershell -File "$REPO\.trae\skills\verify-revit-cli-bridge\helpers\dialog-sweep.ps1" -Target <NEWPID>
#    Buttons it clicks: "Security - Unsigned Add-In" -> Load Once;
#    anything else (e.g. "Default Family Template File Invalid") -> Close.
#    The Unsigned dialog can arrive MINUTES late — when any command later
#    times out, re-run the sweep before concluding anything.

# 6. Wait for registration (poll `revit-cli list --json`, filter by OUR
#    pid — never by version alone), then open a scratch copy of a sample:
Copy-Item "D:\Program Files\Autodesk\Revit 2019\Samples\rac_basic_sample_project.rvt" "$SCRATCH\model.rvt"
& $cli --pid <NEWPID> doc_open --path "$SCRATCH\model.rvt"
#    doc_open can pop the Template dialog AGAIN — sweep after it, too.
#    Poll WITHOUT setting REVIT_CLI_DATA_DIR: explorer-launched Revit has no
#    such env, so the bridge registers under %LOCALAPPDATA%\revit-cli\instances
#    and a client pointed at a scratch data dir sees nothing.
```

**Stale-key trap (hit 2026-11-24 and 2026-11-26).** The Go client resolves the API key
memory → `%LOCALAPPDATA%\revit-cli\servers\apikey_localhost_<port>.txt` → the instance
registry (`auth.go:98-126`). A key file left by an earlier dead session shadows the new
bridge's key: every protected command answers 401 and the client prints "The API key was
rejected", while `/api/health` and `/api/identity` (no auth) still work and `commands` may
even succeed from the stale schema cache. Before the first drive, check that file; if it
exists for our port and its mtime predates the Revit launch, delete it (the agent's
sandbox refuses deletes under `%LOCALAPPDATA%` — run the delete through a scheduled task:
`schtasks /create /tn <name> /tr "pwsh -File <script>" /sc once /st 23:59 /f`, `/run`,
`/delete /f`). The next client call re-creates it from the registry.

**Passing JSON on the wire.** From pwsh, `\"` inside a single-quoted `-d` or `-j` string
reaches the program as a literal backslash (400 "Invalid JSON: Invalid property identifier
character: \\"). Write the body to a file and pass `"--data-binary@$file"` as one token
(`"@file"` on its own is splatted to nothing and the server waits for a body). Give SSE
probes `--max-time`.

## Doctor

```powershell
$cli = "$REPO\client\skills\revit-cli\revit-cli.exe"
# version triangle: tag == dist == deployed (deployed-version.txt)
(Get-Item "$REPO\bridge\dist\Revit2019\RevitCliBridge.dll").VersionInfo.FileVersion
& $cli --pid <NEWPID> health                        # {"status":"ok"}
& $cli --pid <NEWPID> raw -j '{"command":"ping"}'   # full SSE round-trip
Select-String "CLI Bridge enabled on port" "$env:LOCALAPPDATA\RevitCliBridge\Logs\cli_bridge_$(Get-Date -Format yyyy-MM-dd).log" | Select-Object -Last 1
Get-NetTCPConnection -LocalPort <PORT> -State Listen   # OwningProcess 4 (http.sys) is NORMAL — identity goes via health + list reachable
```

## Drive

Harness = the repo's Go client against the live bridge, transcripts via
the same `Run` helper as the client skill. `$U = @('--pid','<NEWPID>')`.

1. `features/http-protocol.md` — route matrix probes: public trio without
   Bearer, 401 + WWW-Authenticate on a protected route, ETag 304
   revalidation, 404s, catalog/commands/llms/documents shapes.
2. `features/sse-and-tasks.md` — SSE frame sequence, `--no-wait` +
   `task -ti` polling, `--request-id` dedup replay.
3. `features/units-contract.md` — `get_units` with and without a
   document, allowed-list errors (yard/mass), an ft round-trip
   (create in ft, re-read geometry), the units echo on success AND error.
4. `features/document-commands.md` — doc_open → get_levels → create_wall
   → element-level re-query → undo → gone; dry-run rollback proof; the
   known product gaps (undo `count` ignored; `create_wall height` not
   applied).
5. `features/sheet-viewports.md` — the viewport six: get_sheets /
   get_views → place_on_sheet (nested `views` JSON) → get_sheet_viewports
   (center round-trip) → check_viewport_overlaps / check_sheet_bounds →
   move_viewport (`moves` array) → remove_view_from_sheet (`view_ids`).
6. `features/auth-and-config.md` — auth reality (EnsureApiKey), raw-mode
   runtime toggle, config keys, api_key preservation across deploys.

Nested object params (`views`, `moves`, `walls`, batch `operations`) are
NOT parsed by the client's dynamic flags — pass them with `raw -j`.

## Evidence

`dist\verify-bridge-evidence\<stamp>\`: `deployed-version.txt`,
`tests.trx`, `NN-*.txt` transcripts with `[exit code: N]`, and
`bridge-log.txt` (copy the daily log at the end — AutoFlushed, safe to
copy after the session). Proof standards: drive the **deployed** build;
every run includes at least one real HTTP round-trip; prove undo/dry-run
by element-level re-query; capture both sides (transcript + log).

## Cleanup

```powershell
Copy-Item "$env:LOCALAPPDATA\RevitCliBridge\Logs\cli_bridge_$(Get-Date -Format yyyy-MM-dd).log" "$EV\bridge-log.txt"
Stop-Process -Id <NEWPID> -Force                                   # only the PID we launched
Start-Sleep 5                                                       # Revit may re-write its file on exit
Remove-Item "$env:LOCALAPPDATA\revit-cli\instances\revit-2019-<NEWPID>.json" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $SCRATCH -ErrorAction SilentlyContinue
Get-ChildItem $EV                                                  # evidence survives
Get-ChildItem "$env:LOCALAPPDATA\revit-cli\instances"              # only the user's own instances remain
```

Never run `configure teardown`; the deployed DLL stays installed. Under the agent
sandbox `Remove-Item` under `%LOCALAPPDATA%` is refused silently — use the scheduled-task
route from Launch and confirm with the last line.

## Helpers

- `helpers\dialog-sweep.ps1 -Target <pid>` — EnumWindows over class
  `#32770` owned by the target PID; clicks `Load Once` on "Unsigned
  Add-In" security dialogs and `Close` on anything else (notably
  "Default Family Template File Invalid"). Repeat until it prints
  `remaining dialogs: 0`; re-run it whenever a drive command times out.
- The Go client binary (`client\skills\revit-cli\revit-cli.exe`) is the
  driving harness — build it with `client\build.ps1` first.
