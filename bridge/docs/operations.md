# Operations

> Dependencies, file paths, and CI/CD. For config schema see
> [api-reference.md](api-reference.md#configuration--cli_bridge_settingjson).

## Dependency Graph

```
                        ┌─────────────────────────────────────┐
                        │  RevitCliBridge.Tests.dll (xUnit)   │
                        │  net48 — no Revit API dependency    │
                        └──────────────┬──────────────────────┘
                                       │ links pure-logic sources via <Compile Include>
                                       ▼
┌──────────────────────────────────────────────────────────────────────┐
│  RevitCliBridge.dll (IExternalApplication, multi-target R19–R22)     │
│  References: RevitCliBridge.Abstractions + Revit_API_x64 + System.Net│
└──────────────┬───────────────────────────────────────┬───────────────┘
               │ uses pure-logic helpers                │ depends on
               ▼                                        ▼
   ┌────────────────────────────────────┐  ┌──────────────────────────────────────┐
   │  Pure logic extracted from main    │  │  RevitCliBridge.Abstractions.dll     │
   │  • CommandNameResolver             │  │  netstandard2.0 — no Revit reference │
   │  • TaskStateMachine                │  │  References: Newtonsoft.Json only    │
   │  • ParameterBinder                 │  │  • IBridgeCommand, QueuedCommand     │
   │  • CommandResponse                 │  │  • CommandResponse, CommandSchema    │
   │  • PortAllocator (algo)            │  │  • ParameterBinder, ParamAttribute   │
   │  • CliBridgeConfigAuth             │  │  • CommandNameResolver               │
   └────────────────────────────────────┘  └──────────────────────────────────────┘
```

**Why an Abstractions project**: third-party plugin authors compile against
`RevitCliBridge.Abstractions.dll` alone (netstandard2.0, no Revit reference)
instead of the multi-targeted main assembly tied to a specific Revit version.
The pure-logic classes live there so tests can exercise them without loading
Revit or the executing assembly.

## File Paths

| File | Path | Purpose |
|------|------|---------|
| `cli_bridge_setting.json` | `.config/` | Bridge on/off, port, auto_port, timeout, api_key, plugin signing policy |
| Instance registry | `%LOCALAPPDATA%\revit-cli\instances\revit-{version}-{pid}.json` | Running instance discovery |
| Schema cache | `%LOCALAPPDATA%\revit-cli\` | Client-side command schema cache (TTL 30 min) |
| Schema ETag cache | `%LOCALAPPDATA%\revit-cli\` | ETag for conditional revalidation |
| Bridge log | `<bridge_dir>/logs/cli_bridge_<yyyy-MM-dd>.log` | Daily-rotated structured log (buffered) |

All `.config/` files copy to build output via
`<CopyToOutputDirectory>Always</CopyToOutputDirectory>`.

## Logging

`CliLogger` writes structured single-line entries with `key=value` fields so
they're grep-friendly (see [components.md](components.md#clilogger) for
format). The 4 KB `StreamWriter` buffer is flushed on shutdown via
`CliBridgeStateManager.Cleanup()`. Levels: `INFO`, `WARN`, `ERROR`.

Typical entries:

```
[2026-08-13 14:30:45.123] [INFO] request_received request_id=ab12cd34 method=POST path=/api/execute
[2026-08-13 14:30:45.500] [INFO] command_completed request_id=ab12cd34 command=create_wall duration_ms=42 status=success
[2026-08-13 14:31:02.000] [WARN] plugin_rejected_unsigned dll=evil.dll reason="unsigned and allow_unsigned_plugins is false"
```

## Plugin Security

Plugin DLLs in `CliBridgePlugins/` are gated by Authenticode signature checks
before being loaded (see [components.md](components.md#bridgepluginloader--signature-gating)):

| Config | Default | Recommendation |
|--------|---------|----------------|
| `allow_unsigned_plugins` | `false` | Leave `false` in production; set `true` only for local dev builds |
| `trusted_publishers` | `[]` (any valid sig) | Populate with `CN=...` entries to pin specific publishers in production |

Unsigned or untrusted plugins are rejected and logged with `plugin_rejected_*`
events — no assembly is loaded, so malicious code never executes.

## CI/CD Pipeline

**File**: `.github/workflows/release.yml` — builds both components and creates a
GitHub Release with distribution packages.

| Feature | Implementation |
|---------|---------------|
| Version injection | Git tag → Go binary via `-ldflags "-X main.Version=$v"`; C# assembly via `-p:Major/Minor/Patch` |
| Dependency caching | Go module cache (`setup-go@v5 cache: true`); NuGet (`actions/cache@v4`) |
| Artifact retention | 7 days |
| Checksums | SHA256 for all release artifacts |
| Permissions | Top-level `contents: read`; release job `contents: write` |
| Security default | `allow_raw_execution: false` and `allow_unsigned_plugins: false` in all generated configs |

← Back to [README.md](README.md).
