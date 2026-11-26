# Schema discovery: commands / catalog / schema, cache, help

## Sub-features

- `commands` — full schema via the cache-aware fetcher; prints `schema.Version`
  (the mock's `2.0.0`, not `server_info.bridge_version`) + one line per
  command. `--refresh` skips the TTL read but still sends `If-None-Match`, so
  an unchanged bridge answers 304 and the cache is `Touch()`ed.
- `catalog` — lightweight `GET /api/catalog` (name/category/summary),
  grouped by category; `--json` re-prints the parsed object.
- `schema <name>` — per-command `GET /api/commands/{name}` via `httpGet`
  (not the process cache); on a non-2xx it falls back to the cached full
  schema and matches `cmd.Name` only — **no alias resolution** here;
  404/unknown → exit 1.
- Cache — per-server files under `<data-dir>\cache`:
  `servers\schema-cache_{host_port}.json` + `schema-etag_{host_port}.txt`
  (the "default server at the root" branch is unreachable from the CLI:
  `ResolveURL` always yields host:port). TTL 30 min; `Load` enforces mtime ≤
  TTL; 304 responses `Touch()` the cache (extend TTL); network failure falls
  back to stale cache; atomic tmp+rename save. Since PR #117 the list is
  written first and the stamp only after that save succeeds, and
  `LoadEtagForList` withholds `If-None-Match` when no list file exists — so
  a stamp without a list re-fetches 200 and rewrites both.
- Dynamic `--help` — rendered from the schema: usage line, per-param
  `--name <type>` + `(required)`/`(default: v)` + `[unit kind: <kind>]` +
  `[-short]`, dry-run note, examples. `dispatchDynamic` calls `FetchCommand`
  first (`main.go:278`), so a reachable bridge logs `GET
  /api/commands/<name>`; it still renders from the stale cache when the
  bridge is down. Exit 0.
- Did-you-mean — unknown command after full-schema resolution prints up to
  3 Levenshtein suggestions (`Did you mean: a, b, or c?`) and exits 1.

## How to get to it (user POV)

"What can this bridge do, and what flags does `create_wall` take?" — the
schema surface; `--help` is the per-command cheat sheet an agent reads
before executing.

## Driving it with the terminal + mock bridge

```powershell
function N($p) { (Select-String -Path "$EV\bridge.log" -Pattern $p).Count }   # request-count oracle
$n0 = N 'GET /api/commands 200'
Run "commands"  @("--url","http://127.0.0.1:$PORT","commands")   # exit 0; one new "GET /api/commands 200"
$n1 = N 'GET /api/commands 200'
Run "commands-cached" @("--url","http://127.0.0.1:$PORT","commands")
$n2 = N 'GET /api/commands 200'
# assert $n2 -eq $n1                  (TTL cache: zero new requests)
Test-Path "$SCRATCH\cache\servers\schema-cache_127_0_0_1_$PORT.json"   # True
Test-Path "$SCRATCH\cache\servers\schema-etag_127_0_0_1_$PORT.txt"    # True

Run "commands-refresh" @("--url","http://127.0.0.1:$PORT","commands","--refresh")
# log shows GET /api/commands 304 (If-None-Match matched the mock ETag); mtime re-Touched

# stamp without list (PR #117 path): delete only the list, keep the stamp
Remove-Item "$SCRATCH\cache\servers\schema-cache_127_0_0_1_$PORT.json"
Run "commands-restamp" @("--url","http://127.0.0.1:$PORT","commands","--refresh")
# log shows GET /api/commands 200 (stamp withheld, no If-None-Match); BOTH files back

Run "catalog" @("--url","http://127.0.0.1:$PORT","catalog")       # grouped index, command_count matches
Run "catalog-json" @("--url","http://127.0.0.1:$PORT","catalog","--json")

Run "schema-wall" @("--url","http://127.0.0.1:$PORT","schema","create_wall")  # params incl. unit_kind
Run "schema-404" @("--url","http://127.0.0.1:$PORT","schema","definitely_not_a_command")  # exit 1

$c0 = N 'GET /api/commands/create_wall'
Run "help-wall" @("--url","http://127.0.0.1:$PORT","create_wall","--help")
# shows: --height <double> (default: 3000)  [unit kind: length]
# assert (N 'GET /api/commands/create_wall') - $c0 -eq 1   (the help DOES fetch the command schema)

Run "did-you-mean" @("--url","http://127.0.0.1:$PORT","creat_wall")  # exit 1 + "Did you mean: create_wall?"
```

## Gotchas

- Cache proof = request-count delta in `bridge.log`; identical stdout does
  not distinguish 200-from-cache vs 304.
- The per-command GET (`schema <name>` / `FetchCommand`) does not
  revalidate its ETag — only the full-schema fetch sends `If-None-Match`.
- Stale-cache fallback: kill the mock, then `commands` still prints the
  cached list (proving offline resilience); `create_wall --help` likewise
  (it falls back to the stale cache when the GET fails).
- Cache files key on host:port with `.`/`:` normalized — every CLI drive
  lands under `servers\`, because `ResolveURL` never yields an empty host.
