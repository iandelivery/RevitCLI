# HTTP route matrix

## Sub-features

- Public probes — `GET /api/health` `{"status":"ok","timestamp"}`,
  `GET /api/identity` `{version,pid,port,hostname,commands_count}`,
  `GET /api/auth/status` `{auth_enabled,scheme:"Bearer",message}` — all
  answer 200 **without** a Bearer header.
- Protected surface — every other `/api/*` route returns 401 +
  `WWW-Authenticate: Bearer` + `{error,auth_enabled}` without a valid key.
- Schema + ETag — `GET /api/commands` (and `/api/catalog`,
  `/api/commands/{name}`) always set `ETag`; a matching `If-None-Match`
  gets 304 with an empty body.
- 404s — unknown `/api/task/{id}` (with retention hint), unknown
  `/api/commands/{name}`, unknown path `{error:"Not found"}`.
- Read-only shapes — `/api/status` `{server_running,total_tasks,...}`,
  `/api/metrics` `{uptime_seconds,by_command,...}`, `/api/documents`
  `{documents[{title,path,is_active}],active_path}`, `/api/llms.txt`
  plain text.

## How to get to it (user POV)

"Is the bridge there, what is it, what can it do?" — the probe/index
surface every client touches before executing anything.

## Driving it with the revit-cli client + raw HTTP

```powershell
# key for raw calls (client commands handle auth themselves):
$key = (Get-Content "$env:LOCALAPPDATA\revit-cli\servers\apikey_localhost_$PORT.txt" -Raw).Trim()
$hdr = @{ Authorization = "Bearer $key" }

# public trio, no auth:
Invoke-WebRequest "http://127.0.0.1:$PORT/api/health"        # 200 {"status":"ok"}
Invoke-WebRequest "http://127.0.0.1:$PORT/api/identity"      # 200 pid/port/commands_count=105
Invoke-WebRequest "http://127.0.0.1:$PORT/api/auth/status"   # 200 auth_enabled:true

# protected without header -> 401 + WWW-Authenticate: Bearer:
try { Invoke-WebRequest "http://127.0.0.1:$PORT/api/commands" } catch { [int]$_.Exception.Response.StatusCode }   # 401

# ETag revalidation:
$r1 = Invoke-WebRequest "http://127.0.0.1:$PORT/api/commands" -Headers $hdr
$etag = $r1.Headers.ETag
Invoke-WebRequest "http://127.0.0.1:$PORT/api/commands" -Headers ($hdr + @{ 'If-None-Match' = $etag })   # 304

# client-side index:
Run "commands" ($U + @("commands"))        # bridge version + 105 lines
Run "catalog"  ($U + @("catalog"))         # count matches
Run "schema404" ($U + @("schema","definitely_not_a_command"))   # exit 1
Run "llms"      ($U + @("llms"))           # exit 0 (document optional)
Run "documents" ($U + @("raw","-j",'{"command":"doc_list"}'))  # or GET /api/documents
```

## Gotchas

- 429 (queue full) and 413 (body too large) need load/oversize payloads —
  drive them only with a targeted script, else record as not-driven.
- `X-Request-Id` is echoed on every response (from header or minted) —
  useful to correlate client transcripts with `bridge-log.txt`.
- The listener binds BOTH `localhost` and `127.0.0.1` prefixes; either
  spelling works.
- Port ownership shows as PID 4 (http.sys) — identity is proven via
  `/api/identity` + client `list` reachable, not `Get-NetTCPConnection`.
