# Sheet viewport commands

## Sub-features

- Read — `get_sheets` (id, sheet_number, name, viewport_count),
  `get_views` (id, name, view_type, is_template),
  `get_sheet_viewports --sheet_id` (per viewport: view_id/name/type,
  viewport_id, pinned, box/center/label in request length units).
- Place — `place_on_sheet --sheet_id --views '[{view_id, x?, y?}]'`
  (nested object array; x/y default to sheet center; unit_kind length;
  placing a view that is already on a sheet fails cleanly).
- Check — `check_viewport_overlaps` (accepts `viewport_ids`), 
  `check_sheet_bounds` (accepts `viewport_ids`).
- Move — `move_viewport --moves '[{viewport_id, x, y}]'` (nested array,
  unit_kind length, x/y required).
- Remove — `remove_view_from_sheet --sheet_id --view_ids <int[]>`.

## How to get to it (user POV)

Sheet composition from an agent: put views on sheets at exact spots,
detect overlaps/boundary violations, nudge, take off again.

## Driving it with the revit-cli client

Nested `views`/`moves` params are object arrays the client does not parse
from flags — pass them with `raw -j` (or `--views '<json>'` only if the
client's raw-string passthrough is acceptable for the drive).

```powershell
Run "sheets" ($U + @("get_sheets"))          # pick a sheet_id (e.g. A102)
Run "views"  ($U + @("get_views"))           # pick an UNPLACED view (e.g. an elevation)

# place at exact coordinates (mm): center round-trip proves unit handling
Run "place" ($U + @("raw","-j",
  ('{"command":"place_on_sheet","parameters":{"sheet_id":<SHEET>,"views":[{"view_id":<VIEW>,"x":100,"y":200}]}}')))
# success; capture the new viewport_id from the result
Run "viewports" ($U + @("get_sheet_viewports","--sheet-id","<SHEET>"))
# the new viewport's center == {x:100, y:200}  (request-unit round-trip)

Run "overlaps" ($U + @("check_viewport_overlaps","--sheet-id","<SHEET>"))   # or --viewport-ids
Run "bounds"   ($U + @("check_sheet_bounds","--sheet-id","<SHEET>"))

Run "move" ($U + @("raw","-j",
  ('{"command":"move_viewport","parameters":{"moves":[{"viewport_id":<VP>,"x":400,"y":300}]}}')))
Run "viewports2" ($U + @("get_sheet_viewports","--sheet-id","<SHEET>"))     # center == {400,300}

Run "remove" ($U + @("remove_view_from_sheet","--sheet-id","<SHEET>","--view-ids","<VIEW>"))
Run "viewports3" ($U + @("get_sheet_viewports","--sheet-id","<SHEET>"))     # viewport gone; count back to baseline

# error paths worth one drive each:
# place an already-placed view -> clean failure naming the view
# remove a view not on the sheet -> clean failure
```

## Gotchas

- Placement coordinates are sheet mm (request length unit); the center
  echo after place/move is the strongest round-trip oracle.
- An already-placed view (e.g. Level 1 on A102 in the rac sample) is the
  natural negative case; pick an unplaced elevation/plan for the happy
  path.
- `get_sheet_viewports` reports `viewport_count` per sheet from
  `get_sheets` — assert both agree.
- Dialogs opened by `doc_open` can stall the whole journey — sweep, then
  re-drive; do not weaken the coordinates to "approximately".
