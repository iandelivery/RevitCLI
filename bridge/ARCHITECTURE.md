# Revit CLI Bridge Framework Architecture

## 1. Overview

### 1.1 Design Goals

Build a complete framework that enables AI Agents to drive Revit software through a Command Line Interface (CLI) or HTTP API. Implement bidirectional communication between AI Agents and Revit, supporting receiving AI-generated instructions via CLI and converting them into Revit-executable operations, while returning execution results, status information, and error feedback to the AI Agent in real time.

### 1.2 Core Challenge

Revit's API is strictly single-threaded and bound to the Revit main process. External processes cannot directly call the Revit API. A **cross-process communication (IPC) + External Event** bridge architecture must be constructed.

### 1.3 Data Flow

```
┌─────────────┐     HTTP POST       ┌────────────────────┐     Enqueue      ┌──────────────────┐
│  AI Agent   │ ──────────────────► │  CLI Client /      │ ───────────────► │  TaskRegistry    │
│  (external) │                     │  HTTP Request      │                  │  CommandQueue    │
└──────┬──────┘                     └──────────┬─────────┘                  └────────┬─────────┘
       │                                       │                                     │
       │         JSON Response                 │         TaskInfo.Tcs                │ Raise()
       │◄──────────────────────────────────────┤◄────────────────────────────────────┤
       │                                       │                           ExternalEvent.Raise()
       │                                       │                                     │
       │                                       │                           ┌─────────▼────────┐
       │                                       │                           │ Revit Main Thread│
       │                                       │                           │CliCommandHandler │
       │                                       │                           │   .Execute()     │
       │                                       │                           └─────────┬────────┘
       │                                       │                                     │
       │                                       │                           CommandRouter.Execute()
       │                                       │                                     │
       │                                       │                           ┌─────────▼────────┐
       │                                       │                           │ Revit API Calls  │
       │                                       │                           │ Transaction      │
       │                                       │                           └──────────────────┘
```

**Async Task Mode:**

```
POST /api/execute {async: true}
       │
       ▼
  Immediately return {task_id, status: "pending"}
       │
       ▼
  GET /api/task/{task_id}  ← polling
       │
       ├── {status: "running", progress: 50, progress_message: "..."}
       │
       └── {status: "completed", result: {...}}
```

## 2. Architecture Layers

### 2.1 Layer Overview

| Layer | Namespace | Responsibility | Key Components |
|------|----------|------|----------|
| L1 - IPC Layer | `CliBridge` | Receive external HTTP requests | `CliHttpServer` |
| L2 - Bridge Layer | `CliBridge` | Async-to-sync coordination | `TaskRegistry`, `TaskInfo` |
| L3 - Execution Layer | `CliBridge` | Revit main thread execution | `CliCommandHandler` |
| L4 - Routing Layer | `CliBridge` | Command dispatch | `CommandRouter` |
| L5 - Handler Layer | `CliBridge.Handlers` | Individual command logic | 42 Handler files |
| L6 - Safety Layer | `CliBridge` | Transaction failure handling | `CliFailurePreprocessor` |
| L7 - Utility Layer | `CliBridge` | Logging, config, state | `CliLogger`, `CliBridgeConfigLoader`, `CliBridgeStateManager` |

### 2.2 Project Structure

```
revit-cli-opensource/
├── bridge/
│   └── RevitCliBridge/                       # Main project
│       ├── RevitCliBridge.csproj             # Multi-target R19-R22
│       ├── RevitCliBridge.addin              # Revit add-in manifest
│       ├── BridgeApp.cs                      # IExternalApplication entry
│       ├── ToggleBridgeCommand.cs            # Toggle command
│       ├── CliBridge/                        # Core source
│       │   ├── Models/
│       │   │   ├── RevitCommandInput.cs      # Command input DTO
│       │   │   ├── CommandResponse.cs        # Command response DTO
│       │   │   ├── CliBridgeConfig.cs        # Config model + loader
│       │   │   └── WallEntry.cs              # Wall batch creation entry
│       │   ├── Handlers/                     # Individual command handlers
│       │   │   ├── HandlerUtilities.cs       # Shared parameter parsing utilities
│       │   │   ├── PingHandler.cs
│       │   │   ├── DocumentInfoHandler.cs
│       │   │   ├── GetElementsHandler.cs
│       │   │   ├── GetElementByIdHandler.cs
│       │   │   ├── GetElementTypesHandler.cs
│       │   │   ├── GetFamilySymbolsHandler.cs
│       │   │   ├── GetFamilySymbolHandler.cs
│       │   │   ├── GetAllLevelsHandler.cs
│       │   │   ├── GetParametersHandler.cs
│       │   │   ├── SetParameterHandler.cs
│       │   │   ├── SetParameterByIdHandler.cs
│       │   │   ├── CreateWallHandler.cs
│       │   │   ├── CreateWallsHandler.cs
│       │   │   ├── CreateDoorHandler.cs
│       │   │   ├── CreateWindowHandler.cs
│       │   │   ├── CreateGridHandler.cs
│       │   │   ├── CreateFamilyInstanceHandler.cs
│       │   │   ├── CreateViewHandler.cs
│       │   │   ├── CreateSheetHandler.cs
│       │   │   ├── CreateRoomHandler.cs
│       │   │   ├── DeleteElementHandler.cs
│       │   │   ├── SetWallConstraintHandler.cs
│       │   │   ├── SetWallsConstraintHandler.cs
│       │   │   ├── MoveElementHandler.cs
│       │   │   ├── CopyElementHandler.cs
│       │   │   ├── RotateElementHandler.cs
│       │   │   ├── MirrorElementHandler.cs
│       │   │   ├── SetOffsetHandler.cs
│       │   │   ├── SetActiveViewHandler.cs
│       │   │   ├── ZoomToFitHandler.cs
│       │   │   ├── SelectElementsHandler.cs
│       │   │   ├── ExportViewHandler.cs
│       │   │   ├── BatchSetParamHandler.cs
│       │   │   ├── SearchElementsHandler.cs
│       │   │   ├── GetViewsHandler.cs
│       │   │   ├── ApplyViewTemplateHandler.cs
│       │   │   ├── GetSheetsHandler.cs
│       │   │   ├── PlaceOnSheetHandler.cs
│       │   │   ├── BatchExportHandler.cs
│       │   │   ├── GetRoomsHandler.cs
│       │   │   ├── TagRoomsHandler.cs
│       │   │   └── UndoHandler.cs
│       │   ├── TaskRegistry.cs               # Global task registry with state tracking
│       │   ├── CliCommandHandler.cs          # IExternalEventHandler implementation
│       │   ├── CliFailurePreprocessor.cs     # IFailuresPreprocessor implementation
│       │   ├── CommandRouter.cs              # Command router (42 commands)
│       │   ├── CliHttpServer.cs              # HTTP REST API server
│       │   ├── CliBridgeStateManager.cs      # Bridge on/off state management
│       │   ├── PortAllocator.cs              # Dynamic port allocation by Revit version
│       │   ├── InstanceRegistry.cs           # Instance registry file management
│       │   ├── LlmsTxtGenerator.cs           # llms.txt API reference generator
│       │   └── CliLogger.cs                  # Logging system
│       └── .config/
│           └── cli_bridge_setting.json       # CLI Bridge configuration
│
└── client/                                   # Go CLI client
    ├── go.mod
    ├── build.sh                              # Cross-platform build script
    ├── cmd/revit-cli/
    │   └── main.go                           # CLI entry point
    └── internal/
        ├── abstractions/                     # ArgHelper, command interfaces
        ├── client/
        │   ├── builtin/                      # Built-in commands (11)
        │   │   ├── system.go                 # ping, status, health, task
        │   │   ├── commands.go               # commands, schema
        │   │   ├── raw.go                    # raw
        │   │   ├── executeraw.go             # execute_raw
        │   │   ├── instances.go              # list
        │   │   ├── configure.go              # configure setup/teardown/check/port
        │   │   └── llms.go                   # llms
        │   ├── discovery/                    # Schema discovery + cache
        │   └── helptext.go                   # Help text generation
        ├── config/                           # Config loader (embedded defaults)
        ├── instance/                         # Instance discovery (registry files)
        └── models/                           # DTOs matching C# wire contract
```

## 3. Core Components

### 3.1 CliHttpServer - IPC Communication Layer

**File**: `CliBridge/CliHttpServer.cs`

**Responsibility**: Lightweight HTTP server running on `localhost`, providing REST API endpoints.

**Endpoints**:

| Method | Path | Function | Request Body |
|------|------|------|--------|
| POST | `/api/execute` | Execute CLI command (sync, async, or SSE) | `RevitCommandInput` JSON |
| GET  | `/api/task/{task_id}` | Query task status & result | None |
| GET  | `/api/task` | List all tasks (latest 50) | None |
| GET  | `/api/status` | Server status | None |
| GET  | `/api/health` | Health check | None |
| GET  | `/api/identity` | Instance identity (version, PID, port) | None |
| GET  | `/api/commands` | Full command schema (ETag support) | None |
| GET  | `/api/commands/{name}` | Single command schema | None |
| GET  | `/api/llms.txt` | Revit API reference for AI agents | None |

**Execution Modes**:

The `/api/execute` endpoint supports two modes controlled by the `async` field in the request body:

| Mode | `async` Value | Behavior |
|------|------|------|
| Sync (default) | `false` or omitted | HTTP connection held until result or timeout |
| Async | `true` | Immediately returns `task_id`, poll `/api/task/{id}` for result |

**Sync Mode Flow**:

```
HTTP POST /api/execute
    │
    ▼
1. Parse JSON → RevitCommandInput
    │
    ▼
2. TaskRegistry.CreateTask(taskId, command)
    │
    ▼
3. Enqueue QueuedCommand → CommandQueue
    │
    ▼
4. ExternalEvent.Raise() — Wake up Revit main thread
    │
    ▼
5. await Task.WhenAny(taskInfo.Tcs.Task, Task.Delay(timeout))
    │              │                          │
    │              │ Timeout                  │ Revit completed
    │              ▼                          ▼
    │         SetFailed()              SetCompleted()
    │              │                          │
    ▼◀─────────────┴──────────────────────────┘
6. Return JSON response
```

**Async Mode Flow**:

```
HTTP POST /api/execute {async: true}
    │
    ▼
1-4. Same as sync
    │
    ▼
5. Immediately return {task_id, status: "pending"}
    │
    │    ┌──────────────────────────────────────┐
    │    │  GET /api/task/{task_id} (polling)   │
    │    │  → {status: "running", progress: 50} │
    │    │  → {status: "completed", result: …}  │
    │    └──────────────────────────────────────┘
```

### 3.2 TaskRegistry - Async-to-Sync Bridge

**File**: `CliBridge/TaskRegistry.cs`

**Responsibility**: Global coordination of async communication between CLI requests and Revit main thread execution, with full task lifecycle tracking.

```csharp
public static class TaskRegistry
{
    // All tasks with state tracking
    public static ConcurrentDictionary<string, TaskInfo> Tasks { get; }

    // Command queue for Revit main thread
    public static ConcurrentQueue<QueuedCommand> CommandQueue { get; }

    // External event to wake up Revit main thread
    public static ExternalEvent? RevitEvent { get; set; }

    // Lifecycle methods
    public static TaskInfo CreateTask(string taskId, string command);
    public static void SetRunning(string taskId);
    public static void SetProgress(string taskId, int progress, string? message = null);
    public static void SetCompleted(string taskId, string resultJson);
    public static void SetFailed(string taskId, string errorJson);
    public static void CleanupOldTasks(int maxAgeSeconds = 300);
}
```

**Task Lifecycle**:

```
pending → running → completed
                  → failed
                  → timeout
```

**TaskInfo Model**:

| Field | Type | Description |
|------|------|------|
| `TaskId` | `string` | Unique task identifier |
| `Command` | `string` | Command name |
| `Status` | `TaskStatus` | pending/running/completed/failed/timeout |
| `Progress` | `int` | Progress percentage (0-100) |
| `ProgressMessage` | `string?` | Progress description |
| `ResultJson` | `string?` | Result JSON (available when completed/failed) |
| `CreatedAt` | `DateTime` | Task creation time |
| `StartedAt` | `DateTime?` | Execution start time |
| `CompletedAt` | `DateTime?` | Execution end time |
| `Tcs` | `TaskCompletionSource<string>` | [JsonIgnore] Signal for sync mode |

**Design Pattern**: `TaskCompletionSource` + `ConcurrentQueue` + `ExternalEvent`

**Why are these three components needed?**

| Component | Problem Solved |
|------|------------|
| `ConcurrentQueue` | CLI requests may arrive concurrently; queue guarantees FIFO ordered execution |
| `TaskCompletionSource` | Revit API calls are async; need to suspend HTTP response until execution completes |
| `ExternalEvent` | The only safe way to call Revit API cross-thread; wakes up main thread from background thread |

### 3.3 CliCommandHandler - Revit Main Thread Executor

**File**: `CliBridge/CliCommandHandler.cs`

**Responsibility**: Implements `IExternalEventHandler`, executes CLI commands on the Revit main thread.

```csharp
public void Execute(UIApplication app)
{
    while (TaskRegistry.CommandQueue.TryDequeue(out var queuedCommand))
    {
        TaskRegistry.SetRunning(queuedCommand.TaskId);

        try
        {
            var resultJson = CommandRouter.Execute(app, queuedCommand);
            TaskRegistry.SetCompleted(queuedCommand.TaskId, resultJson);
        }
        catch (Exception ex)
        {
            var errorJson = CommandResponse.Error(...).ToJson();
            TaskRegistry.SetFailed(queuedCommand.TaskId, errorJson);
        }
    }
}
```

**Key Features**:

- `while` loop drains the queue, processing all backlogged commands in one wake-up
- `TaskRegistry.SetRunning/SetCompleted/SetFailed` tracks full lifecycle
- `try/catch` guarantees failed commands also release pending tasks

### 3.4 CommandRouter - Command Router

**File**: `CliBridge/CommandRouter.cs`

**Responsibility**: Routes command names to corresponding handler functions using a registry pattern.

**Registered Commands (42 total)**:

| Category | Command | Function | Modifies Model |
|------|------|------|:------------:|
| **System** | `ping` | Connection test | No |
| **Document** | `get_document_info` | Get active document info | No |
| **Query** | `get_elements` | List elements | No |
| | `get_element_by_id` | Get element by ID | No |
| | `get_element_types` | List ElementTypes (WallType, FloorType...) | No |
| | `get_family_symbols` | List FamilySymbols | No |
| | `get_family_symbol` | Get specific FamilySymbol by name or instance | No |
| | `get_levels` | List all levels | No |
| | `get_parameters` | Get element parameters | No |
| | `get_views` | List views | No |
| | `get_sheets` | List sheets | No |
| | `get_rooms` | List rooms | No |
| | `search_elements` | Search elements by parameter | No |
| **Create** | `create_wall` | Create single wall | Yes |
| | `create_walls` | Batch create walls | Yes |
| | `create_door` | Create door | Yes |
| | `create_window` | Create window | Yes |
| | `create_grid` | Create grid | Yes |
| | `create_family_instance` | Create family instance | Yes |
| | `create_view` | Create view | Yes |
| | `create_sheet` | Create sheet | Yes |
| | `create_room` | Create room | Yes |
| **Modify** | `set_parameter` | Set parameter value | Yes |
| | `set_parameter_by_id` | Set parameter by BuiltInParameter | Yes |
| | `set_wall_constraint` | Set wall top/base constraint | Yes |
| | `set_walls_constraint` | Batch set wall constraints | Yes |
| | `set_offset` | Set element offset | Yes |
| | `batch_set_param` | Batch set parameter | Yes |
| | `apply_view_template` | Apply view template | Yes |
| | `tag_rooms` | Tag rooms | Yes |
| | `place_on_sheet` | Place viewport on sheet | Yes |
| **Transform** | `move_element` | Move element | Yes |
| | `copy_element` | Copy element | Yes |
| | `rotate_element` | Rotate element | Yes |
| | `mirror_element` | Mirror element | Yes |
| **View** | `set_active_view` | Set active view | No |
| | `zoom_to_fit` | Zoom to fit elements | No |
| | `select_elements` | Get or set selection | No |
| | `export_view` | Export current view as image | No |
| | `batch_export` | Batch export views/sheets | Yes |
| **Other** | `delete_element` | Delete element | Yes |
| | `undo` | Undo operations | No |

**Adding a New Command**:

1. Create handler file in `CliBridge/Handlers/`:

```csharp
public static class MyCommandHandler
{
    public static string Handle(UIApplication app, QueuedCommand cmd)
    {
        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return CommandResponse.Error(cmd.TaskId, "No active document.").ToJson();

        var parameters = cmd.Parameters as Dictionary<string, object>
            ?? new Dictionary<string, object>();

        using (Transaction t = new Transaction(doc, "CLI My Command"))
        {
            t.Start();
            var options = t.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new CliFailurePreprocessor());
            t.SetFailureHandlingOptions(options);

            // ... Revit API operations

            t.Commit();
        }

        return CommandResponse.Success(cmd.TaskId, result, "done").ToJson();
    }
}
```

2. Register in `CommandRouter` static constructor:

```csharp
Register("my_command", MyCommandHandler.Handle);
```

3. Add CLI client handler in `RevitCliClient/Handlers/`

4. Add case in `Program.cs` switch

### 3.5 CliBridgeStateManager - Bridge State Management

**File**: `CliBridge/CliBridgeStateManager.cs`

**Responsibility**: Manages the on/off state of the CLI Bridge, with dynamic port allocation and instance registry support for multi-instance scenarios.

```csharp
public static class CliBridgeStateManager
{
    public static bool IsEnabled { get; }
    public static int RevitVersion { get; }
    public static int ActualPort { get; }
    public static UIApplication? UIApplication { get; }
    public static void Initialize(int revitVersion = 0);  // Start if config enabled
    public static bool Toggle();                           // Toggle on/off, returns new state
    public static void Stop();                             // Stop bridge
    public static void Cleanup();                          // Full cleanup
    public static void SetUIApplication(UIApplication);   // Store UIApp ref
    public static void UpdateActiveDocument(string?);      // Update registry
}
```

**Integration**:

- `BridgeApp.OnStartup()` detects Revit version and calls `CliBridgeStateManager.Initialize(revitVersion)`
- `BridgeApp.OnShutdown()` calls `CliBridgeStateManager.Cleanup()`
- `ToggleBridgeCommand` provides Revit UI toggle button
- On start, writes instance registry file to `%AppData%\revit-cli\instances\revit-{version}-{pid}.json`
- On stop, deletes the registry file

### 3.5.1 PortAllocator - Dynamic Port Allocation

**File**: `CliBridge/PortAllocator.cs`

**Responsibility**: Allocates available ports from version-specific ranges to avoid conflicts when multiple Revit instances are running.

**Port Scheme**:

| Revit Version | Base Port | Range |
|---------------|-----------|-------|
| 2019 | 5011 | 5011-5019 |
| 2020 | 5021 | 5021-5029 |
| 2021 | 5031 | 5031-5039 |
| 2022 | 5041 | 5041-5049 |

**Fallback Chain**: version range → configured `port` → ephemeral OS port

**Algorithm**:
1. Compute `base_port = 5000 + (version - 2018) * 10 + 1`
2. Probe `base_port` through `base_port + 9` for first available port
3. If all occupied, try the configured fallback `port`
4. If still unavailable, request an ephemeral port from the OS

### 3.5.2 InstanceRegistry - Instance Discovery

**File**: `CliBridge/InstanceRegistry.cs`

**Responsibility**: Manages instance registry files that enable the CLI client to discover running Revit instances.

**Registry File**: `%AppData%\revit-cli\instances\revit-{version}-{pid}.json`

```json
{
  "pid": 5678,
  "version": 2022,
  "port": 5041,
  "document": "Project1.rvt",
  "started_at": "2026-06-19T10:30:00Z",
  "hostname": "localhost",
  "commands_count": 64
}
```

**Lifecycle**:
- Written on bridge startup via `InstanceRegistry.Register()`
- Deleted on bridge shutdown via `InstanceRegistry.Unregister()`
- Document field updated via `InstanceRegistry.UpdateDocument()`
- Stale files from crashed instances cleaned up on startup via `InstanceRegistry.CleanupStale()`

### 3.5.3 LlmsTxtGenerator - API Reference Generator

**File**: `CliBridge/LlmsTxtGenerator.cs`

**Responsibility**: Generates a text reference file (`llms.txt`) that describes the raw Revit API elements, parameters, and classes available in the running instance. Enables AI agents to discover uncovered API structures and use `execute_raw` as a fallback.

**Content Sections**:
- BuiltIn categories (dynamic from active document, static fallback)
- Common BuiltIn parameters (static reference)
- Element class hierarchy (static reference)
- Element filter classes (static reference)
- Loaded families (dynamic, requires active document)
- Project/shared parameters (dynamic, requires active document)
- Registered bridge commands

### 3.6 CliFailurePreprocessor - Failure Preprocessor

**File**: `CliBridge/CliFailurePreprocessor.cs`

**Responsibility**: Implements `IFailuresPreprocessor`, automatically handles warnings and errors in Revit transactions.

**Handling Strategy**:

| Severity | Action |
|--------|----------|
| `Warning` | Auto-delete (`DeleteWarning`) |
| `Error` | Attempt resolve (`ResolveFailure`) + `ProceedWithCommit` |

**Why is this needed?**

AI Agents have no eyes or mouse to click Revit's "OK" button. Without the failure preprocessor, Revit would pop up dialogs waiting for user action, causing the AI workflow to permanently freeze.

**Usage**:

```csharp
using (Transaction t = new Transaction(doc, "CLI Action"))
{
    t.Start();
    var options = t.GetFailureHandlingOptions();
    options.SetFailuresPreprocessor(new CliFailurePreprocessor());
    t.SetFailureHandlingOptions(options);
    // ... Revit API operations
    t.Commit();
}
```

## 4. Data Models

### 4.1 RevitCommandInput - Request Model

```json
{
    "task_id": "uuid-optional",
    "command": "get_elements",
    "parameters": {
        "category": "OST_Walls"
    },
    "timeout_seconds": 120,
    "async": false
}
```

| Field | Type | Required | Description |
|------|------|------|------|
| `task_id` | `string` | No | Auto-generated if omitted |
| `command` | `string` | Yes | Command name |
| `parameters` | `object` | No | Command-specific parameters |
| `timeout_seconds` | `int` | No | Default: 120 |
| `async` | `bool` | No | Default: false. If true, returns task_id immediately |

### 4.2 CommandResponse - Response Model

**Success**:

```json
{
    "task_id": "abc-123",
    "status": "success",
    "message": "Retrieved 5 elements.",
    "data": {
        "count": 5,
        "elements": [...]
    }
}
```

**Error**:

```json
{
    "task_id": "abc-123",
    "status": "error",
    "message": "Command failed: ...",
    "error_details": "System.Exception: ..."
}
```

### 4.3 Task Status Response

```json
{
    "task_id": "abc-123",
    "command": "create_walls",
    "status": "running",
    "progress": 50,
    "progress_message": "Creating wall 50/100...",
    "started_at": "2024-01-15T14:30:45.1230000+08:00",
    "completed_at": null
}
```

### 4.4 CliBridgeConfig - Configuration Model

**File**: `.config/cli_bridge_setting.json`

```json
{
    "enabled": true,
    "port": 5000,
    "auto_port": true,
    "timeout_seconds": 180,
    "max_command_queue_size": 100,
    "allow_raw_execution": true
}
```

| Field | Type | Default | Description |
|------|------|---------|-------------|
| `enabled` | `bool` | `true` | Auto-start bridge on Revit launch |
| `port` | `int` | `5000` | Fallback TCP port |
| `auto_port` | `bool` | `true` | Dynamically allocate port based on Revit version |
| `timeout_seconds` | `int` | `180` | Command execution timeout |
| `max_command_queue_size` | `int` | `100` | Maximum pending commands |
| `allow_raw_execution` | `bool` | `true` | Allow `execute_raw` command |

## 5. Revit Plugin Integration

### 5.1 Initialization Flow

In `BridgeApp.OnStartup()`, the bridge is initialized with version detection:

```csharp
public Result OnStartup(UIControlledApplication application)
{
    // Detect Revit version
    int revitVersion = 0;
    int.TryParse(application.ControlledApplication.VersionNumber, out revitVersion);

    // Initialize with version info (enables auto port allocation)
    CliBridgeStateManager.Initialize(revitVersion);
    // ... create Ribbon UI
    return Result.Succeeded;
}
```

### 5.2 Shutdown Flow

In `BridgeApp.OnShutdown()`, clean up resources:

```csharp
public Result OnShutdown(UIControlledApplication application)
{
    CliBridgeStateManager.Cleanup();
    return Result.Succeeded;
}
```

### 5.3 Toggle Command

Users can toggle CLI Bridge on/off from the Revit Ribbon:

- Button: "AI Mode Toggle" in "AI Tools" panel of "Revit CLI Bridge" tab
- Command: `ToggleBridgeCommand`
- Shows TaskDialog with current state after toggle

## 6. CLI Client

### 6.1 Project Info

**Location**: `client/` (Go) / `bridge/RevitCliBridge/CliBridge/Handlers/` (C# server-side)

**Dependencies**: `Newtonsoft.Json` only, no Revit API dependency (can be compiled and run independently)

### 6.2 Architecture

The CLI client uses a handler pattern with shared utilities:

- **`ArgHelper`** (`abstractions/arghelper.go`): Centralized argument parsing with type safety
  - `FindArg(args, flag)` → `string?` — Find flag value
  - `GetInt(args, flag)` → `int?` — Type-safe int parsing
  - `GetDouble(args, flag)` → `double?` — Type-safe double parsing
  - `HasFlag(args, flag)` → `bool` — Check boolean flag
  - `ParseIds(ids)` → `List<int>?` — Parse comma-separated IDs
  - `ParseIdsToArray(ids)` → `int[]?` — Parse to array
  - `TryParseValue(value)` → `object` — Auto-detect int/double/string

- **`instance`** (`instance/discovery.go`): Instance discovery via registry files
  - `Discover()` → `[]InstanceInfo` — Read all alive instances from `%AppData%`
  - `ResolveURL(url, pid, revit)` → `string` — Resolve bridge URL with priority chain
  - Priority: `--url` > `--pid` > `--revit` > auto-discover (single instance) > fallback

### 6.3 Command Execution Flow

```
CLI Client                              Revit Server
    │                                       │
    │  POST /api/execute                    │
    │  {command, parameters}                │
    │ ─────────────────────────────────────►│
    │                                       │
    │  {status: "pending", task_id}         │  ← async mode
    │◄───────────────────────────────────── │
    │                                       │
    │  GET /api/task/{task_id}              │
    │ ─────────────────────────────────────►│
    │                                       │
    │  {status: "running", progress: 50}    │
    │◄───────────────────────────────────── │
    │                                       │
    │  GET /api/task/{task_id}              │
    │ ─────────────────────────────────────►│
    │                                       │
    │  {status: "completed", result: {...}} │
    │◄───────────────────────────────────── │
```

### 6.4 AI Agent Direct HTTP Calls

AI Agents can send HTTP requests directly without the CLI client. When `auto_port` is enabled, the port varies by Revit version — use `GET /api/identity` to verify the target instance:

```bash
# Discover running instances
# Read registry files from %AppData%\revit-cli\instances\

# Sync mode (default) — Revit 2022 on port 5041
curl -X POST http://localhost:5041/api/execute \
  -H "Content-Type: application/json" \
  -d '{"command": "get_elements", "parameters": {"category": "OST_Walls"}}'

# Verify instance identity
curl http://localhost:5041/api/identity

# Async mode
curl -X POST http://localhost:5041/api/execute \
  -H "Content-Type: application/json" \
  -d '{"command": "create_walls", "async": true, "parameters": {...}}'

# Poll task status
curl http://localhost:5041/api/task/abc-123

# Get API reference for uncovered operations
curl http://localhost:5041/api/llms.txt
```

## 7. Thread Model & Safety

### 7.1 Revit Thread Constraints

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
└───────────────────────────────────────────────────────┘
```

**Key Principles**:

- Revit API **must only** be called on the main thread
- `ExternalEvent.Raise()` notifies the main thread from background thread
- `IExternalEventHandler.Execute()` is automatically called during next UI idle
- All `Transaction` must be opened and committed within `Execute()`

### 7.2 Safety Measures

| Risk | Protection |
|------|----------|
| Revit dialog freeze | `CliFailurePreprocessor` auto-handles |
| Deadlock wait | `Task.WhenAny` timeout (default 120s) |
| Edit mode conflict | Check `doc.IsModifiable` |
| Invalid parameters | `ArgHelper` type-safe parsing + handler validation |
| External network exposure | Listen on `localhost` only |
| Running when disabled | `CliBridgeStateManager` + config toggle |
| Task memory leak | `CleanupOldTasks()` removes completed tasks after 5 min |

## 8. Unit Convention

CLI command dimension parameters uniformly use **millimeters (mm)**, internally auto-converted to Revit's **feet**:

```csharp
// Input: mm → Revit: feet
var start = new XYZ(startX.Value.MillimeterToFeet(), startY.Value.MillimeterToFeet(), 0);

// Return: Revit: feet → Output: mm
location_x = locX.FeetToMillimeter()
```

## 9. Command Naming Convention

All CLI commands follow a consistent naming pattern:

| Pattern | Prefix | Examples |
|------|------|------|
| Query | `get_` | `get_elements`, `get_levels`, `get_family_symbols` |
| Create | `create_` | `create_wall`, `create_grid`, `create_family_instance` |
| Set | `set_` | `set_parameter`, `set_wall_constraint`, `set_active_view` |
| Transform | `verb_element` | `move_element`, `copy_element`, `rotate_element` |
| Batch | `batch_` or plural | `batch_set_param`, `batch_export`, `create_walls` |
| Other | direct verb | `delete_element`, `select_elements`, `export_view`, `undo` |

## 10. Dependencies

```
RevitCliBridge.dll (IExternalApplication)
    │
    ├── depends → RevitCliBridge.Abstractions.dll
    │              │
    │              └── Newtonsoft.Json
    │
    ├── .NET Framework System.Net (HttpListener)
    │
    └── Startup initialization
           ├── CliBridgeStateManager.Initialize(revitVersion)
           │     ├── PortAllocator.AllocatePort() → dynamic port
           │     ├── InstanceRegistry.Register() → %AppData% registry file
           │     ├── CliCommandHandler → ExternalEvent
           │     └── CliHttpServer → localhost:{port}
           └── ToggleBridgeCommand → Ribbon button
```

## 11. Configuration File Paths

| File | Path | Purpose |
|------|------|---------|
| `cli_bridge_setting.json` | `.config/` | CLI Bridge on/off, port, auto_port, timeout |
| Instance registry | `%AppData%\revit-cli\instances\revit-{version}-{pid}.json` | Running instance discovery |
| Schema cache | `%AppData%\revit-cli\` | Client-side command schema cache |

All `.config/` files are copied to build output via `<CopyToOutputDirectory>Always</CopyToOutputDirectory>`.
