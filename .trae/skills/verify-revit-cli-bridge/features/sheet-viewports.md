# Sheet viewport commands

## Sub-features

- Read — `get_sheets` (id, sheet_number, name, viewport_count,
  is_placeholder), `get_views` (id, name, view_type, is_template —
  includes DrawingSheet/ProjectBrowser/SystemBrowser rows that can never
  be placed), `get_sheet_viewports --sheet_id` (per viewport:
  view_id/view_name/view_type, viewport_id, pinned, box/center/label in
  request length units).
- Place — `place_on_sheet --sheet_id --views '[{view_id, x?, y?}]'`
  (nested object array; x/y default to `sheet.Origin`, both-or-neither;
  unit_kind length). Result `{sheet_id,count,placed[{view_id,
  viewport_id,location_x,location_y}]}` — no `placed_count`, no `center`.
  A view already on a sheet, a schedule, or a browser row → exit 4
  "View N cannot be placed on sheet A102. A view can sit on one sheet
  only, and schedules are placed as ScheduleSheetInstance, not viewports."
- Check — `check_viewport_overlaps --sheet_id [--viewport_ids]`
  (`sheet_id` REQUIRED) → `{sheet_number,viewport_count,overlap_count,
  has_overlaps,overlaps[{viewport_a,viewport_b,view_name_a,view_name_b,
  overlap{min_x..max_y},overlap_area}]}` (`overlap_area` in area unit).
  `check_sheet_bounds --sheet_id [--viewport_ids] [--margin*] [--titleblock_id]
  [--include_label]` → `{all_in_bounds,out_of_bounds_count,allowed_region,
  title_block_id,viewports[{box_in_bounds,label_in_bounds,overflow{left,
  right,top,bottom}}]}`. **Out of bounds is exit 0** with
  `all_in_bounds:false` — it is a report, not a failure.
- Move — `move_viewport --moves '[{viewport_id, x, y}]'` (nested array,
  unit_kind length, x/y required) → `{count,moved[{viewport_id,x,y}]}`.
- Remove — `remove_view_from_sheet --sheet_id --view_ids <int[]>` →
  `{sheet_id,removed_count,results[{view_id,viewport_id,removed,reason?}]}`.
  Removing a view that is not on the sheet is **exit 0** with
  `removed_count:0` and `reason:"View N is not placed on sheet A102."`.
- `--dry-run` — place/move/remove support it; their results carry NO
  `dry_run` field (unlike create_wall), so prove the rollback by
  re-querying `get_sheet_viewports` (count and centers unchanged).

## How to get to it (user POV)

Sheet composition from an agent: put views on sheets at exact spots,
detect overlaps/boundary violations, nudge, take off again.

## Driving it with the revit-cli client

Nested `views`/`moves` params are object arrays the client does not parse
from flags — pass them with `raw -j`. In a pwsh script, inline JSON with
`\"` reaches the exe with literal backslashes (400); write the JSON to a
file with `-f` formatting and pass `(Get-Content $file -Raw)` as ONE
token. `--view_ids` is an `int[]` schema param that the client does not
coerce from a flag string → use `raw -j` for remove too.

```powershell
Run "sheets" ($U + @("get_sheets"))          # rac sample: 6 sheets; A102 -> 382617, holds Level 1 (312) + Level 2
Run "views"  ($U + @("get_views","--json"))  # keys id,name,view_type,is_template; pick an UNPLACED elevation (8157 North / 8176 East / 8237 West)
$placed = (& $cli @U get_sheet_viewports --sheet_id 382617 --json | ConvertFrom-Json).data.viewports
$cand = (& $cli @U get_views --json | ConvertFrom-Json).data.views |
  Where-Object { $placed.view_id -notcontains $_.id -and $_.view_type -eq 'Elevation' } | Select-Object -First 1

# place at exact coordinates (mm): center round-trip proves unit handling
('{"command":"place_on_sheet","parameters":{"sheet_id":382617,"views":[{"view_id":{0},"x":100,"y":200}]}}' -f $cand.id) |
  Set-Content "$SCRATCH\place.json" -NoNewline
Run "place" ($U + @("raw","-j",(Get-Content "$SCRATCH\place.json" -Raw)))
# exit 0; data.count 1; data.placed[0].viewport_id + location_x 100 / location_y 200
Run "viewports" ($U + @("get_sheet_viewports","--sheet_id","382617"))
# count 3; the new viewport's center == {x:100, y:200}  (request-unit round-trip)

Run "overlaps" ($U + @("check_viewport_overlaps","--sheet_id","382617"))   # exit 0, has_overlaps:false, viewport_count 3
Run "bounds"   ($U + @("check_sheet_bounds","--sheet_id","382617"))        # exit 0, all_in_bounds:true, overflow all 0

# move it far outside the title block -> bounds reports, exit 0
('{"command":"move_viewport","parameters":{"moves":[{"viewport_id":{0},"x":2000,"y":1200}]}}' -f $vpId) |
  Set-Content "$SCRATCH\move.json" -NoNewline
Run "move" ($U + @("raw","-j",(Get-Content "$SCRATCH\move.json" -Raw)))   # data.moved[0].x 2000, y 1200; "Moved 1 viewports."
Run "viewports2" ($U + @("get_sheet_viewports","--sheet_id","382617"))     # center == {2000,1200}
Run "bounds2" ($U + @("check_sheet_bounds","--sheet_id","382617"))        # exit 0; all_in_bounds:false; overflow.right ~1417, top ~690

# dry-run move: reports success, no dry_run field, center unchanged
('{"command":"move_viewport","parameters":{"moves":[{"viewport_id":{0},"x":100,"y":200}]},"dry_run":true}}' -f $vpId) |
  Set-Content "$SCRATCH\movedry.json" -NoNewline
Run "move-dry" ($U + @("raw","-j",(Get-Content "$SCRATCH\movedry.json" -Raw)))   # exit 0, "Moved 1 viewports."
Run "viewports3" ($U + @("get_sheet_viewports","--sheet_id","382617"))           # center STILL {2000,1200}

('{"command":"remove_view_from_sheet","parameters":{"sheet_id":382617,"view_ids":[{0}]}}' -f $cand.id) |
  Set-Content "$SCRATCH\rm.json" -NoNewline
Run "remove" ($U + @("raw","-j",(Get-Content "$SCRATCH\rm.json" -Raw)))   # removed_count 1, "Removed 1 views from sheet A102. The views stay in the project."
Run "viewports4" ($U + @("get_sheet_viewports","--sheet_id","382617"))    # count back to 2
Run "remove-again" ($U + @("raw","-j",(Get-Content "$SCRATCH\rm.json" -Raw)))
# exit 0; removed_count 0; results[0].reason "View 8157 is not placed on sheet A102."

# error paths worth one drive each:
# place an already-placed view (312) -> exit 4, "cannot be placed" message
# get_sheet_viewports --sheet_id 99999999 -> exit 4, "Sheet with ID 99999999 not found."
# check_viewport_overlaps without --sheet_id -> client exit 1 "--sheet_id is required"
```

## Gotchas

- Placement coordinates are sheet mm (request length unit); the center
  echo after place/move is the strongest round-trip oracle.
- An already-placed view (e.g. Level 1 on A102 in the rac sample) is the
  natural negative case; pick an unplaced elevation for the happy path.
  `get_views` also lists ProjectBrowser/SystemBrowser/DrawingSheet rows —
  they fail with the same "cannot be placed" message.
- `get_sheet_viewports` count must equal `get_sheets.viewport_count` for
  that sheet.
- `Run` helpers that `return ,@($out,$code)` wrap the output; read the
  transcript file back when extracting ids, or the regex sees an array.
- Dialogs opened by `doc_open` can stall the whole journey — sweep, then
  re-drive; do not weaken the coordinates to "approximately".
