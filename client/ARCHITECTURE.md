# CLI Client Architecture

Go-based command-line client that sends commands to the Revit CLI Bridge via HTTP/SSE.

## Project Structure

```
client/
├── cmd/revit-cli/main.go          # Entry point: arg parsing, command dispatch
├── internal/
│   ├── abstractions/              # Contracts and shared utilities
│   │   ├── command.go             # CliCommand interface, CommandMetadata, SendCommandFunc
│   │   ├── categories.go          # CommandCategory enum, MapCategory, CategoryOrder
│   │   └── arghelper.go           # FindArg, HasFlag, GetInt, GetDouble, ParseIDs
│   ├── client/                    # Core client implementation
│   │   ├── registry.go            # CommandRegistry (case-insensitive, order-preserving)
│   │   ├── sseclient.go           # SSE transport with legacy polling fallback
│   │   ├── helptext.go            # Help text generation
│   │   ├── outputprocessor.go     # Output post-processing (jq/fields/fmt stubs)
│   │   ├── util.go                # Shared utilities
│   │   ├── builtin/               # Always-available commands (no server needed)
│   │   │   ├── system.go          # ping, status, health, task, raw
│   │   │   ├── executeraw.go      # execute_raw (forwards to bridge)
│   │   │   ├── rawmode.go         # raw-mode (query/toggle via /api/raw-mode)
│   │   │   ├── configure.go       # configure setup|teardown|check|port
│   │   │   ├── instances.go       # list (instance discovery)
│   │   │   ├── commands.go        # commands, schema (schema browsing)
│   │   │   └── llms.go            # llms (API reference)
│   │   └── discovery/             # Schema-driven command discovery
│   │       ├── cache.go           # SchemaCache (TTL, ETag, atomic writes, version-aware)
│   │       ├── fetcher.go         # SchemaFetcher (HTTP + ETag + version detection)
│   │       ├── dynamic.go         # DynamicCommand (schema → CLI handler)
│   │       └── instances.go       # Bridge-side instance discovery helpers
│   ├── config/                    # Configuration loading
│   │   ├── config.go              # CliBridgeConfig, Load, Validate
│   │   └── default_config.json   # Embedded defaults
│   ├── instance/                  # Instance discovery
│   │   └── discovery.go           # Discover, ResolveURL, isProcessAlive
│   └── models/                    # Data transfer objects
│       ├── schema.go              # CommandSchema, CommandDef, CommandParamSchema
│       ├── input.go               # RevitCommandInput
│       └── response.go            # CommandResponse, QueuedCommand
├── go.mod
├── build.ps1
└── README.md
```

## Request Flow

```
User runs: revit-cli.exe --revit 2022 get_elements -c OST_Walls
                    │
                    ▼
┌─────────────────────────────────────────────────────────────┐
│  main.go: run()                                             │
│  1. parseArgs() → resolve base URL                          │
│  2. registerBuiltIns() → populate CommandRegistry           │
│  3. Check registry for built-in command                     │
│  4. If not found → SchemaFetcher.Fetch() → DynamicCommand   │
│  5. cmd.Handle(ctx, args, sendCommand)                      │
└─────────────┬───────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────────────────┐
│  SseClient.Execute()                                        │
│  1. POST /api/execute with Accept: text/event-stream        │
│  2. Read SSE stream: accepted → progress → completed/failed │
│  3. On SSE failure → fallback to polling GET /api/task/{id} │
│  4. Return exit code (0 = success, 1 = failure)             │
└─────────────────────────────────────────────────────────────┘
```

## Component Details

### 1. Entry Point — `cmd/revit-cli/main.go`

**Responsibilities**: Argument parsing, URL resolution, command registration, dispatch.

**Arg Parsing** (`parseArgs`):
- Scans `--url`, `--pid`, `--revit` flags
- First non-flag argument is the command name
- Remaining arguments are command-specific

**Dispatch Strategy**:
1. Register all built-in commands
2. If command name matches a built-in → execute immediately (no network call)
3. If not found → lazy schema discovery → register dynamic commands → retry
4. If still not found → "Unknown command" error

**Version**: Injected at build time via `-ldflags "-X main.Version=$version"`.

### 2. Command Abstractions — `internal/abstractions/`

**CliCommand Interface**:
```go
type CliCommand interface {
    Metadata() CommandMetadata
    Handle(ctx context.Context, args []string, send SendCommandFunc) int
}
```

**SendCommandFunc**: `func(ctx context.Context, command string, parameters interface{}) int`
- The transport abstraction that decouples command logic from HTTP/SSE details
- Currently wired to `SseClient.Execute`

**CommandCategory**: Groups commands for help text display (System, Query, Create, Modify, etc.)

**ArgHelper**: Centralized argument parsing with type safety:
- `FindArg(args, flags...)` → `(string, bool)` — rejects values starting with `-`
- `HasFlag(args, flags...)` → `bool`
- `GetInt/GetDouble(args, flags...)` → typed value + ok
- `ParseIDs(ids)` → `[]int` — comma-separated integer list
- `TryParseValue(value)` → auto-detect int/float64/string

### 3. Command Registry — `internal/client/registry.go`

**Properties**:
- Case-insensitive name lookup
- Preserves registration order for help text
- Duplicate registration is rejected with a warning log (built-ins take priority)
- Alias support: `RegisterAlias("raw_exec", "execute_raw")`

**Thread Safety**: Not required — the registry is populated in `main()` before concurrent access.

### 4. SSE Client — `internal/client/sseclient.go`

**Transport**: HTTP POST with `Accept: text/event-stream` header.

**SSE Event Handling**:

| Event | Action |
|-------|--------|
| `accepted` | Capture `task_id` for fallback polling |
| `progress` | Display progress bar (pct + message) |
| `completed` | Print result, return exit code 0 |
| `failed` | Print error, return exit code 1 |
| `heartbeat` | Reset timeout (no-op) |

**SSE Spec Compliance**:
- Comment lines (`:` prefix) are ignored
- Multi-line `data:` fields accumulated with `strings.Builder`
- Blank line dispatches the accumulated event

**Fallback Chain**:
1. SSE stream read error → `fallbackPollLastTask()`
2. Poll `GET /api/task/{id}` with 500ms interval, 120s max wait
3. Context-based cancellation via `select` instead of `time.Sleep`

**Heartbeat Timeout**: 30 seconds. If no data arrives within this window, the stream is considered dead and fallback polling begins.

**Goroutine Safety**: `readLineWithTimeout` uses `context.WithCancel` to ensure the read goroutine is always cleaned up, preventing leaks.

### 5. Schema Discovery — `internal/client/discovery/`

#### 5.1 SchemaFetcher

**Fetch Flow**:
```
Fetch(forceRefresh=false)
  │
  ├─ Cache hit (TTL valid, version matches) → return cached schema
  │
  ├─ Cache miss or expired:
  │   ├─ GET /api/commands with If-None-Match header
  │   │
  │   ├─ 304 Not Modified → Touch() TTL, return cached
  │   │
  │   ├─ 200 OK → parse, save to cache, save ETag, return
  │   │
  │   └─ Network error → return stale cache (graceful degradation)
  │
  └─ Version change detected → force re-fetch
```

**Version-Change Detection**: Compares `ServerInfo.BridgeVersion` in cached schema against the running server. If different, the cache is treated as stale even if TTL hasn't expired.

#### 5.2 SchemaCache

**Properties**:
- TTL: 30 minutes
- Thread-safe: `sync.Mutex` protects all file operations
- Atomic writes: temp file + `os.Rename` to prevent corruption
- ETag persistence for conditional revalidation
- `Touch()` refreshes TTL on 304 responses
- Stale-cache fallback when bridge is unreachable

**Cache Key**: Derived from server URL (e.g. `localhost_5041`). IPv6 brackets are stripped.

**Data Directory** (cascading resolution):

| Priority | Path | Use Case |
|----------|------|----------|
| 1 | `$REVIT_CLI_DATA_DIR` | CI/headless override |
| 2 | `%LOCALAPPDATA%\revit-cli\` | Windows best practice |
| 3 | `%USERPROFILE%\.revit-cli\` | Dot-folder fallback |
| 4 | `<exe dir>\.revit-cli\` | Portable mode |

#### 5.3 DynamicCommand

**Schema → Handler**: Converts a `CommandDef` from the bridge schema into a `CliCommand` implementation at runtime.

**Parameter Coercion** (`coerce()`):

| Schema Type | Go Type | Error Handling |
|-------------|---------|----------------|
| `int` | `int` | Falls back to string |
| `double` | `float64` | Falls back to string |
| `bool` | `bool` | Falls back to string |
| `int[]` | `[]int` | Warns and skips unparseable elements |
| `double[]` | `[]float64` | Warns and skips unparseable elements |
| `bool[]` | `[]bool` | Warns and skips unparseable elements |
| `string[]` | `[]string` | Comma-split with trim |
| `string` (default) | `string` | No conversion |

**Required Parameter Validation**: Missing required parameters produce per-parameter error messages and a usage hint.

**Built-in Priority**: Dynamic commands whose names match a built-in are silently skipped during registration.

### 6. Instance Discovery — `internal/instance/discovery.go`

**Discover Flow**:
1. Read all `revit-*.json` files from instances directory
2. Parse each as `InstanceInfo`
3. Check if PID is alive (Windows: `tasklist`, Unix: `Signal(0)`)
4. Clean up stale files (dead PID → delete file)
5. Sort by version (desc), then PID (asc)

**URL Resolution** (`ResolveURL`):

```
--url <url>          → use directly
--pid <pid>          → find instance with matching PID
--revit <version>    → find first instance of that version
auto-discover        → single instance → use it
                     → multiple → prompt user
no instances         → error: start Revit and enable the bridge
```

**Process Alive Detection**:
- **Windows**: `tasklist /FI "PID eq {pid}" /NH /FO CSV` — reliable since `os.FindProcess` always succeeds on Windows
- **Unix**: `os.FindProcess(pid).Signal(nil)` — standard approach

### 7. Built-in Commands — `internal/client/builtin/`

| Command | Type | Transport | Description |
|---------|------|-----------|-------------|
| `ping` | Bridge | SSE | Test connection via `execute ping` |
| `status` | HTTP | GET `/api/status` | Server status |
| `health` | HTTP | GET `/api/health` | Health check |
| `task` | HTTP | GET `/api/task/{id}` | Task status query |
| `raw` | Bridge | SSE | Send raw JSON command |
| `execute_raw` | Bridge | SSE | Execute C#/Python code |
| `raw-mode` | HTTP | GET/POST `/api/raw-mode` | Query/toggle raw execution |
| `list` | Local | File system | List running instances |
| `commands` | HTTP | GET `/api/commands` | Browse command schemas |
| `schema` | HTTP | GET `/api/commands/{name}` | Single command schema |
| `llms` | HTTP | GET `/api/llms.txt` | Revit API reference |
| `configure` | Local | File system | setup/teardown/check/port |

**Command Categories**:
- **Bridge commands** (`ping`, `raw`, `execute_raw`): Sent to bridge via `SendCommandFunc` (SSE)
- **HTTP commands** (`status`, `health`, `task`, `raw-mode`, `commands`, `schema`, `llms`): Direct HTTP GET/POST
- **Local commands** (`list`, `configure`): No server connection needed

### 8. Configuration — `internal/config/`

**CliBridgeConfig**:

| Field | Type | Default | Validation |
|-------|------|---------|------------|
| `schema_version` | `string` | `"1"` | — |
| `enabled` | `bool` | `true` | — |
| `port` | `int` | `5000` | 1–65535 |
| `auto_port` | `bool` | `true` | — |
| `timeout_seconds` | `int` | `180` | >= 1 |
| `max_command_queue_size` | `int` | `100` | >= 1 |
| `allow_raw_execution` | `bool` | `false` | — |

**Loading**: Embedded `default_config.json` provides compile-time defaults. Runtime config overrides from `.config/cli_bridge_setting.json`. Zero values are filled from defaults. Validation warns but does not reject.

### 9. Models — `internal/models/`

**Wire Contract**: All JSON field names use `snake_case` to match the C# bridge server.

**Key Types**:
- `CommandSchema` — Full schema response (`version`, `server_info`, `commands[]`)
- `CommandDef` — Single command metadata (`name`, `description`, `parameters[]`, `aliases[]`)
- `CommandParamSchema` — Parameter definition (`name`, `type`, `required`, `default`, `short_flag`, `enum_values`)
- `RevitCommandInput` — Command request (`command`, `parameters`, `task_id`, `timeout_seconds`, `async`, `dry_run`)
- `CommandResponse` — Command result (`task_id`, `status`, `message`, `data`, `error_details`)
- `QueuedCommand` — Internal queue item (`task_id`, `command`, `parameters`, `dry_run`)

## Security Model

| Concern | Mitigation |
|---------|------------|
| Raw execution | Disabled by default (`allow_raw_execution: false`); runtime toggle via `raw-mode` command |
| Network exposure | Bridge listens on `localhost` only |
| Flag consumption | `FindArg` rejects values starting with `-` |
| Request body size | Bridge enforces 10 MB limit |
| Queue overflow | Bridge returns HTTP 429 when queue is full |

## Error Handling Strategy

| Layer | Strategy |
|-------|----------|
| Network | Graceful degradation: stale cache, fallback polling |
| Command | Exit code 1 + stderr message |
| Config | Warn and use defaults (non-fatal) |
| Cache | Log error, return nil (caller falls back) |
| Process alive | Conservative: assume alive if check fails |
