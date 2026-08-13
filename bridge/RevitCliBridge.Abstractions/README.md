# RevitCliBridge.Abstractions

Plugin interfaces for [RevitCliBridge](https://github.com/revit-cli/revit-cli-opensource) — enables third-party command handler development without a Revit API dependency.

## What this package gives you

- `IBridgeCommand` / `BridgeCommandBase` — implement a Revit operation as a bridge command
- `ICommandRegistry` — register/unregister commands at runtime (thread-safe)
- `IBridgePlugin` — lifecycle hooks for plugin DLLs loaded by the bridge
- `QueuedCommand`, `CommandResponse`, `CommandParamSchema`, `ParamAttribute` — request/response model and parameter binding helpers
- `CommandNameResolver` — command-name resolution with `@version` support
- `ParameterBinder` — typed binding from `QueuedCommand.Parameters` to a POCO
- `PagedResultBuilder` — paginated response helper for large result sets

Target framework: **netstandard2.0** — no Revit API reference required, compatible with every Revit version the bridge supports (2019–2022).

## Install

```
dotnet add package RevitCliBridge.Abstractions --version 0.1.0
```

## Quick start: expose a plugin command

```csharp
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;

public class MyCompanyCommand : BridgeCommandBase
{
    public override string CommandName => "mycompany_do_thing";
    public override string Category => "MyCompany";
    public override CommandParamSchema[] Parameters => new[]
    {
        new() { Name = "input", Type = "string", Required = true }
    };

    protected override string Execute(UIApplication app, QueuedCommand cmd)
    {
        var input = cmd.Parameters["input"] as string;
        // ... call your service ...
        return CommandResponse.Success(cmd.TaskId, new { result = input }).ToJson();
    }
}
```

Register it from your `IExternalApplication.OnStartup`:

```csharp
CommandRouter.Register("mycompany_do_thing@v1", new MyCompanyCommand());
```

The command immediately appears at `GET /api/commands` and is callable via `POST /api/execute`.

## Strong naming

This assembly is strong-name signed. Third-party addins can reference it from a different Revit addin folder without type-identity splits — the bridge's own copy and the plugin's copy unify by strong name + version.

## Versioning

0.x — early development; breaking changes possible before 1.0.
The `AssemblyVersion` is pinned to `0.0.0.0` so patch releases don't require binding redirects.

## License

MIT — see [the repository LICENSE](https://github.com/iandelivery/RevitCLI/blob/main/LICENSE).
