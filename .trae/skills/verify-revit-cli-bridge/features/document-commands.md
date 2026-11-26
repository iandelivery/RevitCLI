# Document commands (open, read, mutate, undo, dry-run)

## Sub-features

- Document lifecycle — `doc_open` (validates path/extension, refuses while
  the active document has an **open transaction** — `IsModifiable`; unsaved
  changes alone do not block), `doc_list`, `document_info`, `doc_close`
  (refuses to close the active document; `--all` closes the others but
  skips linked documents).
- Read — `get_levels` (id/name/elevation), `get_elements --category`
  (params exactly `category`, `link`, `limit`, `offset`; paginated:
  count/limit/offset/has_more, default limit 500, clamped at 5000, no
  total, no id filter), `get_element_by_id --element_id [--link]`,
  `get_element_boxes`, `search_elements`.
- Mutate — `create_wall` (`start_x/start_y/end_x/end_y/level_id`, optional
  `height`, unit-converted), plus the creation/modification families. All
  id reads/writes go through `ElementIdCompat` (`FromValue`/`ValueOf`).
- `undo` — posts Revit's Undo command; reports
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

Run "levels" ($U + @("get_levels","--json"))         # data.levels[0].id (e.g. 311)
Run "wall" ($U + @("create_wall","--start_x","0","--start_y","0",
                   "--end_x","5000","--end_y","0","--level_id","<LEVEL_ID>"))
# exit 0; data.element_id is the new wall
Run "element" ($U + @("get_element_by_id","--element_id","<WALL_ID>"))    # exit 0, Walls data
Run "undo"   ($U + @("undo"))                       # "steps_executed": 1
Run "element-gone" ($U + @("get_element_by_id","--element-id","<WALL_ID>"))
# exit 4 + "Element with ID <id> not found." — AND the error JSON still carries the units echo

Run "dry-wall" ($U + @("create_wall",...,"--dry-run"))   # success, dry_run:true, data.element_id
Run "dry-requery" ($U + @("get_element_by_id","--element_id","<DRY_WALL_ID>"))
# exit 4 not found — the rollback is real

Run "close-active" ($U + @("doc_close"))             # exit 4: the active document is never closed
Run "elements-1" ($U + @("get_elements","--category","OST_Levels","--limit","1"))   # count 1, has_more true
```

## Gotchas

- **PRODUCT GAP (live-proven 2026-11-22 and again 2026-11-26, unfixed): `undo --count N`
  is silently ignored** — the schema declares `count` but the handler reads `steps`;
  `undo --count 2` over two fresh walls reports `steps_requested: 1`,
  `steps_executed: 1` and leaves the second wall (proven by `get_element_by_id`).
  `steps_executed` counts accepted `PostCommand` calls, not completed undos. Drive undo
  one step at a time.
- **PRODUCT GAP (live-proven 2026-11-22, unfixed): `create_wall --height`
  is echoed but never applied** — `Wall.Create(doc, line, levelId, false)` never reads
  `p.Height`; walls with height 2000 vs the 3000 default come out identical
  (min_z/max_z equal). Treat `height` as decorative until fixed.
- The client accepts both `--element_id` and `--element-id` (underscore/dash
  aliases); `get_elements` has **no** id filter — use `get_element_by_id` for
  element-level proof.
- Undo proofs must be element-level: `get_elements` wall counts flatline
  at the 500 page size on sample models.
- The sample model is never saved: mutations ride the session and vanish
  when our Revit exits; never call `doc_save`.
- A Revit UI dialog (missing links, template warning) blocks the main
  thread and looks like a timeout — sweep dialogs first, then re-drive.
