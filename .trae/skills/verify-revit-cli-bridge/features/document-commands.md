# Document commands (open, read, mutate, undo, dry-run)

## Sub-features

- Document lifecycle — `doc_open` (validates path/extension, refuses
  while the active doc has uncommitted transactions), `doc_list`,
  `document_info`, `doc_close` (cannot close the active document).
- Read — `get_levels` (id/name/elevation), `get_elements --category`
  (paginated: count/limit/offset/has_more, default limit 500, no total),
  `get_element_by_id --element_id`, `get_element_boxes`, `search_elements`.
- Mutate — `create_wall` (`start_x/start_y/end_x/end_y/level_id`, optional
  `height`, unit-converted and applied to `WALL_USER_HEIGHT_PARAM` inside
  the transaction). Omitted `height` → the wall type height (4000 in the
  rac sample, not 3000: the schema carries no default). The response
  echoes the height read back from the wall, in request units.
- `undo` — posts Revit's Undo command; reports
  `{steps_requested, steps_posted}`. Revit accepts ONE queued undo per
  call (`PostCommand` throws on the second), so `undo --count 2` posts 1
  and says so in the message; repeat the command to undo more.
- `--dry-run` — `DryRunTransaction` executes then rolls back; the
  response says dry-run and the element must NOT exist afterwards.

## How to get to it (user POV)

The core value loop: open a model, read it, modify it, undo mistakes —
proven by element-level state, not counts.

## Driving it with the revit-cli client

```powershell
Run "doc-open" ($U + @("doc_open","--path","$SCRATCH\model.rvt"))
# doc_open can pop the "Default Family Template File Invalid" dialog ->
# run the dialog sweep right after, before concluding a timeout.

Run "levels" ($U + @("get_levels"))                 # e.g. Level 1 -> 311
Run "wall" ($U + @("create_wall","--start-x","0","--start-y","0",
                   "--end-x","5000","--end-y","0","--level-id","<LEVEL_ID>"))
# exit 0; capture element_id from the result
Run "element" ($U + @("get_element_by_id","--element_id","<WALL_ID>"))    # exit 0, Walls data
Run "undo"   ($U + @("undo"))                       # "steps_posted": 1
Run "element-gone" ($U + @("get_element_by_id","--element-id","<WALL_ID>"))
# exit 4 + "Element with ID <id> not found." — AND the error JSON still carries the units echo

Run "dry-wall" ($U + @("create_wall",...,"--dry-run"))   # success, dry_run:true
Run "dry-requery" ($U + @("get_element_by_id","--element_id","<DRY_WALL_ID>"))
# exit 4 not found — the rollback is real

# height applied (live 2026-11-26):
Run "wall-h2000" ($U + @("create_wall",...,"--height","2000"))
Run "box-h2000"  ($U + @("get_element_boxes","--element_ids","<ID>"))     # max_z 2000
Run "wall-noh"   ($U + @("create_wall",...))                               # no --height
Run "box-noh"    ($U + @("get_element_boxes","--element_ids","<ID>"))     # max_z 4000 (wall type)

# undo --count 2 over two fresh walls (live 2026-11-26):
Run "undo-2" ($U + @("undo","--count","2"))
# exit 0; data {steps_posted: 1, steps_requested: 2}; message
# "Undo posted: 1 of 2 step(s). Revit accepts one queued undo at a time; repeat the command to undo more steps."
Start-Sleep 2
Run "gone-2nd" ($U + @("get_element_by_id","--element_id","<SECOND_WALL>"))   # exit 4
Run "undo" ($U + @("undo"))                                                    # steps_posted 1 of 1
Start-Sleep 2
Run "gone-1st" ($U + @("get_element_by_id","--element_id","<FIRST_WALL>"))    # exit 4
Run "undo-0"  ($U + @("undo","--count","0"))    # exit 4 "Steps must be between 1 and 20."
Run "undo-21" ($U + @("undo","--count","21"))   # exit 4, same message
```

## Gotchas

- `steps_posted` counts accepted `PostCommand` calls, not completed undos;
  the handler returns before the Revit UI thread runs them. Sleep ~2 s
  before the element-level proof, and re-query once if it still says found.
- `steps` still works as a parameter name for `raw -j` callers, but the
  schema declares only `count`.
- The client injects schema defaults in REQUEST units from a 30-min cache;
  after a bridge upgrade run `commands --refresh` before trusting any
  default (a pre-fix cache still carries `height: 3000` for create_wall).
- Undo proofs must be element-level: `get_elements` wall counts flatline
  at the 500 page size on sample models.
- The sample model is never saved: mutations ride the session and vanish
  when our Revit exits; never call `doc_save`.
- A Revit UI dialog (missing links, template warning) blocks the main
  thread and looks like a timeout — sweep dialogs first, then re-drive.
