# Schema discovery: commands / catalog / schema, cache, help

## Sub-features

- `commands` — full schema via the cache-aware fetcher; prints bridge
  version + one line per command. `--refresh` forces HTTP revalidation.
  Bridge down with no cache → exit 1 "Cannot fetch commands from <url>".
- `catalog` — lightweight `GET /api/catalog` (name/category/summary),
  grouped by category; never cached; exit 1 when the bridge is down.
- `schema <name>` — per-command `GET /api/commands/{name}` first (a raw
  GET: no cache, no ETag), then falls back to a full-schema match on
  `cmd.Name` ONLY (no alias matching here); both miss → exit 1
  `Command '<n>' not found in schema.` (two requests in the log: 404 +
  full schema 200). The process cache in `FetchCommand` serves dynamic
  dispatch only and never survives between CLI invocations.
- Cache — per-server files under `<data-dir>\cache\servers\`:
  `schema-cache_{host_port}.json` + `schema-etag_{host_port}.txt` (host
  dots and the port colon → `_`; auto-discovery keys as `localhost_<port>`,
  `--url http://127.0.0.1:<port>` as `127_0_0_1_<port>` — different
  files). TTL 30 min; `Load` enforces mtime ≤ TTL; 304 responses
  `Touch()` the cache (extend TTL); network failure falls back to stale
  cache; atomic tmp+rename save. A 304 needs both files: an orphan ETag
  yields a 200.
- Dynamic `--help` — rendered from the schema: usage line, per-param
  `--name <type>` + `(required)`/`(default: v)` + `[unit kind: <kind>]` +
  `[-short]`, dry-run note, examples. Usage prints underscore names while
  examples use dashes; `[--dry-run]` ends the usage line. Works offline
  from stale cache, exit 0; online it always issues the schema GET (only
  `/api/execute` is skipped).
- Did-you-mean — unknown command after full-schema resolution prints up to
  3 Levenshtein suggestions (`Did you mean: a, b, or c?`, distance ≤
  len/2, alphabetical ties, aliases scored but the primary name printed)
  and exits 1. Purely client-side; no suggestions when the schema is
  unavailable (dead port → `Unknown command: <name>` + the generic hint).

## How to get to it (user POV)

"What can this bridge do, and what flags does `create_wall` take?" — the
schema surface; `--help` is the per-command cheat sheet an agent reads
before executing.

## Driving it with the terminal + mock bridge

```powershell
$n0 = (Get-Content "$EV\bridge.log").Count          # the mock logs 2 lines per request
Run "commands"  @("--url","http://127.0.0.1:$PORT","commands")   # exit 0; GET /api/commands 200 in log
"requests: " + (((Get-Content "$EV\bridge.log").Count - $n0) / 2)  # 1
Run "commands-cached" @("--url","http://127.0.0.1:$PORT","commands")
"requests: " + (((Get-Content "$EV\bridge.log").Count - $n0) / 2)  # still 1 (TTL cache: zero new requests)
Test-Path "$SCRATCH\cache\servers\schema-cache_127_0_0_1_$PORT.json"   # True
Get-Content "$SCRATCH\cache\servers\schema-etag_127_0_0_1_$PORT.txt"   # the mock ETag, quote-stripped

Run "commands-refresh" @("--url","http://127.0.0.1:$PORT","commands","--refresh")
# log shows GET /api/commands 304 (If-None-Match matched the mock ETag); cache mtime bumped

Run "catalog" @("--url","http://127.0.0.1:$PORT","catalog")       # grouped index under "Bridge version: <catalog_version>"
Run "catalog-json" @("--url","http://127.0.0.1:$PORT","catalog","--json")

Run "schema-wall" @("--url","http://127.0.0.1:$PORT","schema","create_wall")  # params incl. unit_kind; ONE GET
Run "schema-404" @("--url","http://127.0.0.1:$PORT","schema","definitely_not_a_command")  # exit 1; log: 404 + full schema

Run "help-wall" @("--url","http://127.0.0.1:$PORT","create_wall","--help")
# shows: --height <double> (default: 3000)  [unit kind: length]   (the MOCK schema; the live bridge no longer sets a default)
# log: GET /api/commands/create_wall, no execute

Run "did-you-mean" @("--url","http://127.0.0.1:$PORT","creat_wall")  # exit 1 + "Did you mean: create_wall?"
```

## Gotchas

- Cache proof = request-count delta in `bridge.log`; identical stdout does
  not distinguish 200-from-cache vs 304.
- The per-command GET (`schema <name>`, dynamic dispatch) sends no
  `If-None-Match` — only the full-schema fetch revalidates.
- Stale-cache fallback: kill the mock, then `commands` still prints the
  cached list (proving offline resilience); `create_wall --help` likewise.
  `catalog` has no cache and exits 1 when the mock is down.
- Auto-discovery caches as `localhost_<port>` while `--url 127.0.0.1`
  caches as `127_0_0_1_<port>` — the two do not share a cache file.
