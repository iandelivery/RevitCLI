# Architecture Overview

## Design Goal

Enable AI Agents to drive Revit through a CLI or HTTP API. Revit's API is
strictly single-threaded and bound to the Revit process — external processes
cannot call it directly. The bridge solves this with **IPC (HTTP) + External
Event** coordination.

## Data Flow

```
┌─────────────┐   HTTP POST    ┌──────────────┐   Enqueue     ┌──────────────┐
│  AI Agent   │ ─────────────► │ CliHttpServer│ ────────────► │ TaskRegistry │
│  (external) │                │  (localhost) │               │ CommandQueue │
└──────┬──────┘                └──────┬───────┘               └──────┬───────┘
       │                              │    await TaskInfo.Tcs        │ Raise()
       │   JSON Response              │◄─────────────────────────────┤
       │◄─────────────────────────────┤                  ExternalEvent.Raise()
       │                              │                              │
       │                              │                  ┌───────────▼──────────┐
       │                              │                  │ Revit Main Thread    │
       │                              │                  │ CliCommandHandler    │
       │                              │                  │   .Execute()         │
       │                              │                  └───────────┬──────────┘
       │                              │                              │
       │                              │                  CommandRouter.Execute()
       │                              │                              ▼
       │                              │                  ┌──────────────────────┐
       │                              │                  │ Revit API + Transaction│
       │                              │                  └──────────────────────┘
```

**Async mode**: `POST /api/execute {async: true}` returns `{task_id, status:"pending"}`
immediately; poll `GET /api/task/{task_id}` for `running` → `completed`/`failed`.

## Architecture Layers

| Layer | Responsibility | Key Components |
|------|------|----------|
| L1 IPC | Receive HTTP requests | `CliHttpServer` |
| L2 Bridge | Async-to-sync coordination | `TaskRegistry`, `TaskStateMachine`, `TaskInfo` |
| L3 Execution | Revit main thread | `CliCommandHandler` |
| L4 Routing | Command dispatch + name resolution | `CommandRouter`, `CommandNameResolver` |
| L5 Handler | Per-command logic | 52 handlers in 10 category folders |
| L6 Binding | Typed parameter binding | `ParameterBinder`, `ParamAttribute`, `BridgeCommandBase.TryBind<T>` |
| L7 Safety | Transaction failure handling | `CliFailurePreprocessor`, `DryRunTransaction` |
| L8 Utility | Logging, config, state | `CliLogger`, `CliBridgeConfigLoader`, `CliBridgeStateManager` |

## Project Structure

```
revit-cli-opensource/
├── bridge/
│   ├── RevitCliBridge.Abstractions/      # Plugin SDK — netstandard2.0, no Revit dep
│   │   ├── IBridgeCommand.cs             # Plugin interface
│   │   ├── QueuedCommand.cs              # TaskId + Command + Parameters + DryRun
│   │   ├── CommandResponse.cs            # Success/Error factory + ToJson()
│   │   ├── CommandSchema.cs              # Schema DTOs for discovery
│   │   ├── CommandNameResolver.cs        # Pure-logic name resolution
│   │   ├── ParamAttribute.cs             # [Param] attribute + exceptions
│   │   └── ParameterBinder.cs            # Reflective POCO binder
│   │
│   ├── RevitCliBridge/                   # Main project — multi-target R19–R22
│   │   ├── BridgeApp.cs                  # IExternalApplication entry
│   │   └── CliBridge/
│   │       ├── Models/                   # RevitCommandInput, CliBridgeConfig, WallEntry
│   │       ├── Handlers/                 # 52 handlers in 10 category subfolders
│   │       │   ├── BridgeCommandBase.cs  # Abstract base + TryBind<T>
│   │       │   ├── DocumentCommandBase.cs
│   │       │   ├── CliFailurePreprocessor.cs
│   │       │   └── DryRunTransaction.cs
│   │       ├── TaskRegistry.cs           # Global state (delegates to TaskStateMachine)
│   │       ├── TaskStateMachine.cs       # Pure-logic task transitions
│   │       ├── TaskInfo.cs               # Task model (lock-protected, SSE broadcast)
│   │       ├── CliCommandHandler.cs      # IExternalEventHandler
│   │       ├── CommandRouter.cs          # Routing (delegates to CommandNameResolver)
│   │       ├── CliHttpServer.cs          # HTTP server + auth + concurrency
│   │       ├── CliBridgeStateManager.cs  # On/off state (thread-safe)
│   │       ├── PortAllocator.cs          # Version-based port allocation
│   │       ├── InstanceRegistry.cs       # Discovery files (atomic writes)
│   │       ├── BridgePluginLoader.cs     # 3rd-party plugin auto-discovery
│   │       └── LlmsTxtGenerator.cs       # llms.txt API reference
│   │
│   ├── RevitCliBridge.Tests/             # xUnit, net48 — no Revit dependency
│   │   ├── CommandNameResolverTests.cs   # 13 tests
│   │   ├── TaskStateMachineTests.cs      # 15 tests
│   │   ├── ParameterBinderTests.cs       # 17 tests
│   │   ├── CommandResponseTests.cs       # 4 tests
│   │   ├── PortAllocatorTests.cs         # 3 tests
│   │   └── CliBridgeConfigAuthTests.cs   # 9 tests
│   └── docs/                             # This documentation
│
└── client/                               # Go CLI client
    ├── cmd/revit-cli/main.go
    └── internal/                         # abstractions, client, config, instance, models
```

## Key Design Principle: Pure-Logic Extraction

Revit-coupled classes can't be unit-tested in isolation. To enable fast tests,
pure logic was extracted into separate static classes living in the
`Abstractions` project (no Revit reference):

| Global singleton (Revit-coupled) | Pure-logic extraction (testable) |
|---|---|
| `CommandRouter` | `CommandNameResolver` — name matching rules |
| `TaskRegistry` | `TaskStateMachine` — status transitions + SSE + TCS |

This is why an `Abstractions` project exists: third-party plugin authors compile
against it alone, and the test project links the pure-logic sources via
`<Compile Include>` without loading Revit.

→ Next: [components.md](components.md) for per-component detail.
