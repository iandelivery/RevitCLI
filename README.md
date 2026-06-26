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

Multiple Revit instances (different versions or same version) can run simultaneously. Each bridge auto-selects a port from its version's range and registers itself in the revit-cli data directory.

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ Revit 2020   │     │ Revit 2022   │     │ Revit 2022   │
│ :5021        │     │ :5041        │     │ :5042        │
│ PID 1234     │     │ PID 5678     │     │ PID 9012     │
└──────────────┘     └──────────────┘     └──────────────┘
       │                    │                     │
       └────────────────────┴─────────────────────┘
                            │
                   %LOCALAPPDATA%\revit-cli\instances\
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

## Local Storage Locations

Revit CLI stores runtime state on disk using a **cascading directory strategy**. Both the instance registry and the schema cache share the same base directory, resolved by this priority order:

| Priority | Base Directory | When it is used |
|----------|---------------|-----------------|
| 1 | `%REVIT_CLI_DATA_DIR%` | Explicit override (headless, CI, custom mount points) |
| 2 | `%LOCALAPPDATA%\revit-cli\` | Best Windows practice for local app data |
| 3 | `%USERPROFILE%\.revit-cli\` | Standard CLI dot-folder fallback (bypasses most group policies) |
| 4 | `<exe directory>\.revit-cli\` | Portable mode — no user profile required |

The bridge and CLI both try each path in order, create it on first use, and fall through to the next if creation fails. To verify which path is in use, run `revit-cli.exe configure check`.

### Instance Registry (written by the bridge)

Each running bridge writes a JSON descriptor to `<data-dir>/instances/` so the CLI can discover it:

```
%LOCALAPPDATA%\revit-cli\instances\
  ├── revit-2020-1234.json   # one file per running Revit
  ├── revit-2022-5678.json
  └── revit-2022-9012.json
```

Use `revit-cli.exe list` to see the current contents.

### Schema Cache (written by the CLI client)

The CLI caches the command schema (fetched from `GET /api/commands`) locally with a 30-minute TTL and ETag-based conditional revalidation. Falls back to the stale cache when the bridge is unreachable.

```
%LOCALAPPDATA%\revit-cli\cache\
  ├── localhost_5041_schema.json
  └── localhost_5041_schema.etag
```

## Projects

| Directory | Language | Description |
|-----------|----------|-------------|
| [`bridge/`](bridge/) | C# (.NET) | Revit add-in that runs an HTTP server inside Revit, exposing Revit API operations as CLI commands |
| [`client/`](client/) | Go | Standalone CLI client that sends commands to the bridge via HTTP/SSE |

## Quick Start

### Option 1 — Download a pre-built release

1. Download the latest `revit-cli-<version>.zip` from the [Releases](../../releases) page.
2. Extract it anywhere (e.g. `C:\revit-cli\`). The zip contains the `revit-cli.exe` client, the `SKILL.md` for AI agents, and a `bridge/` folder with all four supported Revit versions.
3. Install the bridge for every Revit on this machine:
   ```powershell
   cd C:\revit-cli
   .\revit-cli.exe configure setup
   ```
   This scans the Windows registry, then copies the matching bridge files to each Revit version's addins folder with the correct port assignment.
4. Start Revit. The bridge starts in **AI Mode** automatically (controlled by `enabled` in `.config/cli_bridge_setting.json`, which is `true` by default). The **AI Mode Toggle** button in the **Revit CLI Bridge** ribbon tab is only needed if you ever want to disable it.
5. Test the connection:
   ```powershell
   .\revit-cli.exe ping
   ```

### Option 2 — Build from source

A unified build script at the repo root builds both the C# bridge and the Go client in one step and packages them into release-ready zips, exactly like the GitHub release workflow.

**Prerequisites**

| Tool | Version | Purpose |
|------|---------|---------|
| Windows | 10+ | Bridge targets `x64`; Go build uses `GOOS=windows` |
| PowerShell | 5.1+ (PowerShell 7+ recommended) | Runs `build.ps1` |
| .NET SDK | 6.0.x and 7.0.x | Builds the C# bridge for all Revit versions |
| Go | 1.21 or newer (matches `go.mod`) | Builds `revit-cli.exe` |
| Git | any recent version | Provides version tags via `git describe` |

Verify everything is on `PATH` before building:

```powershell
dotnet --version     # should print 6.x or 7.x
go version           # should print go1.21 or newer
git --version
```

**Step 1 — Run the unified build**

From the repository root:

```powershell
cd C:\path\to\revit-cli-opensource
.\build.ps1
```

The script performs three phases, mirroring the CI release pipeline:

1. **Build the bridge** for every supported Revit version (2019, 2020, 2021, 2022). Output lands in `bridge/dist/Revit20XX/`, including a per-version `.config/cli_bridge_setting.json` with the correct port (5011, 5021, 5031, 5041).
2. **Build the Go client** (`go vet` + `go build`) and inject the version string via `-ldflags "-X main.Version=…"`.
3. **Package** the artifacts into three kinds of zip in the repo-root `dist/` folder:
   - `revit-cli-<version>.zip` — full bundle (client + SKILL.md + all bridges)
   - `revit-cli-client-<version>.zip` — client + SKILL.md only
   - `RevitCliBridge-Revit<year>-<version>.zip` — per-version bridge

**Step 2 — Verify the build succeeded**

A successful run prints each phase in cyan/yellow and finishes with a green `Build Complete` summary. The exit code is `0` when every step succeeded.

```powershell
# Confirm the exit code was zero
echo $LASTEXITCODE   # should print 0

# Confirm the zip artifacts exist
Get-ChildItem .\dist\*.zip
```

Expected output (example for tag `v1.0.0`):

```
revit-cli-1.0.0.zip
revit-cli-client-1.0.0.zip
RevitCliBridge-Revit2019-1.0.0.zip
RevitCliBridge-Revit2020-1.0.0.zip
RevitCliBridge-Revit2021-1.0.0.zip
RevitCliBridge-Revit2022-1.0.0.zip
```

**Common options**

| Flag | Effect |
|------|--------|
| `-RevitVersions "2021,2022"` | Build the bridge for a subset of Revit versions |
| `-SkipBridge` | Reuse existing `bridge/dist/` output and only build/package the client |
| `-SkipClient` | Reuse an existing `revit-cli.exe` and only build/package the bridge |
| `-SkipPackage` | Run the builds but don't create zips (faster iteration) |
| `-SkipVet` | Skip `go vet` for a quicker client build |

Examples:

```powershell
# Quick iteration on the Go client only
.\build.ps1 -SkipBridge

# Build and package only the Revit 2022 bridge
.\build.ps1 -RevitVersions "2022" -SkipVet
```

**Building components independently**

The root `build.ps1` is the recommended one-stop script, but you can still build either component on its own when you only need a quick local iteration cycle. Each sub-project ships with its own build script that performs the same steps the root script would for that component.

```powershell
# Build only the bridge (all Revit versions, no packaging)
cd bridge
.\build.ps1

# Build only the Go client
cd ..\client
.\build.ps1
```

Both scripts accept the same `-SkipVet` / `-SkipVet` style flags and write to the same output directories the root script uses (`bridge/dist/Revit20XX/` and `client/revit-cli.exe`), so artifacts produced by either path are interchangeable. Use the root `build.ps1` for releases; use the per-project scripts for tight inner-loop development.

**Troubleshooting**

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `[ERROR] Go is not installed or not on PATH.` | `go` not on `PATH` in this PowerShell session | Install Go from <https://go.dev/dl/> or run `winget install GoLang.Go`, then open a new terminal |
| `[ERROR] dotnet CLI not found.` | .NET SDK missing or not on `PATH` | Install .NET 6.0 and 7.0 SDKs (both are required for the Revit version matrix) |
| `dotnet build` fails with `MSB4019: The imported project … was not found.` | Missing `RevitAPI.dll` / `RevitAPIUI.dll` references | The csproj expects the Revit SDK assemblies to be on `PATH` or in the standard `C:\Program Files\Autodesk\Revit <year>\` location |
| `git describe` returns `dev` | No git tag matches the current commit | Tag the commit (e.g. `git tag v1.0.0`) or set `-Version` explicitly; the resulting zips will use `dev` as the version string |
| Bridge builds but `revit-cli.exe --version` smoke test fails | The exe was built but the new binary path is stale | Delete `client/revit-cli.exe` and re-run `.\build.ps1` |
| `Compress-Archive` produces empty zips on PowerShell 5.1 | A long-standing PowerShell 5.1 quirk with `-Path` globs | Use PowerShell 7+ (`pwsh`) or `tar -a -cf <name>.zip -C staging .` instead |
| `Access to the path 'bridge/dist' is denied.` | A previous Revit session or build still has files locked | Close any open File Explorer windows pointing at `bridge/dist/` and re-run |

**Installing the bridge manually** if you did not pass an installer step:

1. Copy the DLLs and `RevitCliBridge.addin` from `bridge/dist/Revit<year>/` to:
   - `%APPDATA%\Autodesk\Revit\Addins\<version>\RevitCliBridge\`
2. Start Revit. The bridge starts in **AI Mode** automatically (controlled by `enabled` in `.config/cli_bridge_setting.json`, which is `true` by default). The **AI Mode Toggle** button in the **Revit CLI Bridge** ribbon tab is only needed if you ever want to disable it.

### Run Commands

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

### Raw Execution Mode

The `execute_raw` command (which runs arbitrary C# or Python code against the Revit API) is **disabled by default** for security. The `raw-mode` command lets you toggle this at runtime without editing config files or restarting Revit:

```bash
# Check current state
revit-cli.exe raw-mode

# Enable raw execution
revit-cli.exe raw-mode --enable

# Disable raw execution
revit-cli.exe raw-mode --disable
```

This calls `GET /api/raw-mode` to query and `POST /api/raw-mode` to toggle. The setting is in-memory only — restarting Revit resets it to the value in `cli_bridge_setting.json` (`allow_raw_execution`). For persistent changes, edit that file.

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
