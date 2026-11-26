# HTTP route matrix

## Sub-features

- Public probes — `GET /api/health` `{"status":"ok","timestamp"}`,
  `GET /api/identity` `{version,pid,port,hostname,commands_count}`,
  `GET /api/auth/status` `{auth_enabled,scheme:"Bearer",message}` — all
  answer 200 **without** a Bearer header.
- Protected surface — every other `/api/*` route returns 401 +
  `WWW-Authenticate: Bearer` + `{error,auth_enabled}` without a valid key.
  Auth runs **before** routing: an unknown `/api/*` path without a key is 401,
  not 404.
- Schema + ETag — `GET /api/commands` and `/api/catalog` always set
  `ETag: "{bridgeVersion}:{count}:{base64url-sha256}"`; `/api/commands/{name}`
  sets `"{bridgeVersion}:{hash}"` (no count). A matching `If-None-Match` gets
  304 with an empty body; catalog accepts the commands ETag.
- 404s — unknown `/api/task/{id}` (with retention hint), unknown
  `/api/commands/{name}`, unknown path `{error:"Not found"}` (with a valid key).
- Read-only shapes — `/api/status` `{server_running,total_tasks,...}`,
  `/api/metrics` `{uptime_seconds,by_command,...}`, `/api/documents`
  `{documents[{title,path,is_active}],active_path}`, `/api/llms.txt`
  plain text, `GET /api/task` (last 50 tasks), `/api/raw-mode` GET
  `{enabled,...}` — the last two are protected too.
- Error codes — 400 malformed JSON or missing `command`; 413 body above
  `max_request_body_size_bytes` (10 MiB default); 500 only when a sync task
  ends `Failed`; **429 only on the sync JSON path** when the queue holds
  `max_command_queue_size` (100) tasks — the SSE path enqueues unconditionally
  and never answers 429.

## How to get to it (user POV)

"Is the bridge there, what is it, what can it do?" — the probe/index
surface every client touches before executing anything.

## Driving it with the revit-cli client + raw HTTP

```powershell
# key for raw calls: the deployed config, not a client cache file
# (a stale servers\apikey_*.txt from a dead session shadows the live key):
$key = (Get-Content "$env:APPDATA\Autodesk\Revit\Addins\2019\RevitCliBridge\.config\cli_bridge_setting.json" -Raw | ConvertFrom-Json).api_key
$B = "http://127.0.0.1:$PORT"

# public trio, no auth (curl prints the status; Invoke-WebRequest throws on any 4xx):
foreach ($p in "/api/health","/api/identity","/api/auth/status") {
    "{0,-18} {1}" -f $p, (curl.exe -s -o NUL -w "%{http_code}" "$B$p") }   # 200 x3

# protected without header -> 401; unknown path without header -> 401 (auth first):
curl.exe -s -o NUL -w "%{http_code}`n" "$B/api/commands"                       # 401
curl.exe -s -o NUL -w "%{http_code}`n" "$B/api/nope"                           # 401
curl.exe -s -o NUL -w "%{http_code}`n" -H "Authorization: Bearer $key" "$B/api/nope"   # 404

# ETag revalidation (curl, so a 304 is a status and not an exception):
$etag = (curl.exe -s -D - -o NUL -H "Authorization: Bearer $key" "$B/api/commands" |
         Select-String '^ETag:').Line.Split(' ',2)[1].Trim()                 # "2019.2.0:105:<base64url>"
curl.exe -s -o NUL -w "%{http_code}`n" -H "Authorization: Bearer $key" -H "If-None-Match: $etag" "$B/api/commands"   # 304
curl.exe -s -o NUL -w "%{http_code}`n" -H "Authorization: Bearer $key" -H "If-None-Match: $etag" "$B/api/catalog"    # 304
# per-command ETag has no count segment:
(curl.exe -s -D - -o NUL -H "Authorization: Bearer $key" "$B/api/commands/ping" |
   Select-String '^ETag:').Line                                                                   # "2019.2.0:<hash>"

# 413: a body over 10 MiB:
Set-Content "$env:TEMP\big.json" ('{"command":"ping","parameters":{"pad":"' + ('x' * 11MB) + '"}}') -NoNewline
curl.exe -s -o NUL -w "%{http_code}`n" -X POST -H "Authorization: Bearer $key" -H "Content-Type: application/json" `
  "--data-binary@$env:TEMP\big.json" "$B/api/execute"                         # 413

# client-side index:
Run "commands" ($U + @("commands"))        # bridge version + 105 lines
Run "catalog"  ($U + @("catalog"))         # count matches
Run "schema404" ($U + @("schema","definitely_not_a_command"))   # exit 1
Run "llms"      ($U + @("llms"))           # exit 0 (document optional)
Run "documents" ($U + @("doc_list"))       # or GET /api/documents
```

## Gotchas

- 429 needs `Accept: application/json` plus 100 queued commands (sync path
  only); drive it with a targeted script, else record as not-driven.
- From pwsh, write request bodies to a file and pass `"--data-binary@$file"`
  as one token — `\"` inside `-d`/`-j` strings arrives as a literal backslash
  (400 "Invalid JSON") and a bare `"@file"` is splatted away.
- `X-Request-Id` is echoed on every response (from header or minted) —
  useful to correlate client transcripts with `bridge-log.txt`.
- The listener binds BOTH `localhost` and `127.0.0.1` prefixes; either
  spelling works.
- Port ownership shows as PID 4 (http.sys) — identity is proven via
  `/api/identity` + client `list` reachable, not `Get-NetTCPConnection`.
