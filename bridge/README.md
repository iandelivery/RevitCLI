# RevitCliBridge

A Revit add-in that runs an HTTP server inside Revit, exposing Revit API operations as CLI commands. AI agents and automation scripts can send commands via HTTP and receive real-time progress updates via Server-Sent Events (SSE).

## Requirements

- **Revit 2019, 2020, 2021, or 2022**
- **.NET Framework 4.7** (Revit 2019/2020) or **4.8** (Revit 2021/2022)
- **Visual Studio 2022** with .NET desktop workload

## Build

1. Open `RevitCliBridge.sln` in Visual Studio
2. Select the configuration matching your Revit version (e.g. `Release R22` for Revit 2022)
3. Build the solution

The output DLLs will be in `RevitCliBridge/bin/Release R22/` (or corresponding version).

## Installation

### Automated (recommended)

```bash
revit-cli.exe configure setup
```

This scans for installed Revit versions, copies the add-in files, and generates version-specific configuration with correct port assignments.

### Manual

1. Create a folder in your Revit addins directory:
   ```
   %APPDATA%\Autodesk\Revit\Addins\<version>\RevitCliBridge\
   ```
   (e.g. `C:\Users\YourName\AppData\Roaming\Autodesk\Revit\Addins\2022\RevitCliBridge\`)

2. Copy these files into that folder:
   - `RevitCliBridge.dll`
   - `RevitCliBridge.Abstractions.dll`
   - `Newtonsoft.Json.dll`
   - `RevitCliBridge.addin`
   - `.config\cli_bridge_setting.json`

3. Start Revit. You should see a "Revit CLI Bridge" tab with an "AI Mode Toggle" button.

## Configuration

Edit `.config\cli_bridge_setting.json`:

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

| Option | Default | Description |
|--------|---------|-------------|
| `enabled` | `true` | Auto-start the bridge when Revit launches |
| `port` | `5000` | Fallback TCP port (used when `auto_port` is disabled or fails) |
| `auto_port` | `true` | Dynamically allocate port based on Revit version |
| `timeout_seconds` | `180` | Command execution timeout |
| `max_command_queue_size` | `100` | Maximum pending commands |
| `allow_raw_execution` | `true` | Allow `execute_raw` command (C#/Python code execution) |

### Port Allocation

When `auto_port` is enabled (default), the bridge automatically selects an available port from a version-specific range:

| Revit Version | Base Port | Range |
|---------------|-----------|-------|
| 2019 | 5011 | 5011-5019 |
| 2020 | 5021 | 5021-5029 |
| 2021 | 5031 | 5031-5039 |
| 2022 | 5041 | 5041-5049 |

This allows multiple Revit instances (including different versions) to run simultaneously without port conflicts. Port 5000 is reserved as a legacy fallback.

### Multi-Instance Support

Each running bridge instance writes a registry file to `%AppData%\revit-cli\instances\revit-{version}-{pid}.json` on startup and deletes it on shutdown. Stale files from crashed instances are cleaned up automatically.

The CLI client uses these registry files to discover and connect to specific instances via `--revit <version>` or `--pid <pid>` flags.

## API Endpoints

| Method | Path | Function |
|--------|------|----------|
| POST | `/api/execute` | Execute a command (sync, async, or SSE mode) |
| GET | `/api/task` | List all tasks (latest 50) |
| GET | `/api/task/{task_id}` | Query task status & result |
| GET | `/api/status` | Server status |
| GET | `/api/health` | Health check |
| GET | `/api/identity` | Instance identity (version, PID, port) |
| GET | `/api/commands` | Full command schema (with ETag support) |
| GET | `/api/commands/{name}` | Single command schema |
| GET | `/api/llms.txt` | Revit API reference for AI agents |

### /api/identity

Returns identity information about the bridge instance, enabling the CLI client to verify which Revit instance it's connected to:

```json
{
  "version": 2022,
  "pid": 5678,
  "port": 5041,
  "hostname": "localhost",
  "commands_count": 64
}
```

### /api/llms.txt

Returns a text reference file describing the raw Revit API elements, parameters, and classes available in the running instance. This enables AI agents to discover uncovered API structures and use `execute_raw` as a fallback when the built-in commands don't cover a specific need.

The reference includes:
- BuiltIn categories
- Common BuiltIn parameters
- Element class hierarchy
- Element filter classes
- Loaded families (dynamic, requires active document)
- Project/shared parameters (dynamic, requires active document)
- Registered bridge commands

## Available Commands

The bridge auto-discovers all `IBridgeCommand` implementations. Built-in commands include:

### System
- `ping` — Test connection
- `status` — Server status (HTTP GET)
- `health` — Health check (HTTP GET)
- `task` — Query task status

### Query
- `get_elements` — Query elements by category
- `get_element_by_id` — Get element by ID
- `get_all_levels` — List all levels
- `get_parameters` — Get element parameters
- `get_views`, `get_sheets`, `get_rooms` — List views/sheets/rooms
- `get_family_symbols` — List family symbols
- `search_elements` — Search elements by name

### Create
- `create_wall`, `create_walls` — Create walls
- `create_door`, `create_window` — Create doors/windows
- `create_grid` — Create grid lines
- `create_room` — Create rooms
- `create_view` — Create views
- `create_sheet` — Create sheets
- `create_family_instance` — Place family instances

### Modify
- `set_parameter`, `set_parameter_by_id` — Set parameters
- `move_element`, `copy_element` — Transform elements
- `rotate_element`, `mirror_element` — Rotate/mirror
- `delete_element` — Delete elements
- `set_offset` — Set element offset

### Document
- `doc_open`, `doc_save`, `doc_save_as` — Document operations
- `doc_list` — List open documents
- `document_info` — Document info

### Export
- `export_view` — Export views
- `batch_export` — Batch export

## Plugin Development

Create third-party command handlers by implementing `IBridgeCommand`:

```csharp
using RevitCliBridge.Abstractions;

public class MyCommand : IBridgeCommand
{
    public string CommandName => "my_command";
    public string Description => "Does something useful";
    public string Category => "Custom";
    public bool SupportsDryRun => false;
    public string[] Aliases => new[] { "my_cmd" };
    public string[] Examples => new[] { "revit-cli.exe my_command --param value" };
    public CommandParamSchema[] Parameters => new[]
    {
        new CommandParamSchema { Name = "param", Type = "string", Required = true }
    };

    public string Handle(object uiApplication, QueuedCommand cmd)
    {
        var app = (Autodesk.Revit.UI.UIApplication)uiApplication;
        // ... your Revit API logic ...
        return CommandResponse.Success(cmd.TaskId, new { result = "done" }).ToJson();
    }
}
```

Compile your plugin DLL and place it in a `CliBridgePlugins/` subfolder next to `RevitCliBridge.dll`. The bridge will auto-discover and register it at startup.

## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full protocol specification.

## License

MIT — see [../LICENSE](../LICENSE)
