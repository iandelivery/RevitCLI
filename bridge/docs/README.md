# Revit CLI Bridge — Architecture Documentation

A framework enabling AI Agents to drive Autodesk Revit through a CLI or HTTP API.
Revit's API is single-threaded and bound to the Revit process, so a
**cross-process IPC + External Event** bridge architecture is used.

## Quick Navigation

> **Progressive disclosure**: start here for the 2-minute overview, then drill
> into a detail doc as needed.

| You want to… | Read this | Length |
|---|---|---|
| Understand the system at a high level | [overview.md](overview.md) | ~5 min |
| Dive into a specific component | [components.md](components.md) | ~10 min |
| Call the HTTP API / see request & response shapes | [api-reference.md](api-reference.md) | ~5 min |
| See the full command catalog or add a new command | [commands.md](commands.md) | ~5 min |
| Understand thread safety & concurrency | [threading.md](threading.md) | ~3 min |
| Integrate with Revit or use the Go CLI client | [integration.md](integration.md) | ~5 min |
| Run or extend the test suite | [testing.md](testing.md) | ~2 min |
| Operate: dependencies, config paths, CI/CD | [operations.md](operations.md) | ~3 min |

## At a Glance

```
AI Agent ──HTTP──► CliHttpServer ──enqueue──► TaskRegistry ──Raise──► Revit Main Thread
   ▲                   (localhost)               (TCS await)              │
   └──────────── JSON response ◄──────────────────────────── CommandRouter → Handler → Revit API
```

- **3 projects**: `RevitCliBridge` (Revit add-in, multi-target R19–R22), `RevitCliBridge.Abstractions` (netstandard2.0 SDK), `RevitCliBridge.Tests` (xUnit).
- **52 commands** across 10 category folders, auto-discovered via reflection.
- **61 unit tests** covering pure-logic components extracted for testability.
- **Auth**: optional API key (constant-time comparison); listens on `localhost` only.

## For AI Agents

- `GET /api/catalog` — lightweight command index (~3–5 KB).
- `GET /api/commands` — full command schema with ETag caching.
- `GET /api/llms.txt` — raw Revit API reference for fallback discovery.
- `GET /api/identity` — verify the target instance (version, PID, port).
- See [api-reference.md](api-reference.md) for all endpoints and [commands.md](commands.md) for the catalog.
