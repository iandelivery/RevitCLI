# Auth, raw-mode, and config

## Sub-features

- Auth reality — `EnsureApiKey` generates and persists a key whenever
  `api_key` is null/empty, so Bearer auth is effectively **always on**.
  Scope: every `/api/*` route except `/api/health`, `/api/identity`,
  `/api/auth/status`. The key is written into the instance registry file,
  which is how the client auto-discovers it. A wrong key → 401 +
  `WWW-Authenticate: Bearer`; the client prints key-sync guidance
  (`configure setup`) and exits 2.
- Raw execution gate — `execute_raw` (aliases `raw_exec`, `eval`) is
  enabled by default (`allow_raw_execution: true` in code AND shipped
  config); `raw-mode --disable` / `--enable` toggles it in-memory for the
  session (resets on Revit restart). Refusal is a normal failed command
  (client exit 4).
- Plugin trust — unsigned third-party DLLs in `CliBridgePlugins\` are
  rejected unless `allow_unsigned_plugins: true`; `trusted_publishers`
  restricts to known subjects when set.
- Config keys — `enabled`, `port`, `auto_port`, `timeout_seconds` (180),
  `max_command_queue_size` (100 → 429), `task_retention_seconds` (900),
  `allow_raw_execution`, `max_request_body_size_bytes` (10 MiB → 413),
  `api_key`, `allow_unsigned_plugins`, `trusted_publishers`.
- Deploy key preservation — `build.ps1 -Deploy` (and `configure setup`)
  read the deployed `api_key` before overwriting the config and re-add
  it, so upgrades keep stored client keys valid.

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
Run "mode-off" ($U + @("raw-mode","--disable"))
Run "raw-refused" ($U + @("execute_raw","--code","return 6*7;"))   # exit 4, refusal message
Run "mode-on"  ($U + @("raw-mode","--enable"))                     # restore the session state

# config reality (read-only): GET /api/raw-mode shows the live toggle
Invoke-WebRequest "http://127.0.0.1:$PORT/api/raw-mode" -Headers $hdr   # {"allow_raw_execution":true}
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
