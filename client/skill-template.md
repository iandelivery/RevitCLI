---
name: "{{SkillName}}"
description: "Go CLI client that drives Autodesk Revit via the CLI Bridge HTTP API. Invoke when user needs to execute Revit commands from the command line or when AI agents need to control Revit programmatically."
---

# Revit CLI Client

A standalone command-line client written in Go that enables AI agents and users to drive Autodesk Revit through the Revit CLI Bridge HTTP API. Communicates with the bridge server running inside Revit via HTTP, converting CLI commands into Revit API operations with real-time SSE progress streaming.

## When to Invoke

Invoke this skill when:
- The user wants to control Revit from the command line
- An AI agent needs to execute Revit API operations (create, query, modify, transform elements)
- The user asks to automate Revit tasks via CLI
- Real-time progress feedback for long-running Revit operations is needed

## Prerequisites

- **Revit** must be running with the CLI Bridge add-in enabled (Ribbon toggle button)
- The bridge server auto-selects a port based on Revit version (e.g. Revit 2022 → port 5041+)
- Verify connection with: `revit-cli.exe ping`
- List running instances with: `revit-cli.exe list`

## Usage

``````bash
# Global options
revit-cli.exe [--url <url> | --pid <pid> | --revit <version>] <command> [arguments]

# List running Revit instances
revit-cli.exe list

# Connect to a specific Revit version
revit-cli.exe --revit 2022 ping

# Connect to a specific instance by PID
revit-cli.exe --pid 5678 ping

# Test connection
revit-cli.exe ping

# List all available commands (auto-discovered from bridge)
revit-cli.exe commands

# Query elements
revit-cli.exe elements -c OST_Walls
revit-cli.exe levels

# Create elements
revit-cli.exe create_wall --start-x 0 --start-y 0 --end-x 5000 --end-y 0 -l 3001

# Send raw JSON command
revit-cli.exe raw -j '{"command":"ping"}'
``````

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
| `configure <setup|teardown|check|port>` | Manage bridge installation and configuration |

## Dynamic Command Discovery

Non-built-in commands are auto-discovered from the bridge server's schema endpoint (`GET /api/commands`). The schema is cached locally (30-min TTL) under `%AppData%/revit-cli/` with ETag-based conditional requests and stale-cache fallback for offline resilience.

## API Reference (llms.txt)

The bridge exposes a `GET /api/llms.txt` endpoint that returns a text index of raw Revit API elements, parameters, and classes. If your high-level commands don't cover a specific need:

1. Run `revit-cli.exe llms` to see the full reference
2. Find the relevant element class, parameter, or filter
3. Use `execute_raw` to invoke it directly:
   ```
   revit-cli.exe execute_raw --lang csharp --code "return doc.Title;"
   ```

## SSE Real-Time Events

All command executions use Server-Sent Events for live progress updates:
- `event: accepted` — task queued
- `event: progress` — real-time progress percentage
- `event: completed` — final result
- `event: failed` — error result

Falls back to HTTP polling (`GET /api/task/{id}`) if SSE is unavailable.

## Argument Shortcuts

| Short | Long Form | Description |
|-------|-----------|-------------|
| `-i` | `--id` | Element ID |
| `-e` | `--element-id` | Element ID |
| `-l` | `--level-id` | Level ID |
| `-n` | `--name` | Name |
| `-v` | `--value` | Value |
| `-c` | `--category` | Category |
| `-j` | `--json` | JSON data |
| `-fl` | `--file` | File path |
| `-vi` | `--view-id` | View ID |
| `-ti` | `--task-id` | Task ID |

## Configuration

Server-side config is read from `.config/cli_bridge_setting.json`:
- `port` (default: 5000, used as fallback)
- `auto_port` (default: true) — dynamically allocate port based on Revit version
- `timeout_seconds` (default: 180)
- `max_command_queue_size` (default: 100)

Port allocation scheme (when `auto_port` is true):
- Revit 2019 → 5011-5019
- Revit 2020 → 5021-5029
- Revit 2021 → 5031-5039
- Revit 2022 → 5041-5049

Instance registry files are stored in `%AppData%\revit-cli\instances\`.

Override the bridge URL with `--url <url>`, `--pid <pid>`, or `--revit <version>`.

## Architecture

This skill is a Go port of the C# `RevitCliClient`, following the same patterns:
- Command registry with case-insensitive lookup
- Lazy schema discovery (schema fetched only when needed)
- SSE transport with polling fallback
- Type-safe argument parsing (ArgHelper)
- 30-min TTL local schema cache with ETag support

See `CliBridge/ARCHITECTURE.md` in the repository for the full protocol specification.

