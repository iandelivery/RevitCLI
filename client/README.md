# RevitCliClient

A standalone command-line client written in Go that enables AI agents to drive
Autodesk Revit through the Revit CLI Bridge HTTP API.

This is a Go port of the C# `RevitCliClient`, following the same architectural
patterns: command registry, lazy schema discovery, SSE streaming with polling
fallback, and type-safe argument parsing.

**Pure Go · Zero Revit Dependency · Single Static Binary**

## How It Works

```
┌──────────────┐                          ┌──────────────────────────────┐
│              │   POST /api/execute      │                              │
│              │   Accept: text/event-    │     Revit CLI Bridge         │
│  CLI Client  │   stream                 │     (Revit Add-in)           │
│  (Go)        │ ───────────────────────► │                              │
│              │   SSE Event Stream       │  Receives HTTP requests,     │
│              │◄───────────────────────  │  executes on Revit main      │
│              │   event: accepted        │  thread, streams progress.   │
│              │   event: progress        │                              │
│              │   event: completed       │                              │
└──────────────┘                          └──────────────────────────────┘
```

## Requirements

- **Go 1.21+** — for building
- **Revit** — with the CLI Bridge add-in installed and running

## Build

The project ships with `build.ps1`, a PowerShell script that compiles the Go
binary, packages it as an Open Skill, and emits the companion `SKILL.md`.

### Prerequisites

- **Go 1.21+** — on `PATH` (install with `winget install GoLang.Go` if missing)
- **PowerShell 5+** (preinstalled on Windows)

### Build the skill (default)

```powershell
.\build.ps1
```

This runs `go vet`, then `go build -o revit-cli.exe ./cmd/revit-cli`, and
finally drops the executable into `skills\revit-cli\` together with a generated
`SKILL.md`.

Output:

```
skills\revit-cli\
  revit-cli.exe
  SKILL.md
```

### Build with a custom skill name

```powershell
.\build.ps1 -SkillName "revit-cli-bridge"
```

Produces `skills\revit-cli-bridge\` with the binary and `SKILL.md` renamed
accordingly. The default is `revit-cli`.

### Skip `go vet` (faster iteration)

```powershell
.\build.ps1 -SkipVet
```

### Build the binary directly (no skill packaging)

If you only need the raw executable without the skill wrapper:

```bash
go build -o revit-cli.exe ./cmd/revit-cli
```

## Quick Start

1. Start Revit and enable the CLI Bridge (via the Ribbon toggle button)
2. Verify the connection:

```bash
revit-cli.exe ping
```

3. Run commands:

```bash
revit-cli.exe levels
revit-cli.exe elements -c OST_Walls
revit-cli.exe create_wall --start-x 0 --start-y 0 --end-x 5000 --end-y 0 -l 3001
```

## Multi-Instance Usage

When multiple Revit instances are running, use instance discovery to target a specific one:

```bash
# List all running Revit instances
revit-cli.exe list

# Connect to a specific Revit version
revit-cli.exe --revit 2022 ping

# Connect to a specific instance by PID
revit-cli.exe --pid 5678 ping

# Explicit URL override (highest priority)
revit-cli.exe --url http://localhost:5041 ping
```

If only one Revit instance is running, it is auto-selected. If multiple instances are found and no selector is provided, the client displays the instance list and exits.

## Configuration

| Option | Default | Description |
|--------|---------|-------------|
| `--url <url>` | auto-discover | Revit CLI server address |
| `--pid <pid>` | — | Connect to a specific Revit instance by process ID |
| `--revit <version>` | — | Connect to a specific Revit version (e.g. 2022) |
| `--help`, `-h` | — | Show help |

Server-side config is read from `.config/cli_bridge_setting.json`. The client also reads instance registry files from `%AppData%\revit-cli\instances\` for auto-discovery.

## Built-in Commands

| Command | Description |
|---------|-------------|
| `ping` | Test connection to Revit |
| `list` | List running Revit instances |
| `llms [--save <path>]` | Show Revit API reference (llms.txt) |
| `status` | Show bridge server status |
| `health` | Health check |
| `task [-ti <id>]` | Query task status (list all if no ID) |
| `commands [--refresh]` | List all commands (with local cache) |
| `schema <name>` | Show command parameter details |
| `raw -j <json>` | Send raw JSON command |
| `execute_raw --code <code>` | Execute C# or Python code on the bridge |
| `configure <setup\|teardown\|check\|port>` | Manage bridge installation and configuration |

### configure Sub-commands

| Sub-command | Description |
|-------------|-------------|
| `configure setup` | Install bridge add-in for all detected Revit versions |
| `configure teardown` | Remove bridge add-in from all Revit versions |
| `configure check` | Verify installation health and connectivity |
| `configure port` | Show port assignments for all Revit versions |

## API Reference (llms.txt)

The `llms` command fetches a text reference from `GET /api/llms.txt` that describes the raw Revit API elements, parameters, and classes available in the running instance. Use it when the built-in commands don't cover a specific need:

```bash
# View the reference
revit-cli.exe llms

# Save it to a file
revit-cli.exe llms --save revit-api-ref.txt

# Then use execute_raw for uncovered operations
revit-cli.exe execute_raw --lang csharp --code "return doc.Title;"
```

## Architecture

The CLI client mirrors the C# `RevitCliClient` architecture:

- **Built-in commands** (11) — always available without server contact
- **Instance discovery** — reads registry files from `%AppData%\revit-cli\instances\`
- **Lazy schema discovery** — schema fetched only when a non-built-in command is invoked
- **Local cache** — schema cached with 30-min TTL, stale fallback on network error
- **SSE streaming** — real-time progress via Server-Sent Events with polling fallback
- **Embedded config** — default configuration embedded in the binary at compile time

See `ARCHITECTURE.md` in the C# `CliBridge` directory for the full protocol spec.

## License

MIT
