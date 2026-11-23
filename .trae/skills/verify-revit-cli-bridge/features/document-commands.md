# Document commands (open, read, mutate, undo, dry-run)

## Sub-features

- Document lifecycle — `doc_open` (validates path/extension, refuses
  while the active doc has uncommitted transactions), `doc_list`,
  `document_info`, `doc_close` (cannot close the active document).
- Read — `get_levels` (id/name/elevation), `get_elements --category`
  (paginated: count/limit/offset/has_more, default limit 500, no total),
  `get_element_by_id --element_id`, `get_element_boxes`, `search_elements`.
- Mutate — `create_wall` (`start_x/start_y/end_x/end_y/level_id`, optional
  `height`, unit-converted), plus the creation/modification families.
- `undo` — reverts the last transaction; reports
  `{steps_executed, steps_requested}`.
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
Run "undo"   ($U + @("undo"))                       # "steps_executed": 1
Run "element-gone" ($U + @("get_element_by_id","--element-id","<WALL_ID>"))
# exit 4 + "Element with ID <id> not found." — AND the error JSON still carries the units echo

Run "dry-wall" ($U + @("create_wall",...,"--dry-run"))   # success, dry_run:true
Run "dry-requery" ($U + @("get_element_by_id","--element_id","<DRY_WALL_ID>"))
# exit 4 not found — the rollback is real
```

## Gotchas

- **PRODUCT GAP (live-proven 2026-11-22, unfixed): `undo --count N` is
  silently ignored** — the schema declares `count` but the handler reads
  `steps`; `undo --count 2` over two fresh walls reports
  `steps_executed: 1` and leaves one wall. Drive undo one step at a time.
- **PRODUCT GAP (live-proven 2026-11-22, unfixed): `create_wall --height`
  is echoed but never applied** — walls with height 2000 vs the 3000
  default come out identical (min_z/max_z equal). Treat `height` as
  decorative until fixed.
- Undo proofs must be element-level: `get_elements` wall counts flatline
  at the 500 page size on sample models.
- The sample model is never saved: mutations ride the session and vanish
  when our Revit exits; never call `doc_save`.
- A Revit UI dialog (missing links, template warning) blocks the main
  thread and looks like a timeout — sweep dialogs first, then re-drive.
