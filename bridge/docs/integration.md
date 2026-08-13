# Integration

> Revit plugin lifecycle and the Go CLI client. For the HTTP API see
> [api-reference.md](api-reference.md); for thread safety see
> [threading.md](threading.md).

## Revit Plugin Lifecycle

**Entry**: `BridgeApp` implements `IExternalApplication`.

- **`OnStartup`**: detects Revit version, calls
  `CliBridgeStateManager.Initialize(revitVersion)` (starts server if config
  `enabled`), creates the Ribbon UI.
- **`OnShutdown`**: calls `CliBridgeStateManager.Cleanup()`.
- **Ribbon toggle**: "AI Mode Toggle" button in "AI Tools" panel of "Revit CLI
  Bridge" tab → `ToggleBridgeCommand` flips the bridge on/off and shows a
  `TaskDialog` with the new state.

## Go CLI Client

**Location**: `client/` — depends on `Newtonsoft.Json` only (no Revit API), so
it compiles and runs independently.

### Architecture

| Component | File | Responsibility |
|---|---|---|
| `ArgHelper` | `abstractions/arghelper.go` | Type-safe flag parsing (`FindArg`, `GetInt`, `GetDouble`, `HasFlag`, `ParseIds`, `TryParseValue`). Rejects values starting with `-` to prevent flag consumption. |
| `instance` | `instance/discovery.go` | Instance discovery via registry files. `ResolveURL` priority: `--url` > `--pid` > `--revit` > auto-discover (single instance) > fallback. |
| `discovery` | `client/discovery/` | Schema fetch (`ETag`/`If-None-Match` revalidation) + thread-safe TTL cache with atomic file writes. Version-aware: invalidates stale cache on bridge version change. `Touch()` refreshes TTL on 304. |
| `DynamicCommand` | `client/discovery/dynamic.go` | Auto-generates a CLI handler from `CommandDef` schema; `coerce()` converts strings to typed values. Built-in commands take priority over dynamic ones. |
| `SseClient` | `client/sseclient.go` | SSE transport: handles `:` comment lines, accumulates multi-line `data:` fields, context-based timeouts prevent goroutine leaks, `select`-based sleep for cancellable polling. |

### Command Execution Flow (async)

```
CLI Client                              Revit Server
    │  POST /api/execute {async:true}        │
    │ ─────────────────────────────────────► │
    │  {task_id, status:"pending"}           │
    │◄───────────────────────────────────── │
    │  GET /api/task/{task_id}               │
    │ ─────────────────────────────────────► │
    │  {status:"running", progress:50}       │
    │◄───────────────────────────────────── │
    │  GET /api/task/{task_id}               │
    │ ─────────────────────────────────────► │
    │  {status:"completed", result:{...}}    │
    │◄───────────────────────────────────── │
```

## AI Agent Direct HTTP

AI agents can call the HTTP API directly without the CLI client. With
`auto_port` enabled the port varies by Revit version — use `GET /api/identity`
to verify the target. See [api-reference.md](api-reference.md#example-http-calls)
for `curl` examples.

→ Next: [testing.md](testing.md).
