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
  convert internal → request unit, rounded to 3 decimals; every response
  carries a top-level `units` echo — on success AND on error (only a
  units PARSE failure skips the echo).
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

# echo-on-error proof: any failing command (e.g. element not found) still
# carries the top-level units map in its error JSON.
```

## Gotchas

- `"project"` without a document fails with `units "project" requires an
  active document.` — drive it only with a document open.
- Input conversion applies to schema-declared params only; raw payload
  fields the handler reads itself stay in internal units unless the
  handler converts.
- Output rounding is 3 decimals (AwayFromZero); tolerance assertions
  should use ±0.001.
- Schema `unit_kind` is visible in `schema <command>` and in dynamic
  `--help` as `[unit kind: <kind>]`.
