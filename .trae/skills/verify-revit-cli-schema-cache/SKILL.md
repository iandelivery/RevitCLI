---
name: verify-revit-cli-schema-cache
description: Model-check the revit-cli Go client's on-disk schema cache protocol — SchemaFetcher.Fetch in client/internal/client/discovery/fetcher.go driving SchemaCache (cache.go) — with TLA+/TLC, and replay each bug path against the real Go code. Use when a change touches fetcher.go or cache.go (Fetch, Load, LoadStale, Save, SaveEtag, Touch, cacheTTL) or their callers (main.go dispatchDynamic, builtin/commands.go commands + schema), when someone reports a stale command list after a bridge upgrade, "Cannot fetch command schema from bridge" while the bridge is up, or "commands --refresh" not refreshing, or when asked whether the schema cache is still verified.
---

# Verify the revit-cli schema cache (formal)

**Sync commit:** `24f817a3f702cfe90b925a553b8ebe66d68ed49f` (main, 2026-11-24). The model and
the replay were confirmed to describe the client code at that commit.

## What is verified

The client keeps two files per bridge under `<data dir>\cache`: the command list
(`schema-cache[_host_port].json`) and the bridge's ETag (`schema-etag[_host_port].txt`). The
promise the code makes (fetcher.go:99-107): *"a bridge upgrade produces a different ETag and the
conditional request returns 200 instead of 304"*, so a saved ETag may be trusted when the bridge
answers `304 Not Modified`.

The harm when it breaks: after a bridge upgrade the client keeps running on the old command list
— or reports "Cannot fetch command schema from bridge" while the bridge is up — and no command,
not even `commands --refresh`, repairs it, because every later request gets `304`.

Three rules, each in one sentence:

| Rule (TLC name) | Reader's sentence |
|---|---|
| `StampNeverAheadOfSchema` (invariant) | Between two runs, the saved ETag never describes a newer bridge than the saved command list. |
| `NotModifiedIsTruthful` (invariant) | When the bridge answers "not modified" and the client serves the list from disk, that list is the bridge's current list. |
| `CatchesUpAfterUpgrade` (temporal) | Once the bridge runs v2 and stays there, and the user keeps running commands, the client eventually runs on v2. |

Tool: **TLA+ / TLC** (tla2tools 1.7.4, Java 25) through the `statechart` skill's runner. The
question is whether two file writes and a server answer can interleave into a bad state across
process restarts, which is a model-checker question. No proof assistant or SMT verifier runs on
Go; the model is therefore paired with a Go replay test that runs the real `SchemaFetcher` and
`SchemaCache` and asserts the same two safety rules.

## Expected result at the sync commit

The check **fails, and that is the baseline**. Both findings below are open:

- `run.ps1` exits **1**.
- `replay.txt`: `TestVerify_NotModifiedNeverServesOldList` and
  `TestVerify_SavedStampWithoutListStillFetches` both FAIL.
- `chart\report.md`: `StampNeverAheadOfSchema`, `NotModifiedIsTruthful`,
  `CatchesUpAfterUpgrade` break; `NoDeadlock` holds; TLC reports 213 states.

Anything else is news: a run that exits 0 means someone fixed the code (re-read the findings,
confirm the fix on the real code, then move the sync commit forward per "After a change"); a
run that exits 2, a fourth broken rule, or a replay compile error is drift or a new bug.

## Run

From the repo root (one command):

```powershell
pwsh -NoProfile -File .trae\skills\verify-revit-cli-schema-cache\run.ps1
```

Exit code: `0` property holds on both the model and the real code; `1` property broken (read
`replay.txt` and `chart\report.md` to see which finding); `2` the check could not run (the
line after `COULD NOT RUN:` says why). Evidence lands in
`dist\verify-evidence\schema-cache\<stamp>\` (`replay.txt`, `statechart.txt`, `chart\`), which is
gitignored. Pass `-Stamp <name>` to name the folder, `-SkipReplay` / `-SkipModel` to run one half.

Prerequisites:

- Go (the repo's `client\go.mod`) and Node 22.18 or later (the `statechart` runner executes TypeScript directly).
- TLC and Java. `run.ps1` uses `TLA2TOOLS` + `JAVA` when both are set, else the pinned tools
  under `%LOCALAPPDATA%\revit-cli-verify\tools\tools.json`, else lets `statechart` resolve them
  through `mise`. To install the pinned pair once (tla2tools 1.7.4, sha256-checked; Microsoft
  OpenJDK 25.0.5.1; GitHub fetched through the proxy discovered per the `proxy-guide` skill):

  ```powershell
  pwsh -NoProfile -File .trae\skills\verify-revit-cli-schema-cache\bootstrap-tools.ps1
  ```

- Run `run.ps1` outside a sandbox that redirects deletions to a recycle bin: the `statechart`
  runner deletes its own temp folder at exit and aborts with `ENOTEMPTY` when the delete is
  virtualised, before it writes the report.

What `run.ps1` does, in order: copies `replay\verify_schema_cache_replay_test.go` into
`client\internal\client\discovery\`, runs `go test -run ^TestVerify_`, deletes the copy and
fails with exit 2 if `git status -- client` is not clean afterwards; then runs
`statechart` on `SchemaCache.tla` with `view.ts` and writes `chart\` (`report.md`, `chart.mmd`,
`tlc.out`, `tlc-<Rule>.out`).

## Read the result

1. **Replay (real code).** `replay.txt` is a plain `go test -v` transcript. A `--- FAIL` line
   names the step where the real `SchemaFetcher` served the wrong list. The one log line
   `[cache] cannot write temp cache: ... is a directory` is the harness making the list write
   fail (a directory parked at `schema-cache.json.tmp`); it stands for a full disk or a file
   lock on Windows.
2. **Model.** Read `chart\report.md` and `chart\tlc-<Rule>.out` with the `statechart` skill's
   `/statechart` reader. It shows each broken rule as its shortest path in plain words and draws
   `chart.mmd`. Refer to `/statechart` by name; its options and paths change between releases,
   so nothing about it is copied here.
3. **Decide bug or modeling error.** Walk the TLC path against the code, one step per action,
   using the table below. If an action's precondition cannot happen in the code at the sync
   commit, the model is wrong: fix the model, say what was wrong, and rerun. If every step can
   happen, it is a bug in the code: do not weaken a rule or the model to make it pass; report
   it with the path. The replay test is the shortcut: if TLC breaks a rule and the replay
   fails on the same path, the finding is on the real code.

## Rules and the code they cover

`SchemaCache.tla` has one action per indivisible step in the code. `vars` are: `server`
(the bridge's list, `v1` or `v2`, ETag derived from it as the bridge does), `diskSchema`,
`diskEtag`, `fresh` (mtime within `cacheTTL`), `phase` (`idle`/`asking`/`got200`/`wroteEtag`),
`served`, `servedVia` (`ttl`/`200`/`304`/`unreachable`), `servedStale` (recorded at serve time).

| Model action | Code at the sync commit |
|---|---|
| `ServeTtl` — within the window, `Fetch(false)` returns the disk copy without asking | `fetcher.go:109-113` → `cache.Load()` `cache.go:55-81` (TTL check `cache.go:68`, `cacheTTL` `cache.go:28`) |
| `StartRequest` — `Fetch(true)`, or `Fetch(false)` past the window | `fetcher.go:115-127`: builds `GET /api/commands`, `If-None-Match` from `lastEtag` or `cache.LoadEtag()` (`cache.go:128-137`) |
| `Answer200` — bridge answers 200 with its list + ETag | `fetcher.go:129` (`client.Do`), `148-159` |
| `Answer304` — bridge answers 304; client `Touch()`es and `Load()`s the disk copy | `fetcher.go:140-146` → `cache.Touch()` `cache.go:155-160` (Chtimes; error ignored) |
| `AnswerUnreachable` — network error, 401, other status, bad body → `LoadStale()` | `fetcher.go:117, 131, 137, 151, 155, 169` → `cache.go:85-98` |
| `SaveEtagOk` / `SaveEtagFail` — `cache.SaveEtag(etag)`; failure is logged, not returned | `fetcher.go:160-163` → `cache.go:140-151` |
| `SaveSchemaOk` / `SaveSchemaFail` — `cache.Save(&schema)`; failure is logged, not returned | `fetcher.go:165` → `cache.go:102-125` (tmp write `117`, rename `121`) |
| `ProcessDies` — the one-shot client exits between the two writes (Ctrl-C, crash, OOM) | the gap between `fetcher.go:162` and `fetcher.go:165` |
| `BridgeUpgrade` — bridge moves to the next version with a new ETag | the bridge recomputes its ETag from its in-memory registry per request (bridge side; not client code) |
| `TimePasses` — 30 minutes elapse | `time.Since(info.ModTime()) > cacheTTL` at `cache.go:68` |

Fairness (`Fair`, strong fairness): the user keeps running commands, windows keep expiring,
the bridge keeps answering real 200/304 responses when asked (unreachable answers may
interleave), and every run ends. `CatchesUpAfterUpgrade` is checked under these assumptions.

Callers that reach the covered code: `cmd/revit-cli/main.go:284` (`dispatchDynamic`: alias
resolution and the fallback when `/api/commands/{name}` is unavailable),
`internal/client/builtin/commands.go:34` (`commands [--refresh]`), `commands.go:83`
(`schema <name>` fallback). `FetchCommand` (`fetcher.go:53-97`) bypasses the disk cache and is
not covered.

## Bounds and assumptions

- Two bridge versions, `v1` then `v2`, upgrades only (no downgrade). One bridge, one server
  key, one client process at a time; each run is a fresh process (`lastEtag` empty, as in the
  one-shot CLI). Disk starts empty.
- Each of the two writes either succeeds or fails as a whole; a torn ETag file (partial
  write) is not modelled — a wrong stamp only causes a 200 next time, so it cannot hide a bug.
- The bridge always sends an ETag and computes it from the list it serves; a 304 means the
  `If-None-Match` equals that ETag. Files unmarshal when they exist.
- Not covered: concurrent client processes writing the same cache files, `FetchCommand`'s
  in-process cache, `schema-etag` truncation, bridge-side ETag computation, and `DataDir`
  resolution.
- A pass covers this model and these bounds, not the whole client.

## Findings at the sync commit

Both have existed since the client's first commit `33d3b77` (2026-06-22).

**Finding 1 — the ETag is saved before the list it belongs to.** `fetcher.go:160-165` writes
`schema-etag*.txt` (line 162) and then `schema-cache*.json` (line 165). If the second write fails
or the process ends in between, the disk holds the new bridge's stamp next to the old list (or no
list). Every later run sends that stamp, gets `304`, `Touch()`es the old file and serves it —
forever, because the window keeps being renewed and `Fetch(true)` still sends the stamp
(`fetcher.go:121-127`).

Path in the code's words (TLC, 9 steps; replay `TestVerify_NotModifiedNeverServesOldList`):
you run commands on bridge v1 (list + v1 stamp saved) → the bridge upgrades to v2 → you run a
command, the window has expired, the client asks with the v1 stamp → the bridge answers 200 with
the v2 list + v2 stamp → the client writes the stamp file → writing the list file fails (disk
full, or the rename is refused because another process has `schema-cache*.json` open) → the
client returns the v2 list for this run only → next run: the client asks with the v2 stamp →
`304 Not Modified` → the client serves the v1 list and renews the window → repeat forever.
`commands --refresh` takes the same 304 path.

What the user sees: `revit-cli commands` lists the old commands with the old bridge version,
`revit-cli <alias>` runs with the old parameter set, `revit-cli schema <name>` prints the old
schema (when the per-command endpoint is unavailable), and `--refresh` changes nothing.

**Finding 2 — a stamp without a list is served as "nothing" forever.** Same write order, but
the list file never existed (first-ever run, list write fails): the disk holds a stamp and no
list. Next run: `304` → `Touch()` fails (no file) → `Load()` nil → `LoadStale()` nil →
`Fetch` returns nil while the bridge is up. `dispatchDynamic` prints `Unknown command` (exit 1)
and `commands` prints `Cannot fetch command schema from bridge.` (exit 1). This state is also
what a user upgrading from the current client may already have on disk; a fix that only reorders
the writes prevents new occurrences but does not recover from this one.

Path (TLC, 6 steps; replay `TestVerify_SavedStampWithoutListStillFetches`): first run → 200 →
stamp written → the client exits before writing the list → next run → 304 → the client serves
nothing.

Model drill (2026-11-24): a scratch copy of the model with the writes reordered and the stamp
written only after a successful list save satisfies all three rules (TLC exit 0, 153 states);
the shipped model breaks them (TLC exit 12 on the first invariant; all three via `run.ps1`).
Real-code drill: a scratch change making `Save` return an error and writing the stamp only on
success makes `TestVerify_NotModifiedNeverServesOldList` pass and leaves
`TestVerify_SavedStampWithoutListStillFetches` failing (Finding 2); reverting the change
restores the baseline. The product code was not changed.

## Does a change touch the verified code?

```powershell
git diff --stat 24f817a3f702cfe90b925a553b8ebe66d68ed49f -- client/internal/client/discovery/fetcher.go client/internal/client/discovery/cache.go client/internal/models/schema.go client/cmd/revit-cli/main.go client/internal/client/builtin/commands.go
```

A non-empty result for `fetcher.go` or `cache.go` means the verification needs another look
before it can be trusted on the new commit. A change only to doc comments or to `FetchCommand`
has no effect on the model; say so by checking against the action table, not the commit message.

## After a change

1. Run `run.ps1` as it is and record the exit code and which rules/tests fail.
2. Diff the covered files from the sync commit and classify each hunk against the action
   table: no effect, the model no longer describes the code (a step split or merged, a branch
   added or removed, a new path to the guarded state), or the promise changed on purpose (cite
   the doc comment, test, or commit that says so).
3. Update `SchemaCache.tla` from the code, never from the result. Keep one indivisible step in
   the code as one action. Update the action table and the replay test to the new code.
4. Show the updated check can still fail: break the code the way the rules guard against
   (for example move the stamp write back before the list write in a scratch copy of the model
   and in a scratch edit of `fetcher.go`), confirm the rule or test breaks, then drop the break
   and confirm `git status -- client` is clean.
5. Move the sync commit forward only after confirming the model and the replay describe the
   code at the new commit. If the findings are fixed, rewrite "Expected result" and
   "Findings" from what the run shows; if they are still open, keep them.
6. Put verification changes in their own PR, never mixed with product changes.

For changes that skipped this skill (merges, refactors), use `/maintain-formal-verification-skill`.

## Files

- `SchemaCache.tla` — the model (`Init`, `Next`, the three rules, `Fair`).
- `SchemaCache.cfg` — `SPECIFICATION Spec`, the two invariants, the one property.
- `view.ts` — the reader's words for `statechart`: states and arrows. It hides three kinds of
  steps from the chart: within-window serves (`ServeTtl`), time passing (`TimePasses`), and
  upgrades that land in the middle of a run (`BridgeUpgrade` from a non-idle phase). The rules
  are checked on all of them.
- `replay/verify_schema_cache_replay_test.go` — the Go replay; copied into the client package
  by `run.ps1`, never committed there.
- `run.ps1`, `bootstrap-tools.ps1` — the runner and the pinned tool installer.
