# Revit CLI Bridge

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Go Version](https://img.shields.io/badge/go-%3E%3D1.23-blue.svg)](https://go.dev/)

A command-line interface toolkit that enables AI agents and automation scripts to drive Autodesk Revit through an HTTP API.

## Architecture

```
┌──────────────┐                          ┌──────────────────────────────┐
│              │   POST /api/execute      │                              │
│              │   Accept: text/event-    │     RevitCliBridge           │
│  CLI Client  │   stream                 │     (Revit Add-in)           │
│  (Go)        │ ───────────────────────► │                              │
│              │   SSE Event Stream       │  Receives HTTP requests,     │
│              │◄───────────────────────  │  executes on Revit main      │
│              │   event: accepted        │  thread, streams progress.   │
│              │   event: progress        │                              │
│              │   event: completed       │                              │
└──────────────┘                          └──────────────────────────────┘
```

### Multi-Instance Architecture

Multiple Revit instances (different versions or same version) can run simultaneously. Each bridge auto-selects a port from its version's range and registers itself in `%AppData%\revit-cli\instances\`.

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ Revit 2020   │     │ Revit 2022   │     │ Revit 2022   │
│ :5021        │     │ :5041        │     │ :5042        │
│ PID 1234     │     │ PID 5678     │     │ PID 9012     │
└──────────────┘     └──────────────┘     └──────────────┘
       │                    │                     │
       └────────────────────┴─────────────────────┘
                            │
                   %AppData%\revit-cli\instances\
                     ├── revit-2020-1234.json
                     ├── revit-2022-5678.json
                     └── revit-2022-9012.json
                            │
                   ┌────────┴────────┐
                   │  CLI Client     │
                   │  revit-cli list │
                   │  --revit 2022   │
                   │  --pid 5678     │
                   └─────────────────┘
```

## Projects

| Directory | Language | Description |
|-----------|----------|-------------|
| [`bridge/`](bridge/) | C# (.NET) | Revit add-in that runs an HTTP server inside Revit, exposing Revit API operations as CLI commands |
| [`client/`](client/) | Go | Standalone CLI client that sends commands to the bridge via HTTP/SSE |

## Quick Start

### 1. Install the Bridge (Revit Add-in)

**Option A: Automated setup (recommended)**

```bash
revit-cli.exe configure setup
```

This scans for Revit installations, copies add-in files, and generates version-specific configs with correct port assignments.

**Option B: Manual installation**

1. Build `bridge/RevitCliBridge.sln` in Visual Studio (targets Revit 2019-2022)
2. Copy the output DLLs and `RevitCliBridge.addin` to your Revit addins folder:
   - `%APPDATA%\Autodesk\Revit\Addins\<version>\RevitCliBridge\`
3. Start Revit and click the "AI Mode Toggle" button in the "Revit CLI Bridge" ribbon tab

### 2. Build the Go Client

```bash
cd client
go build -o revit-cli.exe ./cmd/revit-cli

# Or cross-compile for all platforms:
./build.sh --all
```

### 3. Run Commands

```bash
# List running Revit instances
revit-cli.exe list

# Test connection (auto-discovers if single instance)
revit-cli.exe ping

# Connect to a specific Revit version
revit-cli.exe --revit 2022 ping

# List available commands
revit-cli.exe commands

# Query elements
revit-cli.exe elements -c OST_Walls

# Create a wall
revit-cli.exe create_wall --start-x 0 --start-y 0 --end-x 5000 --end-y 0 -l 3001

# View the Revit API reference for uncovered operations
revit-cli.exe llms
```

## Features

- **60+ built-in commands** — create walls/doors/windows, query elements, modify parameters, export views, manage documents
- **SSE real-time streaming** — live progress updates for long-running operations
- **Schema discovery** — the client auto-discovers available commands from the bridge
- **Multi-instance support** — run multiple Revit versions simultaneously with auto port allocation
- **Instance discovery** — `--revit`, `--pid`, and `list` commands for targeting specific instances
- **llms.txt API reference** — AI agents can discover raw Revit API elements and use `execute_raw` as fallback
- **Plugin system** — third-party command handlers via `IBridgeCommand` interface
- **Multi-version support** — Revit 2019, 2020, 2021, 2022
- **Dry-run mode** — preview operations without modifying the document
- **Automated setup** — `configure setup` installs the bridge across all detected Revit versions

## Port Allocation

When `auto_port` is enabled (default), each Revit version gets a dedicated port range:

| Revit Version | Base Port | Range |
|---------------|-----------|-------|
| 2019 | 5011 | 5011-5019 |
| 2020 | 5021 | 5021-5029 |
| 2021 | 5031 | 5031-5039 |
| 2022 | 5041 | 5041-5049 |

Port 5000 is reserved as a legacy fallback. If all ports in a version's range are occupied, the bridge falls back to the configured `port` value or an ephemeral OS-assigned port.

## Documentation

| Document | Description |
|----------|-------------|
| [Bridge README](bridge/README.md) | Build, install, configure, and use the Revit add-in |
| [Bridge Architecture](bridge/ARCHITECTURE.md) | Full protocol specification and internal architecture |
| [Client README](client/README.md) | Build and use the Go CLI client |

## License

MIT — see [LICENSE](LICENSE)
