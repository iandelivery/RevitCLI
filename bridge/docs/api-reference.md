# API Reference

> HTTP endpoints, request/response models, and configuration. For the command
> catalog see [commands.md](commands.md); for server internals see
> [components.md](components.md).

## Endpoints

| Method | Path | Auth | Function |
|------|------|:----:|------|
| POST | `/api/execute` | ✓ | Execute a command (sync, async, or SSE) |
| GET  | `/api/task/{task_id}` | ✓ | Query task status & result |
| GET  | `/api/task` | ✓ | List all tasks (latest 50) |
| GET  | `/api/status` | ✓ | Server status |
| GET  | `/api/health` | — | Health check (public) |
| GET  | `/api/identity` | — | Instance identity: version, PID, port (public) |
| GET  | `/api/auth/status` | — | Whether API key auth is active (public) |
| GET  | `/api/commands` | ✓ | Full command schema (ETag support) |
| GET  | `/api/commands/{name}` | ✓ | Single command schema (ETag support) |
| GET  | `/api/catalog` | ✓ | Lightweight command index (~3–5 KB vs ~50–150 KB) |
| GET  | `/api/llms.txt` | ✓ | Revit API reference for AI agents |
| GET  | `/api/raw-mode` | ✓ | Whether `execute_raw` is currently enabled |
| POST | `/api/raw-mode` | ✓ | Enable/disable raw execution: `{"enabled": bool}` |

**Authentication**: When `api_key` is set in config, all `/api/*` endpoints
**except** `/api/health`, `/api/identity`, `/api/auth/status` require
`Authorization: Bearer <api_key>`. Comparison is constant-time. Empty/null key
disables auth (open access). Server listens on `localhost` only.

## Request Model — `RevitCommandInput`

```json
{
  "task_id": "uuid-optional",
  "command": "get_elements",
  "parameters": { "category": "OST_Walls" },
  "timeout_seconds": 120,
  "async": false
}
```

| Field | Type | Required | Default | Description |
|------|------|:----:|---------|------|
| `task_id` | `string` | No | auto-generated | Task identifier |
| `command` | `string` | Yes | — | Command name |
| `parameters` | `object` | No | `{}` | Command-specific params |
| `timeout_seconds` | `int` | No | `120` | Execution timeout (min 1) |
| `async` | `bool` | No | `false` | Return `task_id` immediately |

## Response Model — `CommandResponse`

**Success**:
```json
{ "task_id": "abc-123", "status": "success", "message": "Retrieved 5 elements.",
  "data": { "count": 5, "elements": [] } }
```

**Error**:
```json
{ "task_id": "abc-123", "status": "error", "message": "Command failed: ...",
  "error_details": "System.Exception: ..." }
```

## Task Status Response

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

## Configuration — `cli_bridge_setting.json`

**Path**: `.config/cli_bridge_setting.json` (copied to build output).

```json
{
  "schema_version": "1",
  "enabled": true,
  "port": 5000,
  "auto_port": true,
  "timeout_seconds": 180,
  "max_command_queue_size": 100,
  "allow_raw_execution": false,
  "api_key": ""
}
```

| Field | Type | Default | Description |
|------|------|---------|------|
| `schema_version` | `string?` | `"1"` | Schema version for migrations |
| `enabled` | `bool` | `true` | Auto-start on Revit launch |
| `port` | `int` | `5000` | Fallback TCP port (1–65535) |
| `auto_port` | `bool` | `true` | Allocate port by Revit version |
| `timeout_seconds` | `int` | `180` | Command timeout (min 1) |
| `max_command_queue_size` | `int` | `100` | Max pending commands (min 1) |
| `allow_raw_execution` | `bool` | `false` | Allow `execute_raw` |
| `api_key` | `string?` | `""` | API key; empty disables auth |

`CliBridgeConfigLoader.Config` uses double-check locking. Both Go and C# loaders
validate port/timeout/queue ranges on load.

## Example HTTP Calls

```bash
# Sync (default) — Revit 2022 on port 5041
curl -X POST http://localhost:5041/api/execute \
  -H "Content-Type: application/json" \
  -d '{"command":"get_elements","parameters":{"category":"OST_Walls"}}'

# Verify instance identity
curl http://localhost:5041/api/identity

# Async mode + poll
curl -X POST http://localhost:5041/api/execute \
  -H "Content-Type: application/json" \
  -d '{"command":"create_walls","async":true,"parameters":{}}'
curl http://localhost:5041/api/task/abc-123
```

→ Next: [commands.md](commands.md) for the command catalog.
