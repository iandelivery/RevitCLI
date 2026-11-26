# Sheet viewport commands

## Sub-features

- Read — `get_sheets` (id, sheet_number, name, is_placeholder,
  viewport_count), `get_views` (id, name, view_type, is_template, template_id
  — **no placement field**; find an unplaced view by trying a place, or by
  diffing against every sheet's `get_sheet_viewports`),
  `get_sheet_viewports --sheet_id` (per viewport: view_id/name/type,
  viewport_id, pinned, box/center in request length units, label).
- Place — `place_on_sheet --sheet_id --views '[{view_id, x?, y?}]'`
  (nested object array; unit_kind length). **x/y default to the sheet's
  origin (lower-left), not its center** — always pass both. `x` and `y` must
  be set together: a half-set pair fails with `must set both 'x' and 'y'`.
  Placing a view that is already on a sheet fails naming the view.
- Check — `check_viewport_overlaps --sheet_id [--viewport_ids]`
  (`sheet_id` is required; `viewport_ids` only narrows the pair check),
  `check_sheet_bounds --sheet_id [--viewport_ids] [--margin N]` (margin in
  request length units, default 10 mm).
- Move — `move_viewport --moves '[{viewport_id, x, y}]'` (nested array,
  unit_kind length, x/y required; alias `viewport_move`).
- Remove — `remove_view_from_sheet --sheet_id --view_ids <int[]>` (alias
  `delete_viewport`). Removing a view that is not on the sheet is a
  **success envelope** with `removed: false` + reason and `removed_count: 0`,
  not an error.

## How to get to it (user POV)

Sheet composition from an agent: put views on sheets at exact spots,
detect overlaps/boundary violations, nudge, take off again.

## Driving it with the revit-cli client

Nested `views`/`moves` params are object arrays the client does not parse
from flags — pass them with `raw -j` (or `--views '<json>'` only if the
client's raw-string passthrough is acceptable for the drive).

```powershell
Run "sheets" ($U + @("get_sheets","--json"))          # data.sheets[]: pick a sheet_id (e.g. A102)
Run "views"  ($U + @("get_views","--json"))           # data.views[]: filter view_type to FloorPlan /
                                                     # CeilingPlan / Elevation / Section / ThreeD (not
                                                     # ProjectBrowser, Sheet, Schedule, templates)

# place at exact coordinates (mm): center round-trip proves unit handling.
# Body from a file: pwsh turns \" inside -j into a literal backslash (400).
$place = '{"command":"place_on_sheet","parameters":{"sheet_id":<SHEET>,"views":[{"view_id":<VIEW>,"x":100,"y":200}]}}'
[IO.File]::WriteAllText("$SCRATCH\place.json", $place)
Run "place" ($U + @("raw","-j",(Get-Content "$SCRATCH\place.json" -Raw)))
# success; data.placed[0].viewport_id, location_x 100, location_y 200
Run "viewports" ($U + @("get_sheet_viewports","--sheet_id","<SHEET>","--json"))
# the new viewport's center == {x:100, y:200}  (request-unit round-trip)
# half-set pair:
#   {"view_id":<VIEW2>,"x":100}  -> exit 4: "must set both 'x' and 'y'"

Run "overlaps" ($U + @("check_viewport_overlaps","--sheet_id","<SHEET>"))   # --viewport_ids optional
Run "bounds"   ($U + @("check_sheet_bounds","--sheet_id","<SHEET>","--margin","10"))

$move = '{"command":"move_viewport","parameters":{"moves":[{"viewport_id":<VP>,"x":400,"y":300}]}}'
[IO.File]::WriteAllText("$SCRATCH\move.json", $move)
Run "move" ($U + @("raw","-j",(Get-Content "$SCRATCH\move.json" -Raw)))
Run "viewports2" ($U + @("get_sheet_viewports","--sheet_id","<SHEET>"))     # center == {400,300}

Run "remove" ($U + @("remove_view_from_sheet","--sheet_id","<SHEET>","--view_ids","<VIEW>"))
# success, removed_count 1
Run "viewports3" ($U + @("get_sheet_viewports","--sheet_id","<SHEET>"))     # viewport gone; count back to baseline

# error paths worth one drive each:
Run "place-again" (...same place body...)               # exit 4, message names the view / "already on a sheet"
Run "remove-again" (...same remove...)                   # exit 0, "removed": false, removed_count 0
Run "overlaps-nosheet" ($U + @("check_viewport_overlaps"))   # exit 4: --sheet_id is required
```

## Gotchas

- Placement coordinates are sheet mm (request length unit); the center
  echo after place/move is the strongest round-trip oracle.
- In the rac sample **every plan view is already placed** on some sheet.
  For the place happy path pick an unplaced elevation/3D view, or drive
  move/check/remove on an existing viewport (e.g. A102's) — that still
  exercises four of the six commands. `get_views` carries no placement flag.
- `get_sheet_viewports` reports `viewport_count` per sheet from
  `get_sheets` — assert both agree.
- Dialogs opened by `doc_open` can stall the whole journey — sweep, then
  re-drive; do not weaken the coordinates to "approximately".
