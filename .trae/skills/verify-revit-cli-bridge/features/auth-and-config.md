# Auth, raw-mode, and config

## Sub-features

- Auth reality — `EnsureApiKey` generates and persists a key whenever
  `api_key` is null/empty, so Bearer auth is effectively **always on**.
  Scope: every `/api/*` route except `/api/health`, `/api/identity`,
  `/api/auth/status`. The key is written into the instance registry file.
  The Go client resolves the key **memory → disk
  `%LOCALAPPDATA%\revit-cli\servers\apikey_<host>_<port>.txt` → registry**
  (`auth.go:98-126`): the registry is the last resort, and a stale disk key
  from an earlier session shadows the registry key and yields 401 on every
  protected command. A wrong key → 401 + `WWW-Authenticate: Bearer`; on the
  schema/SSE path (`dispatchDynamic`, `ping`, dynamic commands) the client
  prints key-sync guidance (`configure setup`) and exits 2; the builtin
  `health`, `status`, `raw-mode`, `commands`, `schema`, `task` map any
  non-2xx to exit 1 with no guidance.
- Raw execution gate — `execute_raw` (aliases `raw_exec`, `eval`) is
  enabled by default (`allow_raw_execution: true` in code AND shipped
  config); `raw-mode --disable` / `--enable` toggles it in-memory for the
  session (resets on Revit restart; the config file is not rewritten).
  Refusal is a normal failed command (client exit 4).
- Plugin trust — unsigned third-party DLLs in `CliBridgePlugins\` are
  rejected unless `allow_unsigned_plugins: true`; `trusted_publishers`
  restricts to known subjects when set.
- Config keys — the shipped template holds `schema_version`, `enabled`,
  `port`, `auto_port`, `timeout_seconds` (180), `max_command_queue_size`
  (100 → 429), `allow_raw_execution`, `api_key`. `task_retention_seconds`
  (900), `max_request_body_size_bytes` (10 MiB → 413),
  `allow_unsigned_plugins`, `trusted_publishers` are C# defaults that a
  deployed config may also carry (a 2026-11-26 install had all ten).
- Deploy key preservation — `build.ps1 -Deploy` and `configure setup` read
  the deployed `api_key` before overwriting the config and re-add it, so
  upgrades keep stored client keys valid. `configure setup` takes no flags
  and writes a fresh config map (it keeps `api_key`, sets `port` to the
  version's base port, and does not carry over other user-set keys).

## How to get to it (user POV)

"What stops a random localhost process from driving my Revit?" — the
gates; "can I run ad-hoc C#?" — raw mode.

## Driving it with the revit-cli client + raw HTTP

```powershell
# auth probes (no header): see http-protocol.md — public trio 200, protected 401.
# wrong-key path (client side): a stale key file under the data dir's servers\ folder
# shadows the registry key:
#   & $cli --url "http://127.0.0.1:$PORT" ping    -> exit 2 + "configure setup" guidance
# (delete the file afterwards; a scratch data dir keeps this off the real one)

# config reality (read-only): the deployed file is the source of truth for the key
$cfg = Get-Content "$env:APPDATA\Autodesk\Revit\Addins\2019\RevitCliBridge\.config\cli_bridge_setting.json" -Raw | ConvertFrom-Json
$cfg.PSObject.Properties.Name           # the key list actually deployed
$cfg.api_key.Length                     # 43 (32 URL-safe bytes)
# registry carries the same key:
(Get-Content "$env:LOCALAPPDATA\revit-cli\instances\revit-2019-<PID>.json" -Raw | ConvertFrom-Json).api_key -eq $cfg.api_key   # True

Run "raw-on" ($U + @("execute_raw","--code","return 6*7;"))
# exit 0, result 42 — proves raw is enabled by default (needs a document open)
Run "stdin"  ('return 2+2;' | & $cli @($U + @("execute_raw","--stdin"))) | Out-Null
Run "mode-off" ($U + @("raw-mode","--disable"))          # exit 0
Run "raw-refused" ($U + @("execute_raw","--code","return 6*7;"))   # exit 4: "Raw execution is disabled. Set 'allow_raw_execution: true' ..."
Run "mode-on"  ($U + @("raw-mode","--enable"))            # exit 0; restore the session state

# raw-mode is not persisted:
((Get-Content "$env:APPDATA\Autodesk\Revit\Addins\2019\RevitCliBridge\.config\cli_bridge_setting.json" -Raw | ConvertFrom-Json).allow_raw_execution)   # unchanged

# live toggle over raw HTTP (protected):
curl.exe -s -H "Authorization: Bearer $key" "http://127.0.0.1:$PORT/api/raw-mode"   # {"allow_raw_execution":true,...}
curl.exe -s "http://127.0.0.1:$PORT/api/auth/status"                                 # {"auth_enabled":true,"scheme":"Bearer",...}
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
