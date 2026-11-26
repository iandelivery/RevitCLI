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
- Auth rejection — a 401 on execute (`ping`, dynamic commands) or on the
  schema fetch prints key guidance and exits 2. On the GET built-ins
  (`health`/`status`/`task`) a 401 is **exit 1** with the body on stdout
  and no hint. Two client-side texts: no key found → "No API key is
  available for this bridge. Run 'revit-cli configure setup' to sync
  keys, or update the bridge add-in and restart Revit so it publishes
  its key."; wrong key → "The API key was rejected. Run 'revit-cli
  configure setup' to re-sync keys, or restart Revit if you just updated
  the bridge add-in." Schema fetch with a wrong key → "The bridge at
  <url> rejected the API key (401)." + the same hint, exit 2.
- Timeout — `--timeout` (default 120, min 1) is sent as `timeout_seconds`
  and enforced only in the polling fallback (`Task <id> timed out after
  N seconds.`, exit 3). The bridge's own timeout arrives as a `failed`
  event with the exact message below.
- `task` — `task [-ti <id>]`: one GET (`/api/task` or `/api/task/{id}`);
  connect failure or non-2xx → exit 1 (a 404 task prints the body).
- `--version` — banner + `revit-cli <git describe>` + `Author: Ian Chan`.
  Bare/`--help` prints the banner and the exit-code contract hint; the
  Flags list shows the six cobra flags, `--units` appears only in the
  hint text.

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

401 paths (live 2026-11-26) — a stdlib 401 mock is enough, no real bridge:

```powershell
@'
import json,sys
from http.server import BaseHTTPRequestHandler,HTTPServer
class H(BaseHTTPRequestHandler):
	def log_message(self,*a): pass
	def _r(self):
		b=json.dumps({"error":"Unauthorized.","auth_enabled":True}).encode()
		self.send_response(401); self.send_header("Content-Type","application/json")
		self.send_header("Content-Length",str(len(b))); self.end_headers(); self.wfile.write(b)
	do_GET=do_POST=_r
HTTPServer(("127.0.0.1",int(sys.argv[1])),H).serve_forever()
'@ | Set-Content "$SCRATCH\m401.py"
$p401 = Start-Process python "$SCRATCH\m401.py 25998" -WindowStyle Hidden -PassThru
Run "401-ping-nokey"  @("--url","http://127.0.0.1:25998","ping")        # exit 2 + "No API key is available" hint
Run "401-health"      @("--url","http://127.0.0.1:25998","health")      # exit 1, body on stdout, no hint
"wrong" | Set-Content "$SCRATCH\servers\apikey_localhost_25998.txt"
Run "401-ping-wrong"  @("--url","http://127.0.0.1:25998","ping")        # exit 2 + "The API key was rejected" hint
Run "401-create_wall" @("--url","http://127.0.0.1:25998","create_wall") # exit 2 "rejected the API key (401)" (schema fetch)
Remove-Item "$SCRATCH\servers\apikey_localhost_25998.txt"; Stop-Process -Id $p401.Id -Force
```

## Gotchas

- `ping` prints the result on stdout after a `\n`; progress lines live on
  stderr with `\r` — capture with `2>&1` and assert on the exit code, not on
  stdout purity.
- Both loopback spellings work against the real bridge (it binds both
  prefixes); the mock binds `127.0.0.1` only — use `127.0.0.1` with `--url`
  in every mock drive.
- Exit codes are the contract (`sseclient.go:22-28`); any doc or assertion
  that says "failed = exit 1" is wrong — that is usage-error territory.
