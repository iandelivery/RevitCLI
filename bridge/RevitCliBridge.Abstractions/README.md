# RevitCliBridge.Abstractions

Plugin SDK for [RevitCliBridge](https://github.com/iandelivery/RevitCLI) — implement third-party bridge commands without referencing the bridge assembly or locking to a specific Revit version.

## What this package gives you

| Type | Purpose |
|------|---------|
| `IBridgeCommand` | Implement this interface to expose a command. The bridge's plugin loader discovers implementations via reflection — no registration code needed. |
| `QueuedCommand` | Request model: task ID, command name, parameters, `DryRun` flag, `RequestId`. |
| `CommandResponse` | Response model with `Success`/`Error` factories and `ToJson()`. |
| `CommandParamSchema` | Parameter metadata (type, required, default, enum values, deprecation, sensitive) published via the schema endpoint. |
| `CommandDef` / `CommandSchema` | Models of the `GET /api/commands` schema response. |
| `ParamAttribute` + `ParameterBinder` | Typed binding from the raw parameters dictionary to a POCO (`Bind<T>`); throws `MissingParameterException` / `ParameterTypeException`. |
| `PagedResult<T>` + `PagedResultBuilder` | Paging helper (`limit`/`offset` parsing, clamping, `has_more`). |
| `CommandNameResolver` | Command-name resolution: `@version` splitting, domain paths, underscore reversal. |
| `ICommandRegistry` | Abstraction over the bridge's command registry, for tests that verify registration logic. |
| `IBridgePlugin` | *Reserved* for structured plugin lifecycle hooks. The current bridge loader auto-discovers `IBridgeCommand` only and does not invoke this interface yet. |

Target framework: **netstandard2.0** — no Revit API reference required, compatible with every Revit version the bridge supports (2019–2022).

## Install

The package uses plain semantic versioning (`1.7.0`). It is **Revit-version independent** — the same package works with every supported Revit version (2019–2022):

```
dotnet add package RevitCliBridge.Abstractions --version 1.7.0
```

To always stay on the latest patch in a minor line:

```xml
<PackageReference Include="RevitCliBridge.Abstractions" Version="1.7.*"/>
```

## Quick start: expose a plugin command

Create a class library targeting **net48** (you will cast to Revit API types). Reference this package plus your Revit version's API package:

```xml
<PackageReference Include="RevitCliBridge.Abstractions" Version="1.7.*" />
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

## Deployment

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

## Versioned commands

Override `Version` (e.g. `"v2"`) to publish a breaking change under the same command name. Callers pin a version with `mycompany_do_thing@v2`; unversioned requests resolve to the default version (`v1`).

## Strong naming

This assembly is strong-name signed. Third-party addins can reference it from a different Revit addin folder without type-identity splits — the bridge's own copy and the plugin's copy unify by strong name + version.

## Versioning

This package uses plain semantic versioning (`1.7.0`), not the Nice3point `{RevitYear}.{Major}.{Minor}` scheme. Because the assembly is netstandard2.0 with no Revit API reference, the binary is identical across every supported Revit version — a single version line is honest about that, and one package serves all Revit versions (the bridge itself, which _does_ bind Revit API types, keeps the Nice3point-style version).

`AssemblyVersion`, `FileVersion` and the package `Version` all track `1.7.0`.

## License

MIT — see [the repository LICENSE](https://github.com/iandelivery/RevitCLI/blob/main/LICENSE).
