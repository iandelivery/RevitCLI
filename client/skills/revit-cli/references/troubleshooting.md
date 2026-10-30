# Troubleshooting Quick Reference

Symptom-first table for bridge and CLI failures, consolidated from production runs (Revit 2020 + bridge). Scan this table before debugging blind.

## Fault Quick Reference

| Symptom | Cause | Fix |
|---|---|---|
| `ping` fails / no connection | Bridge not running: add-in not loaded or AI Mode toggled off | `revit-cli.exe list` to see registered instances; check the Ribbon toggle button; verify port range for the Revit version (2020 → 5021-5029); target explicitly with `--pid`/`--revit`/`--url` |
| Connection lost mid-session | Revit restarted → bridge re-registered on a new port | Re-run `revit-cli.exe list` and reconnect; raw mode also resets to disabled after restart |
| HTTP 401 / raw execution rejected | Raw mode is disabled (default, security) | `revit-cli.exe raw_mode --enable`; note it is in-memory only — Revit restart resets it to `allow_raw_execution` in `cli_bridge_setting.json` |
| `Invalid JSON` from the bridge | `\r` (CRLF) or non-ASCII characters leaked into the payload | Enforce the ASCII + LF asserts BEFORE writing/sending the script file (constraints 1-2 in csharp-scripting.md); regenerate the file |
| Compile errors cascade (one fix → next error) | Only the FIRST compiler error is real; the rest are fallout of the first | Read the first error only; usual roots: duplicate/unknown `using`, non-ASCII token, malformed char-code expression |
| Garbled Chinese strings in Revit (乱码) | Chinese literals sent raw in an ASCII-only channel | Never inline Chinese; build strings with `(char)0xXXXX` expressions (`csexpr` helper) |
| Delete-heavy script hangs (deletion deadlock) | Active plan view participates in the deletion dependency graph | Switch the active view to {3D} before running deletion scripts (constraint 11) |
| Task times out (`timeout_seconds`, default 180) | Batch too large for one run | Split the batch; poll long tasks with `revit-cli.exe task -ti <id>`; watch `event: progress` SSE updates |
| Element created but invisible in plan views | Reference level not set / offset semantics | Pass the correct `Level` to `NewFamilyInstance`; verify with `INSTANCE_REFERENCE_LEVEL_PARAM`; offsets are relative to the curve, not absolute z |
| `The name is already in use for this element type` | `Duplicate()` collided with an existing type name | Search existing types by parameter values (e.g. width/height with 0.003 ft tolerance) first; only `Duplicate` when not found |
| Residual offsets after creation (+2200/+3000) | Created with family default height: top constraint and base/top offsets not fully set | Set top constraint AND zero both base/top offsets immediately after create; audit with a full offset sweep (|offset| > 0.5 mm → reset) |
| "Fixed" script still reports the same error | Stale payload file: assertion fired after the file write, so the old file was sent | Re-order generator: assert ASCII/LF BEFORE `json.dump`/write (constraint 10); delete the stale output and regenerate |
| Edits succeed on a "read-only" document | Bridge writes are not blocked by the read-only flag | Verify the target document before any write session; confirm before save (safety rules in SKILL.md) |

## Escalation Path

1. `revit-cli.exe status` / `health` — bridge-side state
2. `revit-cli.exe task` — recent task results and failures
3. `revit-cli.exe commands --refresh` — schema cache may be stale after a bridge update
4. Upstream protocol docs: `bridge/docs/README.md` in https://github.com/iandelivery/RevitCLI
