# Schema discovery: commands / catalog / schema, cache, help

## Sub-features

- `commands` — full schema via the cache-aware fetcher; prints bridge
  version + one line per command. `--refresh` forces HTTP revalidation.
- `catalog` — lightweight `GET /api/catalog` (name/category/summary),
  grouped by category; `--json` re-prints the parsed object.
- `schema <name>` — per-command `GET /api/commands/{name}` first (process
  cache), falls back to full-schema match by name or alias; 404/unknown →
  exit 1.
- Cache — per-server files under `<data-dir>\cache`: default server at the
  root, others under `servers\schema-cache_{host_port}.json` +
  `schema-etag_{host_port}.txt`. TTL 30 min; `Load` enforces mtime ≤ TTL;
  304 responses `Touch()` the cache (extend TTL); network failure falls
  back to stale cache; atomic tmp+rename save.
- Dynamic `--help` — rendered from the schema: usage line, per-param
  `--name <type>` + `(required)`/`(default: v)` + `[unit kind: <kind>]` +
  `[-short]`, dry-run note, examples. Works offline from stale cache,
  exit 0, no bridge contact.
- Did-you-mean — unknown command after full-schema resolution prints up to
  3 Levenshtein suggestions (`Did you mean: a, b, or c?`) and exits 1.

## How to get to it (user POV)

"What can this bridge do, and what flags does `create_wall` take?" — the
schema surface; `--help` is the per-command cheat sheet an agent reads
before executing.

## Driving it with the terminal + mock bridge

```powershell
$before = (Get-Content "$EV\bridge.log" | Measure-Object -Line).Lines
Run "commands"  @("--url","http://127.0.0.1:$PORT","commands")   # exit 0; GET /api/commands 200 in log
Run "commands-cached" @("--url","http://127.0.0.1:$PORT","commands")
$after = (Get-Content "$EV\bridge.log" | Measure-Object -Line).Lines
# assert $after -eq $before            (TTL cache: zero new requests)
Test-Path "$SCRATCH\cache\servers\schema-cache_127_0_0_1_$PORT.json"   # True

Run "commands-refresh" @("--url","http://127.0.0.1:$PORT","commands","--refresh")
# log shows GET /api/commands 304 (If-None-Match matched the mock ETag)

Run "catalog" @("--url","http://127.0.0.1:$PORT","catalog")       # grouped index, command_count matches
Run "catalog-json" @("--url","http://127.0.0.1:$PORT","catalog","--json")

Run "schema-wall" @("--url","http://127.0.0.1:$PORT","schema","create_wall")  # params incl. unit_kind
Run "schema-404" @("--url","http://127.0.0.1:$PORT","schema","definitely_not_a_command")  # exit 1

Run "help-wall" @("--url","http://127.0.0.1:$PORT","create_wall","--help")
# shows: --height <double> (default: 3000)  [unit kind: length]

Run "did-you-mean" @("--url","http://127.0.0.1:$PORT","creat_wall")  # exit 1 + "Did you mean: create_wall?"
```

## Gotchas

- Cache proof = request-count delta in `bridge.log`; identical stdout does
  not distinguish 200-from-cache vs 304.
- The per-command GET (`schema <name>`) does not revalidate its ETag —
  only the full-schema fetch sends `If-None-Match`.
- Stale-cache fallback: kill the mock, then `commands` still prints the
  cached list (proving offline resilience); `create_wall --help` likewise.
- Cache files key on host:port with `.`/`:` normalized — the default
  server (no host) writes to the cache root, not `servers\`.
