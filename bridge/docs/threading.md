# Thread Model & Safety

> How concurrency works and what guards are in place. See
> [components.md](components.md) for component internals.

## Revit Thread Constraints

```
┌───────────────────────────────────────────────────────┐
│                   Revit Process                       │
│                                                       │
│  ┌──────────────┐    ┌───────────────────────────┐    │
│  │ HTTP Server  │    │   Revit Main Thread       │    │
│  │ (background) │    │   (UI thread)             │    │
│  │              │    │                           │    │
│  │ Receive req  │───►│ IExternalEventHandler     │    │
│  │ Create Task  │    │   .Execute()              │    │
│  │ Enqueue+Raise│    │   CommandRouter.Execute() │    │
│  │ await TCS    │◄───│   SetCompleted/SetFailed  │    │
│  │ Return resp  │    │                           │    │
│  └──────────────┘    └───────────────────────────┘    │
│                                                       │
│  Concurrency: SemaphoreSlim(10,10) limits concurrent  │
│  request handlers to prevent unbounded task spawning  │
└───────────────────────────────────────────────────────┘
```

**Key principles**:

- Revit API **must only** be called on the main thread.
- `ExternalEvent.Raise()` notifies the main thread from a background thread.
- `IExternalEventHandler.Execute()` runs during the next UI idle.
- All `Transaction`s must be opened and committed within `Execute()`.

## Safety Measures

| Risk | Protection |
|------|----------|
| Revit dialog freeze | `CliFailurePreprocessor` auto-handles warnings/errors |
| Deadlock wait | `Task.WhenAny` timeout (default 180s) |
| Edit mode conflict | Check `doc.IsModifiable` |
| Invalid parameters | `ArgHelper` type-safe parsing + `ParameterBinder` validation |
| External network exposure | Listen on `localhost` only |
| Running when disabled | `CliBridgeStateManager` + config `enabled` toggle |
| Task memory leak | `CleanupOldTasks()` removes completed tasks after 5 min |
| Unbounded concurrency | `SemaphoreSlim(10, 10)` limits concurrent request handlers |
| Queue overflow | HTTP 429 when `CommandQueue.Count >= max` |
| Large request bodies | HTTP 413 for bodies > 10 MB |
| Double-set TCS | `TrySetResult` (not `SetResult`) in `SetCompleted`/`SetFailed` |
| Stale CTS after restart | `Start()` creates fresh `CancellationTokenSource` |
| SSE stream not closed | `responseHandled` flag + final `event: done` before cleanup |
| Race in TaskInfo | All mutable properties protected by `lock(_lock)` |
| Race in state manager | `volatile _isEnabled` + `lock(_lock)` for transitions |
| Re-entrancy in CommandRouter | `_initializing` flag breaks recursion during `GetTypes()` |
| Config thread safety | Double-check locking in `CliBridgeConfigLoader` |
| Cache corruption | Atomic writes (temp file + rename) in Go `SchemaCache` |
| Cache version mismatch | `LoadWithVersion()` detects bridge version changes |
| Process handle leak | `Process.Close()` after `GetProcessById()` (C#); `tasklist` (Go) |

→ Next: [integration.md](integration.md).
