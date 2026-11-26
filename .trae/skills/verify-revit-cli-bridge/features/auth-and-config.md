# Auth, raw-mode, and config

## Sub-features

- Auth reality — `EnsureApiKey` generates and persists a key whenever
  `api_key` is null/empty, so Bearer auth is effectively **always on**.
  Scope: every `/api/*` route except `/api/health`, `/api/identity`,
  `/api/auth/status`. The key is 43 chars (`[A-Za-z0-9_-]`, 32 random
  bytes URL-safe Base64). It is written BOTH to the instance registry
  file (how the client auto-discovers it) and to
  `<addins>\<year>\RevitCliBridge\.config\cli_bridge_setting.json`
  (atomic write). A wrong key → 401 + `WWW-Authenticate: Bearer`; the
  client prints key-sync guidance (`configure setup`) and exits 2 on
  execute paths — but `health`/`status`/`task` answer a 401 with exit 1.
- Raw execution gate — `execute_raw` (aliases `raw_exec`, `eval`) is
  enabled by default (`allow_raw_execution: true` in code AND shipped
  config); `raw-mode --disable` / `--enable` toggles it in-memory for the
  session (resets on Revit restart). Refusal is a normal failed command
  (client exit 4, message "Raw execution is disabled. Set
  'allow_raw_execution: true' in cli_bridge_setting.json to enable.").
- Plugin trust — unsigned third-party DLLs in `CliBridgePlugins\` are
  rejected unless `allow_unsigned_plugins: true`; `trusted_publishers`
  restricts to known subjects when set.
- Config keys — `schema_version`, `enabled`, `port` (seed only under
  `auto_port`; the deployed base port is `5000+(year-2018)*10+1`),
  `auto_port`, `timeout_seconds` (180; overridable per request as
  `timeout_seconds`), `max_command_queue_size` (100 → 429),
  `task_retention_seconds` (900, completed tasks only; failed/timeout
  retention is fixed 3600 s), `allow_raw_execution`,
  `max_request_body_size_bytes` (10485760 → 413), `api_key`,
  `allow_unsigned_plugins`, `trusted_publishers`. There is no `host`
  key. The config is a singleton: edits need a Revit restart.
- Deploy key preservation — `build.ps1 -Deploy` (and `configure setup`)
  read the deployed `api_key` before overwriting the config and re-add
  it; **every other key resets to the template defaults** (six keys +
  `api_key`), so a verification deploy loses custom
  `timeout_seconds`/`allow_raw_execution` edits. A manual
  `robocopy /MIR /XF cli_bridge_setting.json` keeps the whole file.
- HTTP shapes — `GET /api/auth/status` → `{auth_enabled,scheme:"Bearer",
  message}` (public); `GET /api/raw-mode` → `{allow_raw_execution}`
  (Bearer); `POST /api/raw-mode` with `{"enabled":bool}` → 200
  `{allow_raw_execution,message}`, any other body → 400 `{error:"Request
  body must contain 'enabled' field."}`.

## How to get to it (user POV)

"What stops a random localhost process from driving my Revit?" — the
gates; "can I run ad-hoc C#?" — raw mode.

## Driving it with the revit-cli client + raw HTTP

```powershell
# auth probes (no header): see http-protocol.md — public trio 200, protected 401.
# wrong-key path (client side): point a scratch data dir's key file at junk,
# or --url with a mismatched stored key:
#   & $cli --url "http://127.0.0.1:$PORT" ping    -> exit 2 + "configure setup" guidance

Run "raw-on" ($U + @("execute_raw","--code","return 6*7;"))
# exit 0, result 42 — proves raw is enabled by default (needs a document open)
Run "stdin"  ('return 2+2;' | & $cli @($U + @("execute_raw","--stdin"))) | Out-Null
Run "mode-query" ($U + @("raw-mode"))                                 # {"allow_raw_execution":true}
Run "mode-off" ($U + @("raw-mode","--disable"))
Run "mode-query-off" ($U + @("raw-mode"))                             # {"allow_raw_execution":false}
Run "raw-refused" ($U + @("execute_raw","--code","return 6*7;"))   # exit 4, refusal message
Run "mode-on"  ($U + @("raw-mode","--enable"))                     # restore the session state
Run "raw-restored" ($U + @("execute_raw","--code","return doc.Title;"))   # exit 0, result = open doc title

# config reality (read-only; live 2026-11-26):
$cfg = Get-Content "$env:APPDATA\Autodesk\Revit\Addins\<YEAR>\RevitCliBridge\.config\cli_bridge_setting.json" -Raw | ConvertFrom-Json
$cfg.api_key -cmatch '^[A-Za-z0-9_-]{43}$'                                                  # True
(Get-Content "$env:LOCALAPPDATA\revit-cli\instances\revit-<YEAR>-<PID>.json" -Raw | ConvertFrom-Json).api_key -eq $cfg.api_key   # True
$cfg.PSObject.Properties.Name   # enabled,port,timeout_seconds,max_command_queue_size,task_retention_seconds,allow_raw_execution,auto_port,max_request_body_size_bytes,api_key,allow_unsigned_plugins
Invoke-RestMethod "$B/api/auth/status"                                                      # auth_enabled True, scheme Bearer
Invoke-RestMethod "$B/api/raw-mode" -Headers $hdr                                           # {allow_raw_execution}
'{"enabled":false}' | Set-Content "$env:TEMP\rm.json" -NoNewline
Invoke-RestMethod "$B/api/raw-mode" -Method Post -Headers $hdr -ContentType application/json -InFile "$env:TEMP\rm.json"   # allow_raw_execution false
```

## Gotchas

- `execute_raw` needs an **active document** — a session without one
  fails with "Active document not found." (exit 4); that is not a raw-gate
  refusal.
- Toggling raw-mode mutates session state — every drive that disables it
  must re-enable (or accept the reset on Revit restart).
- Editing the deployed `cli_bridge_setting.json` touches the user's
  install — back it up and restore in cleanup, and prefer `raw-mode`
  over config edits for toggle journeys.
- The stored client key file can resist overwrite ("Access denied"
  until deleted first) — rotate by deleting the file and re-running
  `configure setup`.
- Unsigned-plugin rejection is only provable with an unsigned plugin
  DLL dropped into `CliBridgePlugins\` — the bridge log names the
  rejection; leaving one loaded would be a finding, not a pass.
