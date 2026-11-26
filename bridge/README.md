# RevitCliBridge

A Revit add-in that runs an HTTP server inside Revit, exposing Revit API operations as CLI commands. AI agents and automation scripts can send commands via HTTP and receive real-time progress updates via Server-Sent Events (SSE).

## Requirements

- **Revit 2019, 2020, 2021, 2022, 2023, or 2024**
- **.NET Framework 4.7** (Revit 2019/2020) or **4.8** (Revit 2021/2022/2023/2024)
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
   - `.config\cli_bridge_setting.json`

   Then copy `@RevitCliBridge.addin` into the Addins root (`...\Addins\<version>\`, one level above the `RevitCliBridge` folder). The `@` prefix makes Revit load the bridge before other add-ins, which is required for programmatic command registration from third-party add-ins.

3. Start Revit. The bridge starts in **AI Mode** automatically (controlled by `enabled` in `.config\cli_bridge_setting.json`, which is `true` by default). You should see a "Revit CLI Bridge" tab with an "AI Mode Toggle" button — it is only needed if you ever want to disable the bridge.

## Configuration

Edit `.config\cli_bridge_setting.json`:

```json
{
  "schema_version": "1",
  "enabled": true,
  "port": 5000,
  "auto_port": true,
  "timeout_seconds": 180,
  "max_command_queue_size": 100,
  "allow_raw_execution": true,
  "api_key": null
}
```

| Option | Default | Description |
|--------|---------|-------------|
| `schema_version` | `"1"` | Config schema version for future format migrations |
| `enabled` | `true` | Auto-start the bridge when Revit launches |
| `port` | `5000` | Fallback TCP port (used when `auto_port` is disabled or fails) |
| `auto_port` | `true` | Dynamically allocate port based on Revit version |
| `timeout_seconds` | `180` | Command execution timeout |
| `max_command_queue_size` | `100` | Maximum pending commands |
| `allow_raw_execution` | `true` | Allow `execute_raw` command (C# code execution) |
| `api_key` | `null` | Bearer token for `/api/*` endpoints. Auto-generated on first startup or by `configure setup`. `null`/empty disables auth (legacy mode). |

### Authentication

When `api_key` is non-empty, all `/api/*` endpoints (except `/api/health`,
`/api/identity`, and `/api/auth/status`) require an `Authorization: Bearer <api_key>`
header. The key is auto-generated on first bridge startup and persisted to
`cli_bridge_setting.json`. The client syncs this key into its auth cache during
`configure setup`, so authenticated commands work transparently.

To rotate the key, delete the `api_key` field from the config file and restart
Revit — a new key will be generated. Then re-run `configure setup` so the
client cache is updated.

To disable authentication, set `api_key` to `null` or `""` in the config.

### Upgrading

Upgrades keep the key stable on both supported paths:

- `revit-cli configure setup` (the documented update flow) rewrites the
  deployed config in place and preserves its `api_key`, then syncs the same
  key into the client cache. Revit can stay running — the preserved key is
  the one the bridge already loaded.
- `bridge\build.ps1 -Deploy` (the developer path) preserves the deployed
  `api_key` before copying the keyless template over it.

**Restart Revit after any deploy or file replacement.** A running bridge
keeps its binaries and its in-memory key until the process exits, so a
deploy only takes effect on the next start. If you ever replace the config
by hand and lose the key, re-run `configure setup` to generate a fresh one
and re-sync the client cache.

Public probe endpoints (no auth required):

- `GET /api/health` — liveness check
- `GET /api/identity` — instance identity (PID, version, document)
- `GET /api/auth/status` — reports whether auth is enabled


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

Each running bridge instance writes a registry file to `%LOCALAPPDATA%\revit-cli\instances\revit-{version}-{pid}.json` on startup and deletes it on shutdown. Stale files from crashed instances are cleaned up automatically.

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
| GET | `/api/catalog` | Lightweight command index (name, category, summary) |
| GET | `/api/llms.txt` | Revit API reference for AI agents |

### /api/identity

Returns identity information about the bridge instance, enabling the CLI client to verify which Revit instance it's connected to:

```json
{
  "version": 2022,
  "pid": 5678,
  "port": 5041,
  "hostname": "localhost",
  "commands_count": 72
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

## Units

Commands speak the **units contract**: a caller never receives a Revit internal unit. Every dimensional parameter and response field carries the request unit of its kind, and every response echoes the effective units in its `units` envelope field. Canonical units: `length=mm`, `area=m2`, `volume=m3`, `angle=deg`; a request chooses units with the top-level `units` field (a kind-to-unit map, or the string `project` to follow the active document's decimal display units — `get_units` reports them). Outputs round to 3 decimals; inputs are never rounded.

Handler-side mechanics (see [docs/commands.md](docs/commands.md) for the full contract):

- Schema parameters declare `unit_kind` — the middleware converts them from the request unit to Revit internal units before the handler runs
- `ResponseFieldKinds` maps response field names to kinds — the middleware rewrites them from internal units to the request units
- `ParameterUnits` serves the parameter commands, where the spec belongs to the element's parameter rather than the schema
- Handlers stay unit-blind and emit internal-unit values; conversion happens at one choke point

## Available Commands

The bridge auto-discovers all `IBridgeCommand` implementations — 105 built-in commands, listed below by their declared category. The running bridge is the source of truth: `revit-cli catalog`, `GET /api/catalog` (lightweight index), or `GET /api/commands` (full schemas) always reflect what is registered, including third-party plugins.

### System
- `ping` — Test bridge connectivity and return version info
- `batch` — Execute multiple commands in a single transaction group, with optional rollback on error

### Document
- `doc_open` — Open a document (.rvt, .rte, .rfa)
- `doc_close` — Close documents (the active document cannot be closed via API)
- `doc_save`, `doc_save_as` — Save the active document / save to a new path
- `doc_sync` — Synchronize with the central model
- `doc_list` — List open documents

### Query
- `get_elements`, `get_element_by_id` — Query elements (active or linked document)
- `search_elements` — Search elements by category and parameter value
- `get_element_types`, `get_family_symbol`, `get_family_symbols`, `get_symbol_instances` — Types and symbols
- `get_parameters` — Element parameters (with per-parameter `unit_kind`)
- `get_units` — The active document's unit matrix (project / canonical / allowed)
- `get_levels` — All levels
- `get_views`, `get_sheets`, `get_rooms` — Views/sheets/rooms
- `get_links` — Linked RVT documents
- `get_selected_elements` — Current selection
- `get_element_locations`, `get_element_boxes` — Element locations and bounding boxes
- `document_info` — Active document metadata
- `get_sheet_viewports`, `check_sheet_bounds`, `check_viewport_overlaps` — Sheet layout inspection
- MEP catalogs: `get_cable_trays`, `get_cable_tray_types`, `get_ducts`, `get_duct_types`, `get_duct_system_types`, `get_pipes`, `get_pipe_types`, `get_pipe_system_types`

### Create
- `create_wall`, `create_walls` — Walls
- `create_door`, `create_window` — Doors/windows on walls
- `create_grid` — Grid lines
- `create_dimensions` — Linear dimension across grid lines
- `create_room` — Rooms
- `create_view`, `duplicate_view` — Views
- `create_sheet` — Sheets
- `create_family_instance` — Family instances
- `create_beam`, `create_structural_column` — Structural framing and columns
- Cable trays: `create_cable_tray`, `create_elbow_fitting`, `create_tee_fitting`, `create_cross_fitting`, `create_transition_fitting`, `create_union_fitting`
- Ducts: `create_duct`, `create_duct_elbow_fitting`, `create_duct_tee_fitting`, `create_duct_cross_fitting`, `create_duct_transition_fitting`, `create_duct_union_fitting`, `create_duct_takeoff_fitting`
- Pipes: `create_pipe`, `create_pipe_elbow_fitting`, `create_pipe_tee_fitting`, `create_pipe_cross_fitting`, `create_pipe_transition_fitting`, `create_pipe_union_fitting`, `create_pipe_takeoff_fitting`
- `create_mep_fitting` — Unified fitting entry point for any pipe/duct/cable tray

### Modify
- `set_parameter`, `set_parameter_by_id`, `batch_set_param` — Set parameters
- `move_element`, `copy_element`, `rotate_element`, `mirror_element` — Transform elements
- `delete_element` — Delete elements
- `set_offset` — Base/top offset on walls or family instances
- `set_wall_constraint`, `set_walls_constraint` — Base/top level constraints on walls
- `undo` — Undo recent transactions
- `apply_view_template` — Apply a view template
- Sheet layout: `place_on_sheet`, `move_viewport`, `remove_view_from_sheet`, `tag_rooms`
- 3D views: `set_section_box` (explicit or computed from elements), `toggle_section_box`
- MEP/structural: `modify_duct`, `modify_pipe`, `modify_cable_tray`, `modify_beam`, `modify_structural_column`

### UI
- `select_elements` — Get or set the current selection
- `hide_elements` — Hide or unhide elements in a view (alias: `unhide_elements`)
- `isolate_elements` — Temporarily isolate elements
- `clear_temporary_isolate` — Exit temporary hide/isolate mode
- `set_active_view` — Set the active view
- `zoom_to_fit` — Zoom the active view to fit all elements

### Export
- `export_view` — Export a view to an image (defaults to the active view)
- `batch_export` — Batch export views/sheets to PDF, DWG, or images
- `export_element_snapshot` — Export an image of specific elements (temporary isolate + section box, view state restored)

### Raw
- `execute_raw` — Execute raw C# code (requires `allow_raw_execution` in the config)

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

Compile your plugin DLL and place it in a `CliBridgePlugins/` subfolder next to `RevitCliBridge.dll`. The bridge will auto-discover and register it at startup (the DLL must be Authenticode-signed; unsigned DLLs are rejected unless `allow_unsigned_plugins` is enabled).

### Registering from your own add-in (recommended)

If your plugin is itself a Revit add-in, skip the `CliBridgePlugins` folder and register commands programmatically — using only the `RevitCliBridge.Abstractions` package, no reference to the bridge assembly:

```csharp
public class MyCompanyApp : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        BridgeRegistration.Register(new MyCommand());
        return Result.Succeeded;
    }
}
```

Deployment rules:

- Ship only your own DLL. **Never** deploy a copy of `RevitCliBridge.Abstractions.dll` or `Newtonsoft.Json.dll` with your plugin — the bridge folder provides both, and a stale local copy splits type identity so your commands silently fail to register. Exclude them with `ExcludeAssets="runtime"` on the package references.
- Your `.addin` manifest must load after the bridge's (`@RevitCliBridge.addin` sorts first alphabetically). Any conventionally named manifest is fine.

Verify after starting Revit:

```bash
revit-cli catalog
```

Your command should appear in the index. Full authoring guide: [RevitCliBridge.Abstractions README](RevitCliBridge.Abstractions/README.md).

## Architecture

See [docs/README.md](docs/README.md) for the full protocol specification.

## License

MIT — see [../LICENSE](../LICENSE)
