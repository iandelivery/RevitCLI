# CLI Reference

Complete operational reference for `revit-cli.exe`. All details that are not trigger-critical live here so SKILL.md stays lean.

## Global Options and Instance Targeting

```
revit-cli.exe [--url <url> | --pid <pid> | --revit <version>] <command> [arguments]
```

- `--url <url>` — talk to an explicit bridge URL
- `--pid <pid>` — target a specific Revit process (e.g. `--pid 5678`)
- `--revit <version>` — target a Revit version (e.g. `--revit 2022`)

Instance registry files are stored under `%AppData%\revit-cli\instances\`, one JSON per running Revit (`revit-<year>-<pid>.json`).

## Built-in Commands

| Command | Description |
|---------|-------------|
| `ping` | Test connection to Revit |
| `list` | List running Revit instances. `--json` omits the API key |
| `llms [--save <path>]` | Show Revit API reference (llms.txt), optionally save to file |
| `status` | Show bridge server status |
| `health` | Health check |
| `task [-ti <id>]` | Query task status (list all if no ID) |
| `commands [--refresh]` | List all commands (with local cache) |
| `schema <name>` | Show command parameter details |
| `raw -j <json>` | Send raw JSON command |
| `execute_raw --code <code>` | Execute C# code on the bridge |
| `raw-mode [--enable\|--disable]` | Query or toggle raw execution mode |
| `configure <setup\|teardown\|check\|port>` | Manage bridge installation and configuration |
| `skill <install\|show\|update\|uninstall>` | Install and manage the AI agent skill (SKILL.md); see the Skill Installation section in SKILL.md |

Beyond these, the bridge exposes **70+ high-level commands** (walls/doors/windows, cable trays, ducts, pipes, element queries, parameter modification, view export, document management). The full set is only discoverable at runtime via `commands`.

## Units

The units contract: an agent never sees a Revit internal unit. Every dimensional argument and response value of a high-level command is expressed in the request unit of its kind, and every response carries a `units` echo naming the units used. Canonical defaults: `length=mm`, `area=m2`, `volume=m3`, `angle=deg`; allowed per kind: length `mm cm m ft in`, area `m2 mm2 ft2`, volume `m3 mm3 ft3 l`, angle `deg rad`.

Choose units per invocation with the `--units` global flag: repeatable `kind=unit` pairs (`--units length=ft --units angle=rad`) or `--units project` to follow the active document's decimal display units (query them with `get_units`). Without the flag, canonical units apply. Outputs round to 3 decimals; inputs are never rounded. Command schemas declare `unit_kind` per dimensional parameter, and `get_parameters` reports each parameter's `unit_kind` (`raw` when the contract does not convert that spec yet).

```bash
revit-cli.exe get_element_boxes --element-ids 12345                      # mm
revit-cli.exe --units length=ft get_element_boxes --element-ids 12345    # ft
revit-cli.exe --units project get_rooms                                  # document units
revit-cli.exe get_units                                                  # project/canonical/allowed matrices
```

This conversion applies ONLY to high-level commands; raw scripts via `execute_raw` deal with the Revit API directly in internal feet. In raw scripts, convert with `Autodesk.Revit.DB.UnitUtils.ConvertToInternalUnits` / `ConvertFromInternalUnits` — the unit-argument type changed in Revit 2021: `DisplayUnitType.DUT_MILLIMETERS` (≤2020, obsolete since 2021) vs `UnitTypeId.Millimeters` (≥2021) (see constraint 7 in [csharp-scripting.md](csharp-scripting.md)).

## Dynamic Command Discovery

Non-built-in commands are auto-discovered from the bridge schema endpoint (`GET /api/commands`). The schema is cached locally (30-min TTL) under `%AppData%\revit-cli\` with ETag-based conditional requests and stale-cache fallback for offline resilience. Use `commands --refresh` to force a re-fetch.

Every dynamic command accepts the output flags `--fields`, `--fmt`, and `--jq`. The **bridge** executes them; the client forwards arguments untouched, so there is no client-side filtering step.

Commands with `supports_dry_run` also accept `--dry-run` after the command name. `--dry-run` before the command name is a usage error: the client prints `Error: --dry-run must come after the command name`, exits 1, and does not run the command.

## API Reference (llms.txt) and Raw Execution

The bridge exposes `GET /api/llms.txt`, a text index of raw Revit API elements, parameters, and classes. If and only if discovered high-level commands do not cover a need:

1. Run `revit-cli.exe llms` to see the full reference
2. Find the relevant element class, parameter, or filter
3. Execute it directly:
   ```
   revit-cli.exe execute_raw --lang csharp --code "return doc.Title;"
   ```
4. To run a generated script file (PowerShell):
   ```powershell
   revit-cli.exe raw-mode --enable
   revit-cli.exe execute_raw --lang csharp --code (Get-Content rebuild.csx -Raw)
   ```

Raw execution is enabled by default; the raw-mode toggle is **in-memory only**: a Revit restart resets it to the `allow_raw_execution` value in `cli_bridge_setting.json` (true for new installs). All raw scripts must satisfy the hard constraints in [csharp-scripting.md](csharp-scripting.md).

## SSE Real-Time Events

All command executions use Server-Sent Events for live progress updates:

- `event: accepted` — task queued
- `event: progress` — real-time progress percentage
- `event: completed` — final result
- `event: failed` — error result

Falls back to HTTP polling (`GET /api/task/{id}`, exposed as `revit-cli.exe task -ti <id>`) if SSE is unavailable.

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

| Key | Default | Meaning |
|-----|---------|---------|
| `port` | 5000 | Fallback port |
| `auto_port` | true | Dynamically allocate port based on Revit version |
| `timeout_seconds` | 180 | Command timeout |
| `max_command_queue_size` | 100 | Command queue limit |
| `enabled` | true | AI Mode auto-start on Revit launch |
| `allow_raw_execution` | true | Persistent default for raw mode |

Port allocation scheme (when `auto_port` is true):

- Revit 2019 → 5011-5019
- Revit 2020 → 5021-5029
- Revit 2021 → 5031-5039
- Revit 2022 → 5041-5049
- Revit 2023 → 5051-5059
- Revit 2024 → 5061-5069

Port 5000 is reserved as a legacy fallback; if a version's range is exhausted, the bridge falls back to the configured port or an ephemeral OS-assigned port.

## Architecture Notes

This skill ships a Go port of the C# `RevitCliClient`, following the same patterns:

- Command registry with case-insensitive lookup
- Lazy schema discovery (schema fetched only when needed)
- SSE transport with polling fallback
- Type-safe argument parsing (ArgHelper)
- 30-min TTL local schema cache with ETag support

See `bridge/docs/README.md` in the upstream repository for the full protocol specification: https://github.com/iandelivery/RevitCLI
