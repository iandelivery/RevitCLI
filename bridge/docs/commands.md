# Commands

> Command catalog, naming conventions, parameter binding, and how to add a
> command. For HTTP shapes see [api-reference.md](api-reference.md); for routing
> internals see [components.md](components.md#commandrouter--commandnameresolver).

## Naming Convention

| Pattern | Prefix | Examples |
|------|------|------|
| Query | `get_` | `get_elements`, `get_levels`, `get_family_symbols` |
| Create | `create_` | `create_wall`, `create_grid`, `create_family_instance` |
| Set | `set_` | `set_parameter`, `set_wall_constraint`, `set_active_view` |
| Transform | `verb_element` | `move_element`, `copy_element`, `rotate_element` |
| Batch | `batch_` or plural | `batch_set_param`, `batch_export`, `create_walls` |
| Other | direct verb | `delete_element`, `select_elements`, `export_view`, `undo` |

## Unit Convention

Dimension parameters use **millimeters (mm)**, auto-converted to Revit's feet
internally. Returns convert feet → mm.

## Command Versioning

Handlers expose a `Version` property (default `"v1"` via `BridgeCommandBase`).
Overriding it lets you ship breaking changes under a new version tag without
disrupting existing callers:

```csharp
public class CreateWallHandlerV2 : DocumentCommandBase
{
    public override string CommandName => "create_wall";
    public override string Version => "v2";   // pin via create_wall@v2
    // ...
}
```

Clients pin a version by appending `@v2` to the command name:
`{"command": "create_wall@v2", ...}`. Requests without a suffix resolve to the
default version (`v1`). The router registers each handler under both
`{name}@{version}` and (for `v1`) the bare `{name}`. The same rules apply to
aliases and to the domain-path / underscore-reversal matching in
`CommandNameResolver` — e.g. `domain.create_wall@v2` and `wall_create@v2` both
resolve to `create_wall@v2`.

## Catalog (52 commands, 10 categories)

| Category | Command | Modifies Model |
|------|------|:------------:|
| **General** | `ping`, `undo` | No |
| **Documents** | `document_info`, `doc_list`, `doc_open`, `doc_close`, `doc_save`, `doc_save_as`, `doc_sync` | save/save_as/sync: Yes |
| **Query** | `get_elements`, `get_element_by_id`, `get_element_types`, `get_family_symbols`, `get_family_symbol`, `get_symbol_instances`, `get_levels`, `get_parameters`, `get_views`, `get_sheets`, `get_rooms`, `search_elements` | No |
| **Creation** | `create_wall`, `create_walls`, `create_door`, `create_window`, `create_grid`, `create_family_instance`, `create_view`, `create_sheet`, `create_room` | Yes |
| **Modification** | `move_element`, `copy_element`, `rotate_element`, `mirror_element`, `delete_element`, `hide_elements`, `select_elements`, `set_offset` | most: Yes |
| **Parameters** | `set_parameter`, `set_parameter_by_id`, `batch_set_param` | Yes |
| **Architecture** | `set_wall_constraint`, `set_walls_constraint` | Yes |
| **Views** | `set_active_view`, `zoom_to_fit`, `export_view`, `apply_view_template`, `place_on_sheet`, `tag_rooms` | apply_view_template, place_on_sheet, tag_rooms: Yes |
| **Batch** | `batch`, `batch_export` | Yes |
| **Raw** | `execute_raw` (gated by `allow_raw_execution`) | Yes |

> Full per-command schemas (params, types, examples, aliases) are available at
> runtime via `GET /api/commands` or the lightweight `GET /api/catalog`.

## Parameter Binding

New handlers use a declarative POCO + `[Param]` attribute instead of manual
`GetXxxOrNull` calls. See [components.md](components.md#parameterbinder--typed-binding)
for the binder internals.

```csharp
public class CreateWallParams
{
    [Param("start_x", Required = true)] public double StartX { get; set; }
    [Param("start_y", Required = true)] public double StartY { get; set; }
    [Param("end_x",   Required = true)] public double EndX { get; set; }
    [Param("end_y",   Required = true)] public double EndY { get; set; }
    [Param("level_id",Required = true)] public int LevelId { get; set; }
    [Param("height",  Default = 3000.0)] public double Height { get; set; }
}
```

Supported types: `int`, `int?`, `double`, `double?`, `string`, `int[]`, `bool`,
`bool?`. Nullable value types are optional. Missing required →
`MissingParameterException`; bad type → `ParameterTypeException`. Both map to
`CommandResponse.Error` via `BridgeCommandBase.TryBind<T>`.

## Adding a New Command

1. Create a handler in `CliBridge/Handlers/{Category}/`, inheriting
   `BridgeCommandBase` (or `DocumentCommandBase` if you need an active doc):

```csharp
public class MyCommandHandler : DocumentCommandBase
{
    public override string CommandName => "my_command";
    public override string Description => "Does something useful";
    public override string Category => "Custom";
    public override bool SupportsDryRun => false;
    public override string[] Aliases => new[] { "my_cmd" };
    public override CommandParamSchema[] Parameters => new[]
    {
        new() { Name = "param", Type = "string", Required = true }
    };

    protected override string Execute(UIApplication app, Document doc,
        Dictionary<string, object> parameters, QueuedCommand cmd)
    {
        var p = TryBind<MyParams>(cmd, out var error);
        if (p is null) return error!;
        // ... Revit API operations ...
        return CommandResponse.Success(cmd.TaskId, new { result = "done" }).ToJson();
    }
}
```

2. No manual registration — `CommandRouter` auto-discovers via reflection.

3. For third-party plugins: compile a DLL implementing `IBridgeCommand` and place
   it in `CliBridgePlugins/` next to `RevitCliBridge.dll`. No recompilation of
   the bridge needed. By default the DLL must carry a valid Authenticode
   signature (publisher checked against `trusted_publishers` when configured);
   set `allow_unsigned_plugins: true` for local dev builds. See
   [components.md](components.md#bridgepluginloader--signature-gating) for
   details.

→ Next: [threading.md](threading.md) for concurrency & safety.
