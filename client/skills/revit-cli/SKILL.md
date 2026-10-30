---
name: revit-cli
description: "Command-line client (revit-cli.exe) that drives Autodesk Revit through the RevitCLI Bridge HTTP API. Use when the user wants to control Revit from the command line, when an agent needs to create, query, modify, or transform Revit elements, automate Revit tasks, execute raw C# or Python scripts inside Revit via execute_raw, or stream real-time progress (SSE) for long-running Revit operations. Owns bridge connection and instance discovery, command schema discovery, millimeter-based command units, raw-mode toggling, C# script hard constraints, execution discipline, and CLI troubleshooting."
metadata:
  author: "CHEN YIN"
  upstream: "https://github.com/iandelivery/RevitCLI"
---

# Revit CLI Client

A standalone command-line client written in Go that enables AI agents and users to drive Autodesk Revit through the Revit CLI Bridge HTTP API. It communicates with the bridge server running inside Revit via HTTP, converts CLI commands into Revit API operations, and streams real-time SSE progress.

The `revit-cli.exe` binary is located in the same directory as this markdown file.

This skill is the single source of truth for *how to talk to Revit through RevitCLI*: connection, command discovery, raw script execution, script constraints, and fault handling.

## Skill Installation

This skill file travels with `revit-cli.exe`. To register it with an AI coding agent (Claude Code, Cursor, Codex, Trae, Trae CN, CodeBuddy, WorkBuddy, Kimi), run:

```bash
# 1. Discover candidate agent directories (non-destructive)
revit-cli.exe skill install --list

# 2. Install non-interactively into the chosen agents
revit-cli.exe skill install --agent claude,codebuddy
```

Installation copies this file (plus `references/`) into `<agent>/skills/revit-cli/`, rewriting the `revit-cli.exe` reference above to its absolute path on this machine.

| Flag | Description |
|------|-------------|
| `--agent <ids>` | Comma-separated agent ids: `claude`, `cursor`, `codex`, `trae`, `trae-cn`, `codebuddy`, `workbuddy`, `kimi-work`, `kimi-code` |
| `--all` | Install into every detected agent directory |
| `--target <dir>` | Custom skills root; installs into `<dir>/revit-cli/` |
| `--global` | User-level (`~`) agent directories instead of project (cwd) |
| `--force` | Overwrite an existing installation |
| `--list` | Only list candidate targets; do not install |
| `--json` | Machine-readable output |

Related sub-commands: `skill show` (installation status), `skill update` (re-sync all installs after upgrading `revit-cli.exe`), `skill uninstall` (remove installs; only touches directories containing a revit-cli install manifest).

## When to Invoke

- The user wants to control Revit from the command line
- An agent needs to execute Revit API operations (create, query, modify, transform elements)
- The user asks to automate Revit tasks via CLI
- Raw C# or Python code must run inside Revit (`execute_raw`)
- Real-time progress feedback for long-running Revit operations is needed

## Prerequisites

- **Revit** must be running with the CLI Bridge add-in enabled (Ribbon toggle button; AI Mode auto-starts by default)
- The bridge server auto-selects a port based on Revit version (e.g. Revit 2022 → port 5041+)
- Verify connection with `revit-cli.exe ping`; list running instances with `revit-cli.exe list`
- Target against a specific instance with `--revit <version>`, `--pid <pid>`, or `--url <url>`

## Execution Strategy

Always follow this two-tier strategy to optimize performance and maintain stability:

1. **Dynamic Discovery** — run `revit-cli.exe commands` first to check available high-level commands, or inspect a command's parameters via `revit-cli.exe schema <name>`. If a high-level command exists for the task, **always use it**.
2. **Raw Code Fallback** — only if the discovered high-level commands **cannot** solve the task, search the API reference (`revit-cli.exe llms`) and execute C# or Python scripts directly with `revit-cli.exe execute_raw`. Toggle raw mode with `revit-cli.exe raw_mode --enable` / `revit-cli.exe raw_mode --disable` (raw execution is disabled by default for security and resets on Revit restart).

Raw scripts compiled by the bridge are subject to **hard constraints** (ASCII-only payload, LF endings, no `using` directives, flat parallel arrays, single transaction, mm↔feet units). Violating any of them fails the run. Read [references/csharp-scripting.md](references/csharp-scripting.md) before writing any raw script.

## Quick Usage

```bash
revit-cli.exe ping                                  # test connection
revit-cli.exe list                                  # running Revit instances
revit-cli.exe --revit 2022 ping                     # target a specific version
revit-cli.exe commands                              # discover high-level commands
revit-cli.exe schema create_wall                    # inspect one command
revit-cli.exe elements -c OST_Walls                 # query elements by category
revit-cli.exe create_wall --start-x 0 --start-y 0 --end-x 5000 --end-y 0 -l 3001   # mm
revit-cli.exe execute_raw --lang csharp --code "return doc.Title;"
revit-cli.exe raw -j '{"command":"ping"}'           # raw JSON command
```

**Units**: the client uses **millimeters** — every distance, coordinate, and size argument of high-level commands (and every such value returned) is in mm; the bridge converts to/from Revit internal feet (`1 ft = 304.8 mm`). Raw C#/Python scripts bypass this conversion and talk to the Revit API directly in internal feet — see the units constraint in [references/csharp-scripting.md](references/csharp-scripting.md).

Full command table, argument shortcuts, dynamic discovery, SSE events, and configuration: [references/cli-reference.md](references/cli-reference.md).

## Safety Rules

> [!CAUTION]
> A document opened as read-only still accepts modifications through the bridge. Always verify the target document before writing.

Always ask for user confirmation before:

- Open, close, save, save as file
- Batch operations
- Interacting with a central Revit file (sync with central, load latest worksets)
- Modifying links (unload link, reload link with a different file)

Execution discipline (probe first, single-element experiment before batches, immediate re-validation, save confirmation) is defined in [references/csharp-scripting.md](references/csharp-scripting.md); apply it to every write session.

## References

| File | Contents |
|------|----------|
| [references/cli-reference.md](references/cli-reference.md) | Built-in commands, argument shortcuts, dynamic command discovery and schema cache, llms.txt API reference workflow, SSE events, bridge configuration and port allocation, architecture notes |
| [references/csharp-scripting.md](references/csharp-scripting.md) | Bridge compile template, transaction failure preprocessor, the 12 hard constraints for raw C# scripts, code-generator helpers, execution discipline |
| [references/troubleshooting.md](references/troubleshooting.md) | Symptom → cause → fix quick reference (connection failures, 401, Invalid JSON, compile cascades, garbled text, deletion deadlocks, timeouts) |
