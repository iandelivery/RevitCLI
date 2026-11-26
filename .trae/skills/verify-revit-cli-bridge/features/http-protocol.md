# HTTP route matrix

## Sub-features

- Public probes — `GET /api/health` `{"status":"ok","timestamp"}`,
  `GET /api/identity` `{version,pid,port,hostname,commands_count}`,
  `GET /api/auth/status` `{auth_enabled,scheme:"Bearer",message}` — all
  answer 200 **without** a Bearer header.
- Protected surface — every other `/api/*` route returns 401 +
  `WWW-Authenticate: Bearer` + `{error,auth_enabled}` without a valid key.
  **Auth runs before routing**: an unknown `/api/*` path WITHOUT a key
  answers 401, not 404 (live 2026-11-26).
- Schema + ETag — `GET /api/commands` and `/api/catalog` share one ETag
  (`"{bridgeVersion}:{count}:{hash}"`, so a commands ETag 304s the
  catalog); `/api/commands/{name}` carries `"{bridgeVersion}:{hash}"`.
  A matching `If-None-Match` gets 304 with an empty body; a 404 has no
  ETag.
- 404s — unknown `/api/task/{id}` (with retention hint), unknown
  `/api/commands/{name}`, unknown path `{error:"Not found"}` (with a key,
  or any non-`/api` path), and a method mismatch such as
  `POST /api/commands` (404, never 405).
- 400 / 413 — `POST /api/execute` with `{}` → 400
  `{task_id:"unknown",status:"error",message:"Invalid request: missing
  'command' field."}`; a body over 10 MiB (`max_request_body_size_bytes`)
  → 413 `{error:"Request body too large."}` (drive it with a 10 MiB + 1
  byte file; no load needed).
- Read-only shapes — `/api/status` `{server_running,total_tasks,...}`,
  `/api/metrics` `{uptime_seconds,by_command,...}`, `/api/documents`
  `{documents[{title,path,is_active}],active_path}`, `/api/llms.txt`
  plain text, `/api/catalog` `{catalog_version,command_count,commands[]}`,
  `/api/commands` `{version,server_info{bridge_version,port,features},commands[]}`.

## How to get to it (user POV)

"Is the bridge there, what is it, what can it do?" — the probe/index
surface every client touches before executing anything.

## Driving it with the revit-cli client + raw HTTP

```powershell
# key for raw calls (client commands handle auth themselves):
$key = (Get-Content "$env:LOCALAPPDATA\revit-cli\instances\revit-2019-<PID>.json" -Raw | ConvertFrom-Json).api_key
$hdr = @{ Authorization = "Bearer $key" }
$B   = "http://127.0.0.1:$PORT"

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

# status-code matrix (curl.exe, one line each; the registry file carries the key):
$key = (Get-Content "$env:LOCALAPPDATA\revit-cli\instances\revit-2019-<PID>.json" -Raw | ConvertFrom-Json).api_key
$A = @("-H","Authorization: Bearer $key")
& curl.exe -s -o NUL -w "%{http_code}`n" "$B/api/nope"                  # 401 (no key, unknown path)
& curl.exe -s -o NUL -w "%{http_code}`n" @A "$B/api/nope"               # 404
& curl.exe -s -o NUL -w "%{http_code}`n" "$B/"                           # 404 (non-/api, no auth gate)
& curl.exe -s -o NUL -w "%{http_code}`n" -X POST @A "$B/api/commands"   # 404 (method mismatch)
& curl.exe -s -X POST @A "$B/api/execute" -d '{}'                        # 400 task_id "unknown"
[IO.File]::WriteAllBytes("$env:TEMP\big.bin",[byte[]]::new(10485761))
& curl.exe -s -o NUL -w "%{http_code}`n" -X POST @A -H "Content-Type: application/json" --data-binary "@$env:TEMP\big.bin" "$B/api/execute"   # 413
```

## Gotchas

- 429 (queue full) needs `max_command_queue_size` concurrent tasks —
  drive it only with a targeted script, else record as not-driven. The
  sync and `async:true` paths return it; the SSE path never does.
- In a pwsh script, `\"` inside a `-d`/`-j` value reaches the exe as a
  literal backslash (400 "Invalid property identifier character"). Write
  the JSON to a file and pass `--data-binary "@$file"` / `(Get-Content
  $file -Raw)` as ONE token.
- `curl.exe -N` on the SSE route stays attached after the terminal frame
  (http.sys keeps the stream open until the client closes) — always add
  `--max-time 4` to raw SSE probes.
- `X-Request-Id` is echoed on every response (from header or minted) —
  useful to correlate client transcripts with `bridge-log.txt`.
- The listener binds BOTH `localhost` and `127.0.0.1` prefixes; either
  spelling works.
- Port ownership shows as PID 4 (http.sys) — identity is proven via
  `/api/identity` + client `list` reachable, not `Get-NetTCPConnection`.
