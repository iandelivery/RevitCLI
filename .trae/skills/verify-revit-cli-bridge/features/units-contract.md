# The units contract

## Sub-features

- Vocabulary — kinds `length/area/volume/angle` (+ `raw` passthrough);
  canonical `mm/m2/m3/deg`; allowed: length `mm cm m ft in`, area
  `m2 mm2 ft2`, volume `m3 mm3 ft3 l`, angle `deg rad`.
- Request side — top-level `units` = map (per-kind overrides) or the
  string `"project"` (needs an active document). Declared `unit_kind`
  inputs (schema-level, incl. defaults and nested object items) convert
  request-unit → internal feet, never rounded.
- Response side — fields named in the handler's `ResponseFieldKinds`
  convert internal → request unit, rounded to 3 decimals; a top-level
  `units` echo is stamped on every response a handler **returns**, success
  or `CommandResponse.Error` alike. No echo when `UnitsContext.Finish`
  never runs: a `units` parse failure (bad unit or bad kind), `"project"`
  without a document, dry-run rejected for an unsupported command, unknown
  command, a handler that throws, a timeout, queue-full.
- Rejections name the vocabulary — `Unknown unit kind 'mass' in 'units'.
  Allowed kinds: length, area, volume, angle.` and `Unit 'yard' is not
  allowed for kind 'length'. Allowed units: mm, cm, m, ft, in.` (client
  exit 4; the error body has no `units` key).
- `get_units` — `{project, canonical, allowed}`; `project` reflects the
  document's decimal display units, **null without a document**; a display
  type with no mapping falls back to the canonical unit.
- `get_parameters` reports each parameter's `unit_kind` (`raw` when the
  contract does not convert that spec).
- Version branches — `ParameterUnits.KindFor` and `UnitsContext.ReadProjectUnits`
  compile the `UnitType` enum path for 2019/2020 and the `SpecTypeId` path
  for 2021+ (`GetDataType()` from 2022 on); the `#if`s name the legacy
  versions so 2023/2024 take the modern branch by default.

## How to get to it (user POV)

"Give me that wall length in feet, and let me create it in feet" — an
agent never sees a Revit internal unit.

## Driving it with the revit-cli client

```powershell
Run "units-no-doc" ($U + @("get_units"))
# before doc_open: success with "project": null + full canonical/allowed

# ... doc_open a scratch model first (see document-commands.md) ...
Run "units-doc" ($U + @("get_units"))       # project matrix populated

Run "units-bad-unit" ($U + @("raw","-j",'{"command":"get_units","units":{"length":"yard"}}'))
# "Unit 'yard' is not allowed for kind 'length'. Allowed units: mm, cm, m, ft, in." (exit 4, NO units echo)
Run "units-bad-kind" ($U + @("raw","-j",'{"command":"get_units","units":{"mass":"kg"}}'))
# "Unknown unit kind 'mass' in 'units'. Allowed kinds: length, area, volume, angle." (exit 4, NO units echo)

# input+output round-trip in ft (5000 mm == 16.404 ft):
Run "wall-ft" ($U + @("--units","length=ft","create_wall","--start_x","0","--start_y","0",
                      "--end_x","16.404","--end_y","0","--level_id","<LEVEL_ID>"))
Run "box-ft"  ($U + @("--units","length=ft","get_element_boxes","--element_ids","<WALL_ID>"))
# box max_x == 16.404 (request-unit echo both ways); response carries units echo
Run "undo"    ($U + @("undo"))

# echo-on-error proof — only a handler that RETURNS an error has the echo:
Run "echo-on-error" ($U + @("raw","-j",'{"command":"get_element_boxes","parameters":{"element_ids":[99999999]},"units":{"length":"ft"}}'))
# exit 0, data.elements[0].found=false, top-level "units":{"length":"ft",...}
Run "no-echo-unknown-cmd" ($U + @("raw","-j",'{"command":"no_such_cmd","units":{"length":"ft"}}'))
# exit 4, no "units" key
Run "no-echo-dryrun" ($U + @("raw","-j",'{"command":"get_units","parameters":{},"units":{"length":"ft"},"dry_run":true}'))
# exit 4 ("does not support --dry-run"), no "units" key
```

## Gotchas

- `"project"` without a document fails with `units "project" requires an
  active document.` — drive it only with a document open.
- **Units are dropped when the command name is rewritten.** `CommandRouter`
  rebuilds the `QueuedCommand` without `Units`/`ResolvedUnits` when
  `ResolveCommandName` changes the name — a domain path such as
  `elements.walls.create`, or an undeclared alias such as `wall_create` →
  `create_wall`. The request then converts in canonical mm and echoes mm;
  `"project"` never errors on that path. Declared `Aliases` are unaffected.
  Reproduce with `--units length=ft wall_create ...` on a live bridge and
  compare the echoed `units` with the `create_wall` run. Recorded as a
  product gap (2026-11-26), not fixed here.
- Input conversion applies to schema-declared params only; raw payload
  fields the handler reads itself stay in internal units unless the
  handler converts.
- Output rounding is 3 decimals (AwayFromZero); tolerance assertions
  should use ±0.001.
- Schema `unit_kind` is visible in `schema <command>` and in dynamic
  `--help` as `[unit kind: <kind>]`.
