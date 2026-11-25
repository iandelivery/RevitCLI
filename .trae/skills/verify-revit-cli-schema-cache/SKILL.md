---
name: verify-revit-cli-schema-cache
description: Model-check the revit-cli Go client's on-disk schema cache protocol — SchemaFetcher.Fetch in client/internal/client/discovery/fetcher.go driving SchemaCache (cache.go) — with TLA+/TLC, and replay each bug path against the real Go code. Use when a change touches fetcher.go or cache.go (Fetch, Load, LoadStale, Save, SaveEtag, LoadEtagForList, Touch, cacheTTL) or their callers (main.go dispatchDynamic, builtin/commands.go commands + schema), when someone reports a stale command list after a bridge upgrade, "Cannot fetch command schema from bridge" while the bridge is up, or "commands --refresh" not refreshing, or when asked whether the schema cache is still verified.
---

# Verify the revit-cli schema cache (formal)

**Sync commit:** `4211d29` (main, 2026-11-25; merge of #117 `fix(client): save the schema cache
list before its ETag`). The model and the replay were confirmed to describe the client code at
that commit. Previous sync: `24f817a` (2026-11-24, before the fix).

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
| `ClientKeepsStampInStep` (action property on `StampNeverAheadOfSchema`) | The client never leaves a saved ETag that describes a newer bridge than the saved command list. |
| `NotModifiedIsTruthful` (invariant) | When the bridge answers "not modified" and the client serves the list from disk, that list is the bridge's current list. |
| `CatchesUpAfterUpgrade` (temporal) | Once the bridge runs v2 and stays there, and the user keeps running commands, the client eventually runs on v2. |

Tool: **TLA+ / TLC** (tla2tools 1.7.4, Java 25) through the `statechart` skill's runner. The
question is whether two file writes and a server answer can interleave into a bad state across
process restarts, which is a model-checker question. No proof assistant or SMT verifier runs on
Go; the model is therefore paired with a Go replay test that runs the real `SchemaFetcher` and
`SchemaCache` and asserts the same two safety rules.

## Expected result at the sync commit

The check **passes**:

- `run.ps1` exits **0**.
- `replay.txt`: `TestVerify_NotModifiedNeverServesOldList` and
  `TestVerify_LegacyStampWithoutListRecovers` both PASS.
- `chart\report.md`: every rule holds (`ClientKeepsStampInStep`, `NotModifiedIsTruthful`,
  `NoDeadlock`, `CatchesUpAfterUpgrade`); TLC reports 172 states from 2 initial states.

Anything else is news: exit 1 means a change reintroduced one of the fixed paths (read
`replay.txt` and `chart\report.md`, walk the path against the action table, then decide bug or
modeling error); exit 2 means the check could not run (the line after `COULD NOT RUN:` says
why; a replay compile error means `fetcher.go` or `cache.go` changed shape and the replay needs
updating per "After a change").

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
   names the step where the real `SchemaFetcher` served the wrong list. In
   `TestVerify_NotModifiedNeverServesOldList`, `Save` now returns its error, so the harness
   makes the list write fail with a directory parked at `schema-cache.json.tmp` and no
   `[cache]` log line appears; it stands for a full disk or a file lock on Windows.
2. **Model.** Read `chart\report.md` and `chart\tlc-<Rule>.out` with the `statechart` skill's
   `/statechart` reader. It shows each broken rule as its shortest path in plain words and draws
   `chart.mmd`. Refer to `/statechart` by name; its options and paths change between releases,
   so nothing about it is copied here.
3. **Decide bug or modeling error.** Walk the TLC path against the code, one step per action,
   using the table below. If an action's precondition cannot happen in the code at the sync
   commit, the model is wrong: fix the model, say what was wrong, and rerun. If every step can
   happen, it is a bug in the code: do not weaken a rule or the model to make it pass; report
   it with the path. The replay test is the shortcut: if TLC breaks a rule and the replay
   fails on the same path, the finding is on the real code. The reverse is not proof: a path
   that starts at the legacy disk (`LegacyDisk`) has no replay on the current code, because the
   current client cannot produce that state; `TestVerify_LegacyStampWithoutListRecovers` seeds
   it by hand instead.

## Rules and the code they cover

`SchemaCache.tla` has one action per indivisible step in the code. `vars` are: `server`
(the bridge's list, `v1` or `v2`, ETag derived from it as the bridge does), `diskSchema`,
`diskEtag`, `fresh` (mtime within `cacheTTL`), `phase` (`idle`/`asking`/`got200`/`wroteSchema`),
`served`, `servedVia` (`ttl`/`200`/`304`/`unreachable`), `servedStale` (recorded at serve time).
`SentEtag` is the stamp the client sends: `diskEtag` when a list exists, nothing otherwise.

| Model action | Code at the sync commit |
|---|---|
| `Init` (fresh disk) | a new `REVIT_CLI_DATA_DIR`: no list, no stamp |
| `Init` (`LegacyDisk`) | a disk written by the pre-`d104189` client: a stamp and no list. Not reachable from the current code; see Bounds |
| `ServeTtl` — within the window, `Fetch(false)` returns the disk copy without asking | `fetcher.go:109-113` → `cache.Load()` `cache.go:55-81` (TTL check `cache.go:68`, `cacheTTL` `cache.go:28`) |
| `StartRequest` — `Fetch(true)`, or `Fetch(false)` past the window; sends `If-None-Match` only when `SentEtag # None` | `fetcher.go:115-127`: `etag = lastEtag` or `cache.LoadEtagForList()` (`cache.go:137-146`, `os.Stat` gate) |
| `Answer200` — bridge answers 200 because `SentEtag` is absent or differs from its ETag | `fetcher.go:129` (`client.Do`), `148-157` |
| `Answer304` — bridge answers 304; client `Touch()`es and `Load()`s the disk copy | `fetcher.go:140-146` → `cache.Touch()` `cache.go:171-176` (Chtimes; error ignored) |
| `AnswerUnreachable` — network error, 401, other status, bad body → `LoadStale()` | `fetcher.go:117, 131, 137, 151, 155, 168` → `cache.go:85-98` |
| `SaveSchemaOk` — `cache.Save(&schema)` returns nil; list on disk, window fresh | `fetcher.go:160` → `cache.go:103-124` (tmp write `116`, rename `119`) |
| `SaveSchemaFail` — `cache.Save` returns an error; the stamp is not touched | `fetcher.go:160` (the `err == nil` guard fails, so `SaveEtag` is skipped) |
| `SaveEtagOk` / `SaveEtagFail` — `cache.SaveEtag(etag)`; failure is logged, not returned | `fetcher.go:161-162` → `cache.go:156-168` |
| `ProcessDies` — the one-shot client exits after the list write and before the stamp write (Ctrl-C, crash, OOM), or before either | the gap between `fetcher.go:160` and `fetcher.go:162` |
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
  one-shot CLI). Disk starts either empty or as `LegacyDisk`.
- `LegacyDisk` is an initial state the current client cannot produce: a stamp and no list. It
  stands for a disk the pre-`d104189` client left behind, which a user carries across an
  upgrade. The stamp rule is an action property (`ClientKeepsStampInStep`) rather than an
  invariant so that this inherited state does not count as the client's doing; every state the
  client reaches through `Next` must satisfy it. `NotModifiedIsTruthful` is an invariant and
  does apply to the legacy disk: if the client sends the legacy stamp and gets a 304, that
  breaks the rule, which is exactly what `LoadEtagForList` prevents.
- Each of the two writes either succeeds or fails as a whole; a torn ETag file (partial
  write) is not modelled — a wrong stamp only causes a 200 next time, so it cannot hide a bug.
- The bridge always sends an ETag and computes it from the list it serves; a 304 means the
  `If-None-Match` equals that ETag. Files unmarshal when they exist.
- Not covered: concurrent client processes writing the same cache files, `FetchCommand`'s
  in-process cache, `schema-etag` truncation, bridge-side ETag computation, and `DataDir`
  resolution.
- A pass covers this model and these bounds, not the whole client.

## Findings, and the fix that closed them

Both findings existed from the client's first commit `33d3b77` (2026-06-22) until `d104189`
(2026-11-25, PR #117). They are closed at the sync commit. The record stays so the next agent
can recognise a reintroduction.

**Finding 1 — the ETag was saved before the list it belonged to.** `Fetch` wrote
`schema-etag*.txt`, then `schema-cache*.json`, and logged away both failures. If the list write
failed or the process died in between, the disk held the new stamp next to the old list. Every
later run sent that stamp, got `304`, served the old list, and renewed the window, so
`Fetch(false)` stopped asking and `Fetch(true)` got the same `304`. Reintroduction path (TLC, 9
steps; replay `TestVerify_NotModifiedNeverServesOldList`): v1 list + v1 stamp on disk → bridge
upgrades to v2 → run → 200 with the v2 list → the stamp is written → the list write fails →
the disk now holds v2 stamp + v1 list → run → 304 serves the v1 list → forever. The fix holds
because the stamp write runs only after the list write succeeds.

**Finding 2 — a stamp without a list was served as nothing.** Same write order on a first run
whose list write failed: stamp on disk, no list. Next run: `304` → `Touch()` failed → `Load()`
nil → `LoadStale()` nil → `Fetch` nil while the bridge was up (`Unknown command`, or
`Cannot fetch command schema from bridge.`, exit 1). The fix closes the production path (the
stamp is only written after the list) and the inherited path (`LoadEtagForList` withholds the
stamp when no list exists, so the next run gets a 200 and writes both files). Reintroduction
path on the model (3 steps from `LegacyDisk`; replay `TestVerify_LegacyStampWithoutListRecovers`
seeds the same state): legacy disk → run → 304 serves "no list" → `NotModifiedIsTruthful`
breaks.

Drills at this sync (2026-11-25), all reverted, `git status -- client` clean after each:

- Model: the shipped model passes (172 states). A scratch copy with `SentEtag == diskEtag`
  (the `LoadEtagForList` gate removed) breaks `NotModifiedIsTruthful` in 3 steps from
  `LegacyDisk`.
- Real code: a scratch edit of `fetcher.go` restoring the old order (stamp first, list after,
  error discarded) makes `TestVerify_NotModifiedNeverServesOldList` and
  `TestFetch_ListSaveFailureKeepsOldPair` fail with `served "old"`. A scratch edit replacing
  `LoadEtagForList()` with `LoadEtag()` does **not** make any Go test fail, because no Go test
  can produce the legacy state; only the model drill above catches that break. This is why the
  model is kept even though the replay covers the production paths.

## Does a change touch the verified code?

```powershell
git diff --stat 4211d29 -- client/internal/client/discovery/fetcher.go client/internal/client/discovery/cache.go client/internal/models/schema.go client/cmd/revit-cli/main.go client/internal/client/builtin/commands.go
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
4. Show the updated check can still fail, once on each side: a scratch copy of the model with a
   guard removed ( TLC must break a rule), and a scratch edit of `fetcher.go` that reintroduces
   the old write order (the replay must fail). Drop both breaks and confirm
   `git status -- client` is clean. A pass that survived both breaks is a description that has
   lost the ability to fail; find out why before moving the sync commit.
5. Move the sync commit forward only after confirming the model and the replay describe the
   code at the new commit. If a rule breaks on the real code, keep the sync commit where it is
   and report the finding instead.
6. Put verification changes in their own PR, never mixed with product changes.

For changes that skipped this skill (merges, refactors), use `/maintain-formal-verification-skill`.

## Files

- `SchemaCache.tla` — the model (`Init` with `LegacyDisk`, `Next`, the three rules, `Fair`).
- `SchemaCache.cfg` — `SPECIFICATION Spec`, one invariant (`NotModifiedIsTruthful`), two
  properties (`ClientKeepsStampInStep`, `CatchesUpAfterUpgrade`).
- `view.ts` — the reader's words for `statechart`: states and arrows. It hides three kinds of
  steps from the chart: within-window serves (`ServeTtl`), time passing (`TimePasses`), and
  upgrades that land in the middle of a run (`BridgeUpgrade` from a non-idle phase). The rules
  are checked on all of them.
- `replay/verify_schema_cache_replay_test.go` — the Go replay; copied into the client package
  by `run.ps1`, never committed there.
- `run.ps1`, `bootstrap-tools.ps1` — the runner and the pinned tool installer.
