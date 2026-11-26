# RevitCliBridge.Abstractions

Plugin SDK for [RevitCliBridge](https://github.com/iandelivery/RevitCLI) — implement third-party bridge commands without referencing the bridge assembly or locking to a specific Revit version.

## What this package gives you

| Type | Purpose |
|------|---------|
| `IBridgeCommand` | Implement this interface to expose a command. The bridge's plugin loader discovers implementations via reflection — no registration code needed. |
| `QueuedCommand` | Request model: task ID, command name, parameters, `DryRun` flag, `RequestId`, progress reporting (`ReportProgress`). |
| `CommandResponse` | Response model with `Success`/`Error` factories and `ToJson()`. |
| `CommandParamSchema` | Parameter metadata (type, required, default, enum values, deprecation, sensitive, unit kind) published via the schema endpoint. |
| `CommandDef` / `CommandSchema` | Models of the `GET /api/commands` schema response. |
| `ParamAttribute` + `ParameterBinder` | Typed binding from the raw parameters dictionary to a POCO (`Bind<T>`); throws `MissingParameterException` / `ParameterTypeException`. |
| `PagedResult<T>` + `PagedResultBuilder` | Paging helper (`limit`/`offset` parsing, clamping, `has_more`). |
| `CommandNameResolver` | Command-name resolution: `@version` splitting, domain paths, underscore reversal. |
| `BridgeRegistration` | **Programmatic registration entry point** — register commands from your own add-in using only this package. |
| `ICommandRegistry` | Abstraction over the bridge's command registry, for tests that verify registration logic. |
| `IBridgePlugin` | *Reserved* for structured plugin lifecycle hooks. The current bridge loader auto-discovers `IBridgeCommand` only and does not invoke this interface yet. |

Target framework: **netstandard2.0** — no Revit API reference required, compatible with every Revit version the bridge supports (2019–2024).

## Install

The package uses plain semantic versioning. It is **Revit-version independent** — the same package works with every supported Revit version (2019–2024):

```
dotnet add package RevitCliBridge.Abstractions --version 2.1.0
```

To always stay on the latest patch in a minor line:

```xml
<PackageReference Include="RevitCliBridge.Abstractions" Version="1.17.*"/>
```

## Quick start: expose a plugin command

Create a class library targeting **net48** (you will cast to Revit API types). Reference this package plus your Revit version's API package:

```xml
<PackageReference Include="RevitCliBridge.Abstractions" Version="1.17.*" />
<PackageReference Include="Revit_API_x64" Version="2022.*" />
```

Implement `IBridgeCommand`. All members are required; a public parameterless constructor is required for the loader to instantiate the class:

```csharp
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

public class MyCompanyDoThing : IBridgeCommand
{
    public string CommandName => "mycompany_do_thing";
    public string Version => "v1";
    public string Description => "Does a thing.";
    public string Category => "MyCompany";
    public bool SupportsDryRun => false;
    public string[] Aliases => System.Array.Empty<string>();
    public string[] Examples => new[] { "{\"input\":\"value\"}" };

    public CommandParamSchema[] Parameters => new[]
    {
        new CommandParamSchema { Name = "input", Type = "string", Required = true }
    };

    public string Handle(object uiApplication, QueuedCommand cmd)
    {
        // uiApplication is passed as object to keep this package Revit-free.
        var app = (UIApplication)uiApplication;

        // Typed binding from the parameters dictionary to a POCO.
        var p = ParameterBinder.Bind<DoThingParams>(
            cmd.Parameters as System.Collections.Generic.IDictionary<string, object>);

        // ... call the Revit API via app / app.ActiveUIDocument.Document ...

        return CommandResponse.Success(cmd.TaskId, new { result = p.Input }).ToJson();
    }
}

public class DoThingParams
{
    [Param("input", Required = true)]
    public string Input { get; set; }
}
```

> `ParameterBinder` supports `int`, `double`, `string`, `bool`, `int[]` and their nullable forms. For manual access, cast `cmd.Parameters` to `Dictionary<string, object>` (the bridge deserializes JSON objects that way).

### Units contract (2.0.0)

Since 2.0.0 the bridge speaks the units contract: a caller never receives a Revit internal unit. Declare `UnitKind` on every dimensional parameter of your schema (added in 2.0.0):

```csharp
new CommandParamSchema { Name = "height", Type = "double", Required = false,
    UnitKind = "length", Default = 3000 }
```

The bridge's middleware converts declared parameters from the request unit (canonical `length=mm`, `area=m2`, `volume=m3`, `angle=deg`, chosen per request via the top-level `units` field) to Revit internal units before your `Handle` runs, so you read internal-unit values and never convert by hand. `get_parameters` reports each parameter's `unit_kind`; specs the contract does not convert yet report `raw` and pass through unchanged. Response-side rewriting (`ResponseFieldKinds`) lives in the bridge assembly — plugin commands that emit dimensional values can read the request's raw `Units` field on `QueuedCommand` to convert on the way out, or emit canonical units and say so in the response.

## Reporting progress

Long-running commands can stream progress to the CLI client. Inside `Handle`, call `ReportProgress` on the `QueuedCommand`:

```csharp
for (int i = 0; i < walls.Count; i++)
{
    // ... create wall i ...

    cmd.ReportProgress((i + 1) * 100 / walls.Count, $"Created wall {i + 1}/{walls.Count}");
}
```

The bridge wires the reporter to the task's SSE `progress` event before dispatch — the same channel built-in commands use — so `revit-cli` clients observe live updates. Guidelines:

- `percent` is 0–100. The bridge broadcasts `0` when the command starts and a terminal event on completion, so report intermediate values only.
- Call from the Revit main thread (inside `Handle`); marshal back if you spawn background work.
- The call is a no-op when no reporter is wired (unit tests, standalone execution), so no null-checks are needed.

## Failure handling

Unattended execution (AI mode, no user at the screen) means Revit warning dialogs would block
your transaction forever. The bridge's built-in commands attach a failure preprocessor to every
transaction; plugin commands should do the same. The pattern is small — carry it in your plugin:

```csharp
using Autodesk.Revit.DB;

public class UnattendedFailurePreprocessor : IFailuresPreprocessor
{
    public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
    {
        foreach (var failure in fa.GetFailureMessages())
        {
            if (failure.GetSeverity() == FailureSeverity.Warning)
                fa.DeleteWarning(failure);          // auto-dismiss warnings
            else if (failure.GetSeverity() == FailureSeverity.Error)
            {
                fa.ResolveFailure(failure);         // auto-resolve errors
                return FailureProcessingResult.ProceedWithCommit;
            }
        }
        return FailureProcessingResult.Continue;
    }
}
```

Attach it to each transaction before committing:

```csharp
using (var t = new Transaction(doc, "MyCompany: do thing"))
{
    var options = t.GetFailureHandlingOptions();
    options.SetFailuresPreprocessor(new UnattendedFailurePreprocessor());
    t.SetFailureHandlingOptions(options);

    t.Start();
    // ... modify the model ...
    t.Commit();
}
```

Without this, any "duplicate element" or "elements joined" warning pops a modal dialog on the
Revit main thread and the command appears to hang until a human intervenes.

## Deployment via plugin DLL discovery

1. Build the class library.
2. Sign the DLL with **Authenticode** (default requirement — unsigned DLLs are rejected).
3. Copy it to the `CliBridgePlugins` folder next to the bridge add-in:

```
<Revit addins folder>\RevitCliBridge\     (bridge add-in location)
    RevitCliBridge.dll
    CliBridgePlugins\
        MyCompany.Plugin.dll               (your plugin)
```

The bridge scans the folder at startup and registers every non-abstract `IBridgeCommand` implementation under `{CommandName}@{Version}` (plus the bare name when `Version` is `"v1"`). The command immediately appears at `GET /api/commands` and is callable via `POST /api/execute`.

For unsigned development builds, set `"allow_unsigned_plugins": true` in `cli_bridge_setting.json`. A `trusted_publishers` whitelist can restrict signed DLLs to known certificate subjects.

## Alternative: register from your own add-in (recommended)

DLL discovery is convenient for zero-code deployments, but if you already ship a Revit add-in you can register commands explicitly with `BridgeRegistration` — using **only this package**, with no reference to the bridge assembly and no `CliBridgePlugins` folder:

```csharp
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;   // the only RevitCliBridge reference you need

public class MyCompanyApp : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        BridgeRegistration.Register(new MyCompanyDoThing());
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
```

`BridgeRegistration.Register(IBridgeCommand)` registers the command under its versioned key (`mycompany_do_thing@v1`), the bare name (default version), and all aliases — the same keys the bridge's built-in discovery would create. An explicit-key overload (`Register("name@v2", cmd)`) is available for advanced cases.

Under the hood the facade locates the already-loaded bridge assembly in the Revit process and forwards to the bridge's command registry via reflection. You therefore never reference (or ship a copy of) `RevitCliBridge.dll`, which keeps type identity intact.

| | Programmatic registration | DLL discovery |
|---|---|---|
| Authenticode signing | Not required | Required by default |
| Registration failures | Surface in your code | Logged, plugin skipped |
| Conditional registration (license/feature checks) | Natural | Not possible |
| Extra add-in project required | Yes | No |

Setup notes:

- The bridge add-in must be installed and started before your registration call runs. Revit loads `.addin` manifests alphabetically; the bridge's manifest is named `@RevitCliBridge.addin`, so its `@` prefix sorts it ahead of ordinary names — any conventionally named manifest loads after it.
- Registration is thread-safe and invalidates the bridge's schema cache, so commands appear at `GET /api/commands` immediately, even when registered after startup.

### Deployment rule: never ship your own `RevitCliBridge.Abstractions.dll` or `Newtonsoft.Json.dll`

The bridge folder already provides both, and the runtime loader falls back to the bridge's copies for any assembly your plugin didn't load. If your plugin's output folder contains its own (possibly older) `RevitCliBridge.Abstractions.dll`, .NET loads that exact copy instead, your `IBridgeCommand` type splits from the bridge's, and registration breaks **silently** — DLL discovery just skips your command with no error. Exclude both from your build output:

```xml
<PackageReference Include="RevitCliBridge.Abstractions" Version="1.17.*" ExcludeAssets="runtime" />
<PackageReference Include="Newtonsoft.Json" ExcludeAssets="runtime" />
```

`ExcludeAssets="runtime"` keeps the compile-time reference while keeping the DLLs out of your publish output. If you copy files manually instead, delete both DLLs before shipping.

### Verify discovery

After starting Revit with the bridge enabled, confirm your command is registered:

```bash
revit-cli catalog
```

Your command should appear in the index (name, category, summary). `GET /api/commands` returns the full schema including parameters.

## Versioned commands

Override `Version` (e.g. `"v2"`) to publish a breaking change under the same command name. Callers pin a version with `mycompany_do_thing@v2`; unversioned requests resolve to the default version (`v1`).

## Strong naming

This assembly is strong-name signed. The bridge registers an `AssemblyResolve` fallback that serves `RevitCliBridge.Abstractions.dll` from its own folder, so a plugin compiled against an older package version binds to the newer copy the bridge already loaded — provided the plugin does **not** ship its own copy of the DLL (see the deployment rule above). Plugins built against package **1.4.0 or later** are supported; earlier versions lack the `IBridgeCommand.Version` member and fail to load.

## Versioning

This package uses plain semantic versioning, not the Nice3point `{RevitYear}.{Major}.{Minor}` scheme. Because the assembly is netstandard2.0 with no Revit API reference, the binary is identical across every supported Revit version — a single version line is honest about that, and one package serves all Revit versions (the bridge itself, which _does_ bind Revit API types, keeps the Nice3point-style version).

`AssemblyVersion`, `FileVersion` and the package `Version` all track the package version (currently `2.1.0`).

## License

MIT — see [the repository LICENSE](https://github.com/iandelivery/RevitCLI/blob/main/LICENSE).
