# The units contract

## Sub-features

- Vocabulary — kinds `length/area/volume/angle`; canonical `mm/m2/m3/deg`;
  allowed: length `mm cm m ft in`, area `m2 mm2 ft2`, volume `m3 mm3 ft3 l`,
  angle `deg rad`. (`raw` is a `unit_kind` label on unconverted parameter
  specs, NOT a valid `units`-map kind — `{"raw": "x"}` is rejected.)
- Request side — top-level `units` = map (per-kind overrides) or the
  string `"project"` (needs an active document). Declared `unit_kind`
  inputs (schema-level, incl. defaults and nested object items) convert
  request-unit → internal feet, never rounded.
- Response side — fields named in the handler's `ResponseFieldKinds`
  convert internal → request unit, rounded to 3 decimals; every response
  the handler RETURNS carries a top-level `units` echo — on success AND
  on error. The echo is absent on: a units parse failure, `"project"`
  without a document, `--dry-run` on an unsupported command, an unknown
  command, and any exception thrown out of a handler (all built outside
  `UnitsContext.Finish`).
- Name resolution keeps units — `wall_create` (underscore reversal),
  `elements.create_wall` (domain-path suffix) and `wall_create@v1`
  convert and echo like `create_wall`. `elements.walls.create` does NOT
  resolve (the suffix rules try `walls.create`, then `create`) → "Unknown
  command: elements.walls.create. Did you mean: ...?", exit 4, no echo.
- Rejections name the vocabulary — `Unknown unit kind 'mass' in 'units'.
  Allowed kinds: length, area, volume, angle.` and `Unit 'yard' is not
  allowed for kind 'length'. Allowed units: mm, cm, m, ft, in.`
- `get_units` — `{project, canonical, allowed}`; `project` reflects the
  document's decimal display units, **null without a document**.
- `get_parameters` reports each parameter's `unit_kind` (`raw` when the
  contract does not convert that spec).

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
# failed event: "Unit 'yard' is not allowed for kind 'length'. Allowed units: mm, cm, m, ft, in." (exit 4)
Run "units-bad-kind" ($U + @("raw","-j",'{"command":"get_units","units":{"mass":"kg"}}'))
# "Unknown unit kind 'mass' in 'units'. Allowed kinds: length, area, volume, angle." (exit 4)

# input+output round-trip in ft (5000 mm == 16.404 ft):
Run "wall-ft" ($U + @("--units","length=ft","create_wall","--start-x","0","--start-y","0",
                      "--end-x","16.404","--end-y","0","--level-id","<LEVEL_ID>"))
Run "box-ft"  ($U + @("--units","length=ft","get_element_boxes","--element-ids","<WALL_ID>"))
# box max_x == 16.404 (request-unit echo both ways); response carries units echo
Run "undo"    ($U + @("undo"))

# name rewrite keeps units (live 2026-11-26, fixed in PR #123). The client
# resolves aliases/domain paths ONLY on the raw path — `wall_create` typed
# as a command name is "Unknown command" client-side (see
# schema-discovery.md in the client skill). Use raw -j, JSON from a file:
'{"command":"wall_create","parameters":{"start_x":0,"start_y":1000,"end_x":16.404,"end_y":1000,"level_id":<LEVEL_ID>},"units":{"length":"ft"}}' |
  Set-Content "$SCRATCH\alias.json" -NoNewline
Run "wall-alias-ft" ($U + @("raw","-j",(Get-Content "$SCRATCH\alias.json" -Raw)))
# exit 0; "units":{"length":"ft"}, data.end_x 16.404, data.height 13.123 (4000 mm wall type in ft)
Run "box-alias-ft" ($U + @("--units","length=ft","get_element_boxes","--element_ids","<ID>"))  # max_x 16.404
Run "box-alias-mm" ($U + @("get_element_boxes","--element_ids","<ID>"))                        # max_x 4999.939
# same for "elements.create_wall" and "wall_create@v1"; "elements.walls.create" -> exit 4 Unknown command
Run "undo" ($U + @("undo"))    # one per created wall

# echo-on-error proof (live): a handler-returned error still carries the units map —
Run "err-echo" ($U + @("--units","length=ft","get_element_by_id","--element_id","99999999"))
# exit 4 "Element with ID 99999999 not found." + "units":{"length":"ft"}
# ...but the parse failures above and "project" without a document carry NO units key.
```

## Gotchas

- `"project"` without a document fails with `units "project" requires an
  active document.` — drive it only with a document open.
- Input conversion applies to schema-declared params only; raw payload
  fields the handler reads itself stay in internal units unless the
  handler converts.
- Output rounding is 3 decimals (AwayFromZero); tolerance assertions
  should use ±0.001.
- `data.end_x` alone does NOT discriminate a dropped-units regression
  (16.404 mm in → 16.404 mm out echoes the same number). Assert on
  `units.length` AND `data.height` (13.123 ft vs 4000 mm) or re-read the
  geometry (`max_x` 16.404 vs 0.054).
- Schema `unit_kind` is visible in `schema <command>` and in dynamic
  `--help` as `[unit kind: <kind>]`.
