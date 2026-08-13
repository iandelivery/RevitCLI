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
| `cli_bridge_setting.json` | `.config/` | Bridge on/off, port, auto_port, timeout, api_key |
| Instance registry | `%LOCALAPPDATA%\revit-cli\instances\revit-{version}-{pid}.json` | Running instance discovery |
| Schema cache | `%LOCALAPPDATA%\revit-cli\` | Client-side command schema cache (TTL 30 min) |
| Schema ETag cache | `%LOCALAPPDATA%\revit-cli\` | ETag for conditional revalidation |

All `.config/` files copy to build output via
`<CopyToOutputDirectory>Always</CopyToOutputDirectory>`.

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
| Security default | `allow_raw_execution: false` in all generated configs |

← Back to [README.md](README.md).
