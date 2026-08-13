# Core Components

> See [overview.md](overview.md) first for the big picture. Endpoints and data
> models live in [api-reference.md](api-reference.md); the command catalog and
> parameter binding live in [commands.md](commands.md).

## CliHttpServer — IPC Layer

**File**: `CliBridge/CliHttpServer.cs`

Lightweight `HttpListener` server on `localhost`. Concurrency is bounded by
`SemaphoreSlim(10, 10)`; bodies over 10 MB return HTTP 413; queue full returns
HTTP 429. SSE streams are linked to the server shutdown token and emit a final
`event: done` before closing.

**Execution modes** on `POST /api/execute`:

| Mode | Trigger | Behavior |
|------|---------|----------|
| Sync (default) | `async: false` / omitted | Connection held until result or timeout |
| Async | `async: true` | Returns `task_id` immediately; poll `/api/task/{id}` |
| SSE | `Accept: text/event-stream` | Streams progress + result events |

Endpoints are listed in [api-reference.md](api-reference.md#endpoints).

## TaskRegistry + TaskStateMachine — Async-to-Sync Bridge

**Files**: `CliBridge/TaskRegistry.cs` + `CliBridge/TaskStateMachine.cs`

`TaskRegistry` owns the global state (`Tasks` dictionary, `CommandQueue`,
`RevitEvent`) and delegates transitions to `TaskStateMachine` (pure logic,
unit-testable):

```csharp
// TaskRegistry — global state + lookup only
public static TaskInfo CreateTask(string taskId, string command);
public static void SetRunning(string taskId);     // → TaskStateMachine.SetRunning(task)
public static void SetCompleted(string taskId, string resultJson);
public static void SetFailed(string taskId, string errorJson);

// TaskStateMachine — pure logic, operates on a TaskInfo instance
public static void SetRunning(TaskInfo? task);              // Pending→Running, stamp, broadcast
public static void SetCompleted(TaskInfo? task, string r);  // → Completed, TCS.TrySetResult
public static void SetFailed(TaskInfo? task, string r);     // → Failed, TCS.TrySetResult
```

`TrySetResult` (not `SetResult`) avoids `InvalidOperationException` on double-set.
`CleanupOldTasks()` runs after each command loop to prevent memory leaks.

**Task lifecycle**: `pending → running → completed | failed | timeout`

**TaskInfo** (all mutable fields `lock`-protected; `Tcs` is `[JsonIgnore]`):
`TaskId`, `Command`, `Status`, `Progress`, `ProgressMessage`, `ResultJson`,
`CreatedAt`, `StartedAt`, `CompletedAt`, `Tcs`.

> The enum is `CliTaskStatus` (not `TaskStatus`) to avoid clashing with
> `System.Threading.Tasks.TaskStatus`.

**Why three primitives?** `ConcurrentQueue` (FIFO under concurrency) +
`ExternalEvent` (wake the Revit UI thread safely) + `TaskCompletionSource`
(unblock the HTTP awaiter when Revit finishes).

## CliCommandHandler — Revit Main Thread Executor

**File**: `CliBridge/CliCommandHandler.cs`

Implements `IExternalEventHandler`. On each wake-up it drains the queue in a
`while` loop, routing each command through `CommandRouter` and tracking the
lifecycle via `TaskRegistry`. A `try/catch` guarantees failed commands still
release the pending `TCS`.

## CommandRouter + CommandNameResolver — Routing

**Files**: `CliBridge/CommandRouter.cs` + `Abstractions/CommandNameResolver.cs`

Auto-discovers all `IBridgeCommand` implementations via reflection (lazy init,
re-entrancy-guarded). Third-party DLLs in `CliBridgePlugins/` are loaded by
`BridgePluginLoader`. `CommandNameResolver` (pure logic) implements the
matching rules so they're unit-testable:

1. **Exact match** — input is a registered name.
2. **Domain path suffix** — `"elements.walls.create"` → `"walls.create"` → `"create"`.
3. **Underscore reversal** — `"wall_create"` → `"create_wall"` (two segments).

**Versioned routing**: each handler is registered under `{name}@{version}`.
The default version (`v1`) is *also* registered under the bare `{name}` so
legacy callers are unaffected. The resolver strips any `@version` suffix
before applying rules 2–3, then re-attaches it; see
[commands.md](commands.md#command-versioning).

See [commands.md](commands.md) for the full 52-command catalog and how to add one.

## CliBridgeStateManager — Bridge State

**File**: `CliBridge/CliBridgeStateManager.cs`

Manages on/off state with `volatile _isEnabled` + `lock` for transitions.
`Initialize(revitVersion)` starts the server if config `enabled`; `Toggle()`
flips state from the Ribbon button; `Cleanup()` tears everything down.

### PortAllocator

`base_port = 5000 + (version - 2018) * 10 + 1` (R2019→5011, R2020→5021, …).
Probes `base_port…base_port+9`, then the configured fallback `port`, then an
OS-ephemeral port.

### InstanceRegistry

Writes `%LOCALAPPDATA%\revit-cli\instances\revit-{version}-{pid}.json` (atomic:
temp file + `File.Move`) on start, deletes on stop, cleans stale files on
startup. PID liveness checked via `Process.GetProcessById(pid)` (C#) or
`tasklist /FI` (Go).

### LlmsTxtGenerator

Serves `/api/llms.txt` — a text reference of raw Revit API elements (BuiltIn
categories/parameters, element class hierarchy, loaded families, project/shared
parameters) so AI agents can discover uncovered APIs and fall back to
`execute_raw`.

## CliFailurePreprocessor — Transaction Safety

**File**: `CliBridge/Handlers/CliFailurePreprocessor.cs`

Implements `IFailuresPreprocessor`: `Warning` → auto-delete; `Error` → attempt
resolve + `ProceedWithCommit`. **Why**: AI agents can't click Revit's modal "OK"
button — without this, the workflow freezes. Apply via
`HandlerUtilities.ConfigureFailureHandling(this Transaction)`.

## DryRunTransaction — Auto-Rollback

**File**: `CliBridge/Handlers/DryRunTransaction.cs`

Wraps `Transaction` so `Commit()` rolls back when `cmd.DryRun` is true. Handlers
advertising `SupportsDryRun => true` use it to simulate modifications:

```csharp
using (var tx = new DryRunTransaction(doc, "Create Wall", cmd.DryRun))
{
    // ... Revit API ops ...
    tx.Commit();  // rolls back if dry-run
}
```

## ParameterBinder — Typed Binding

**Files**: `Abstractions/ParameterBinder.cs` + `ParamAttribute.cs` +
`Handlers/BridgeCommandBase.cs`

Replaces per-handler `HandlerUtilities.GetXxxOrNull` + null-check boilerplate
with a declarative POCO + `[Param]` pattern. Declare a POCO, call `TryBind<T>`,
and the binder handles lookup, defaults, type conversion, and structured errors.

```csharp
public class CreateWallParams
{
    [Param("start_x", Required = true)] public double StartX { get; set; }
    [Param("height", Default = 3000.0)]  public double Height { get; set; }
}

// In the handler:
var p = TryBind<CreateWallParams>(cmd, out var error);
if (p is null) return error!;  // missing/type-mismatch response already built
```

Supported types: `int`, `int?`, `double`, `double?`, `string`, `int[]`, `bool`,
`bool?`. Nullable value types are optional. Exceptions map to responses:
`MissingParameterException` → "Missing required parameter: {name}";
`ParameterTypeException` → conversion error.

**Migration status**: 3 handlers migrated as reference (`create_wall`,
`move_element`, `create_family_instance`); the rest use legacy
`HandlerUtilities` incrementally. Old API remains fully supported.

## Observability — Logging & Metrics

**Files**: `CliBridge/CliLogger.cs` + `CliBridge/MetricsCollector.cs`

### CliLogger

Structured logger writing to a daily-rotated file under the bridge log
directory. Each line is grep-filterable:

```
[2026-08-13 14:30:45.123] [INFO] request_received request_id=ab12cd34 method=POST path=/api/execute
[2026-08-13 14:30:45.500] [INFO] command_completed request_id=ab12cd34 command=create_wall duration_ms=42 status=success
[2026-08-13 14:31:02.000] [WARN] plugin_rejected_unsigned dll=evil.dll reason="unsigned and allow_unsigned_plugins is false"
```

- **Buffered I/O**: 4 KB `StreamWriter` with auto-flush; `Shutdown()` forces a
  final flush during teardown (called from `CliBridgeStateManager.Cleanup`).
- **Levels**: `Info`, `Warn`, `Error` — each accepts `params (string, object)[]`
  fields rendered as `key=value` pairs.
- **Daily rotation**: file name includes `yyyy-MM-dd`; new day → new file.

### MetricsCollector

Process-wide counters updated by `CommandRouter` after every command and
served by `GET /api/metrics`. All counters use `Interlocked` so they're safe
under concurrent Revit-event execution:

- `total_commands`, `total_errors`, `total_duration_ms`
- per-command `CommandStats { Count, Errors, TotalDurationMs }` keyed by bare
  command name (so `create_wall@v1` and `create_wall@v2` aggregate together)
- `active_tasks` snapshot from `TaskRegistry`

`GetSnapshot()` returns a JSON-serializable DTO; see
[api-reference.md](api-reference.md#metrics-response--get-apimetrics) for the
shape. No persistence — counters reset on bridge restart.

## BridgePluginLoader — Signature Gating

**File**: `CliBridge/BridgePluginLoader.cs`

Loads third-party `IBridgeCommand` DLLs from `CliBridgePlugins/`. Before
calling `Assembly.LoadFrom`, each DLL is checked for an Authenticode signature
via `X509Certificate.CreateFromSignedFile`:

| Config | Unsigned DLL | Signed DLL, publisher not in `trusted_publishers` | Signed DLL, publisher trusted |
|--------|:------------:|:--------------------------------------------------:|:-----------------------------:|
| `allow_unsigned_plugins: false` (default), `trusted_publishers: []` | rejected | loaded (any valid sig accepted) | loaded |
| `allow_unsigned_plugins: false`, `trusted_publishers: ["CN=MyCorp"]` | rejected | rejected | loaded |
| `allow_unsigned_plugins: true` | loaded | loaded | loaded |

The publisher is extracted as the `CN=` segment of the certificate subject.
Every accept/reject decision is logged via `CliLogger` for audit. Signature
*presence* is checked; full WinVerifyTrust chain validation is intentionally
deferred (P/Invoke) and noted in code comments for a future hardening pass.

→ Next: [api-reference.md](api-reference.md) or [commands.md](commands.md).
