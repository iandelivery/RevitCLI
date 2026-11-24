---
name: verify-revit-cli-bridge-task-lifecycle
description: Model-check the RevitCliBridge task lifecycle promise — TaskStateMachine (bridge/RevitCliBridge/CliBridge/TaskStateMachine.cs) ending a TaskInfo from the Revit main thread (CliCommandHandler) while the HTTP thread's timeout (SseExecuteHandler, CliHttpServer) ends the same task — with TLA+/TLC, and replay each bug path against the real C# TaskStateMachine. Use when a change touches TaskStateMachine.cs, TaskInfo.cs, TaskRegistry.cs, CliCommandHandler.cs, SseExecuteHandler.cs, the SetTimeout call in CliHttpServer.cs, or the linked files in RevitCliBridge.Tests.csproj; when someone reports a command that changed the model but the client printed "Revit API execution timed out." (exit 3) or the reverse, GET /api/task/{id} disagreeing with what the client printed, a retried command running twice, two end frames on one SSE stream, or when asked whether the task lifecycle is still verified.
---

# Verify the bridge task lifecycle (formal)

**Sync commit:** `51e7d20b2b4776ced466030305033b4ea6db25e5` (main, 2026-11-24). The model and
the replay were confirmed to describe the bridge code at that commit.

## What is verified

The bridge runs one task per command. Two threads can end it: the Revit main thread
(`CliCommandHandler.Execute` → `TaskStateMachine.SetRunning` → `CommandRouter.Execute` →
`SetCompleted` or `SetFailed`) and the HTTP listener thread when the command's time budget
runs out (`SseExecuteHandler.HandleAsync` or `CliHttpServer.HandleExecuteRequestAsync` →
`TaskStateMachine.SetTimeout`). The promise the code makes (TaskStateMachine.cs:10-11, written
by commit `6143ff2` on 2026-11-17): *"Terminal states are final: the first terminal transition
wins and later transitions are ignored, so a timed-out task cannot flip back to completed."*

The harm when it breaks (the words of that commit): *"a retried command can run twice and
/api/task reports completed for a task the client saw fail."* A command whose Revit
transaction committed is reported to the user as timed out (client exit 3), an agent retries,
and the wall, pipe or sheet is created twice. Or the client prints success while
`GET /api/task/{id}` later says timeout, and a polling agent concludes the command failed.

Every transition keeps the promise the same way — `if (IsTerminal(task.Status)) return;` and
then `task.Status = ...` — and each of those is its own `lock (_lock)` section in `TaskInfo`
(TaskInfo.cs:28-32). Between the read and the write the other thread can read the same
non-terminal status, and both write.

Two rules, each in one sentence:

| Rule (TLC name) | Reader's sentence |
|---|---|
| `FirstTerminalWins` (invariant) | Once a task has ended, its status, its result and what the waiting client is handed all belong to that first outcome (or are still being written by it). |
| `OneOutcomeOnTheWire` (invariant) | The end-of-task frames on the client's event stream all say the same thing. |

Tool: **TLA+ / TLC** (tla2tools 1.7.4, Java 25) through the `statechart` skill's runner. The
question is whether two threads and a timer can interleave into a torn task record, which is a
model-checker question. No SMT verifier runs on C#; the model is therefore paired with a C#
replay that runs the real `TaskStateMachine` (linked into `RevitCliBridge.Tests`) from two
threads and asserts the same two rules.

## Expected result at the sync commit

The check **fails, and that is the baseline**. Both rules are broken on the real code:

- `run.ps1` exits **1**.
- `replay.txt`: `TestVerify_Sequential_FirstTerminalWins` PASS;
  `TestVerify_Concurrent_CompletionVsTimeout_KeepFirstTerminal` and
  `TestVerify_Concurrent_StartVsTimeout_TimedOutTaskStaysEnded` FAIL with
  `N of 20000 trials ended with a task record, a TCS result or SSE frames that disagree`
  (on a 16-core machine 4600 to 5500 and 1300 to 1400 across runs; the count depends on the
  machine, a non-zero count is the finding).
- `chart\report.md`: `FirstTerminalWins` breaks (shortest path 5 steps),
  `OneOutcomeOnTheWire` breaks (shortest path 12 steps); `NoDeadlock` holds; TLC reports
  312 states and 498 steps.

Anything else is news: a run that exits 0 means someone fixed the code (re-read the findings,
confirm the fix on the real code, then move the sync commit forward per "After a change"); the
sequential test failing means a guard was removed, not that the race was fixed; `INCONCLUSIVE`
in `replay.txt` (exit 2) means the machine could not overlap the two threads (one core, or
fully loaded) — rerun on an idle machine with two or more cores; exit 2 for any other reason,
a third broken rule, or a compile error is drift or a new bug.

## Run

From the repo root (one command):

```powershell
pwsh -NoProfile -File .trae\skills\verify-revit-cli-bridge-task-lifecycle\run.ps1
```

Exit code: `0` both rules hold on the model and on the real code; `1` a rule is broken (read
`replay.txt` and `chart\report.md` to see which finding); `2` the check could not run (the
line after `COULD NOT RUN:` says why). Evidence lands in
`dist\verify-evidence\bridge-task-lifecycle\<stamp>\` (`replay.txt`, `statechart.txt`,
`chart\`), which is gitignored. Pass `-Stamp <name>` to name the folder, `-SkipReplay` /
`-SkipModel` to run one half.

Prerequisites:

- The .NET SDK (`dotnet`; 10.0.301 was used) on Windows: the test project targets net48 and
  builds the real `TaskStateMachine.cs`, `TaskInfo.cs` and `TaskStatus.cs` as linked files.
- Two or more idle cores for the replay; with one core the two threads never overlap and the
  replay reports `INCONCLUSIVE` (exit 2).
- Node 22.18 or later (the `statechart` runner executes TypeScript directly).
- TLC and Java. `run.ps1` uses `TLA2TOOLS` + `JAVA` when both are set, else the pinned tools
  under `%LOCALAPPDATA%\revit-cli-verify\tools\tools.json`, else lets `statechart` resolve them
  through `mise`. To install the pinned pair once (tla2tools 1.7.4, sha256-checked; Microsoft
  OpenJDK 25.0.5.1; GitHub fetched through the proxy discovered per the `proxy-guide` skill):

  ```powershell
  pwsh -NoProfile -File .trae\skills\verify-revit-cli-bridge-task-lifecycle\bootstrap-tools.ps1
  ```

- Run `run.ps1` outside a sandbox that redirects deletions to a recycle bin: the `statechart`
  runner deletes its own temp folder at exit and aborts with `ENOTEMPTY` when the delete is
  virtualised, before it writes the report.

What `run.ps1` does, in order: copies `replay\TaskLifecycleReplayTests.cs` into
`bridge\RevitCliBridge.Tests\`, runs `dotnet test --filter TestVerify_`, deletes the copy and
fails with exit 2 if the replay left `bridge` dirtier than it found it (a scratch edit that was
already there is left alone, so a drill can run through this command); then runs `statechart` on
`TaskLifecycle.tla` with `view.ts` and writes `chart\` (`report.md`, `chart.mmd`, `tlc.out`).

## Read the result

1. **Replay (real code).** `replay.txt` is a plain `dotnet test` transcript with three tests.
   - `TestVerify_Sequential_FirstTerminalWins` calls the transitions one after another and
     checks the whole record (status, `ResultJson`, the TCS result, the frames). It asserts what
     `TaskStateMachineTests` already asserts, so it passes on the baseline and fails only when a
     guard is gone.
   - The two `TestVerify_Concurrent_*` tests release two threads on the same clock tick, one
     calling the Revit-side transition and one calling `SetTimeout`, 20000 times, and fail when
     any trial ends with a record whose parts disagree. The message prints up to five torn
     records as `status=.. result=.. tcs=.. wire=[..]` — read them as the TLC states below.
     The harness aligns the two calls to within one clock tick; in production the two threads
     are milliseconds apart, so the trial count proves the torn states are reachable, not how
     often they happen.
   - `tcs=` is what the sync JSON client (`CliHttpServer.cs:364`) and the SSE client's
     `WhenAny` see; `wire=` is the order of the end frames the SSE client reads (it acts on the
     first one, `sseclient.go:317-330`).
2. **Model.** Read `chart\report.md` and `chart\tlc.out` with the `statechart` skill's
   `/statechart` reader. It shows each broken rule as its shortest path in plain words and draws
   `chart.mmd` (232 chart states; the states on a bug path carry `bugPath`, every state that
   breaks a rule carries `breaks`). Refer to `/statechart` by name; its options and paths
   change between releases, so nothing about it is copied here. `tlc-<Rule>.out` appears only
   when the chart could not draw a rule's path.
3. **Decide bug or modeling error.** Walk the TLC path against the code, one step per action,
   using the table below. If an action's precondition cannot happen in the code at the sync
   commit, the model is wrong: fix the model, say what was wrong, and rerun. If every step can
   happen, it is a bug in the code: do not weaken a rule or the model to make it pass; report
   it with the path. The replay is the shortcut: if TLC breaks a rule and a replay sample shows
   the same record, the finding is on the real code.

## Rules and the code they cover

`TaskLifecycle.tla` has one action per indivisible step in the code. `vars` are: `status`
(`TaskInfo._status`: pending/running/completed/failed/timeout), `result` (`ResultJson`:
none/the ok result/the error result/the timeout error), `tcs` (the `TaskCompletionSource`
result), `winner` (which thread wrote the first terminal status), `outcome` (what the Revit
handler returned: ok or fail), `revitPc`, `httpPc`, `stream` (the end frames sent, in order).

| Model action | Code at the sync commit |
|---|---|
| `RevitStartCheck` — read the status; terminal → skip the command (`exit`) | `CliCommandHandler.cs:16-17` (`IsTerminal(existing.Status)`) and `TaskStateMachine.cs:34` (`SetRunning`'s guard). Two reads back to back with only a dictionary lookup between them; folded into one action because a timeout that lands between them makes every later write a no-op |
| `RevitStartWrite` — `status' = Running` | `TaskStateMachine.cs:36-38` (`Status = Running`, `StartedAt`, the "progress 0" broadcast, which the model does not put on the wire) |
| `RevitFinishes` — the command returns ok or an error | `CliCommandHandler.cs:31-32` (`CommandRouter.Execute`, `IsErrorResult`); the `catch` at `:37-44` is the fail outcome |
| `RevitEndCheck` — read the status; terminal → keep it (`exit`) | `TaskStateMachine.cs:65` (`SetCompleted`) and `:84` (`SetFailed`) |
| `RevitEndWrite` — `status' = Completed|Failed`, first writer records `winner` | `TaskStateMachine.cs:67` / `:86` |
| `RevitResultWrite` — `result' = outcome` | `TaskStateMachine.cs:68` / `:87` (`CompletedAt` at `:69` / `:88` is not modelled) |
| `RevitBroadcast` — append the end frame | `TaskStateMachine.cs:70` / `:89` (`Broadcast("completed" | "failed", ...)`) |
| `RevitTcsWrite` — `tcs' = IF tcs = None THEN outcome ELSE tcs` | `TaskStateMachine.cs:71` / `:90` (`Tcs.TrySetResult`) |
| `HttpWakes` — the HTTP thread's wait ends (the timeout, or the result) | `SseExecuteHandler.cs:140-144` (`Task.Delay`, `Task.WhenAny`); `CliHttpServer.cs:356-359` |
| `HttpEndCheck` — read the status; terminal → no-op (`finalPending`) | `TaskStateMachine.cs:102` (`SetTimeout`'s guard), reached from `SseExecuteHandler.cs:146` and `CliHttpServer.cs:368` |
| `HttpEndWrite` — `status' = Timeout`, first writer records `winner` | `TaskStateMachine.cs:104` |
| `HttpResultWrite` — `result' = timeout` | `TaskStateMachine.cs:105` |
| `HttpBroadcast` — append "failed" | `TaskStateMachine.cs:107` (`Broadcast("failed", { status = "timeout", ... })`) |
| `HttpTcsWrite` — `tcs' = IF tcs = None THEN timeout ELSE tcs` | `TaskStateMachine.cs:108` |
| `HttpFinalFrame` — send the final frame and return | `SseExecuteHandler.cs:150-171` (final frame from `GetTask().Status` / `ResultJson`, heartbeat stop, `ClearSseSubscribers`); the sync path `CliHttpServer.cs:359-372` writes `Tcs.Task.Result` or the timeout error. The frame re-sends the registry, which `FirstTerminalWins` guards, so it is not put on the wire |
| `Done` — both threads have returned | the end state, so that `NoDeadlock` has a step out of it |

`TaskRegistry.cs:38-76` is the pass-through the handlers call; `TaskRegistry.CreateTask`
(`:18-30`) is the `Init` state (pending, nothing written).

## Bounds and assumptions

- One task, one command, exactly two threads, one dequeue. Revit's `ExternalEvent.Raise()`
  coalescing is not modelled: the command is dequeued once.
- The Revit handler's outcome is ok or fail; both end paths have the same shape, and the model
  keeps both so that `SetCompleted` and `SetFailed` are each covered.
- The HTTP thread's wake is one step whether the timeout or the result ended the wait; a wake
  after the task ended takes the no-op path, as in the code (`WhenAny` returns the TCS task
  and `SetTimeout` is never called, or its guard returns).
- Not on the wire: the "progress 0" frame from `SetRunning`, handler progress frames,
  heartbeats, and the final frame. None carries an outcome the registry does not already have.
- `StartedAt` and `CompletedAt` are not modelled; no rule reads them.
- `GET /api/task/{id}` (`CliHttpServer.cs:399-434`) reads status and result in separate locks;
  a poller can see `status: timeout` with no `result` for the microseconds between
  `TaskStateMachine.cs:104` and `:105`. `FirstTerminalWins` allows the winner's own in-progress
  write, so that transient is not a finding here and is not covered.
- Not covered: `TaskRegistry.CreateTask` overwriting a task when a client reuses a `task_id`,
  the `FindByRequestId` dedup lookup (`TaskRegistry.cs:83-92`) which is itself a check-then-act
  across two requests with one `X-Request-Id`, `CleanupOldTasks`, the queue-full path in
  `CliHttpServer.cs:315-335` (the task is created at `:315` before the 429 at `:329-335` and is
  never enqueued, so it stays pending with no `CompletedAt` and is never cleaned up), concurrent
  writes to one SSE `OutputStream` (`HttpIo.WriteSseEventAsync` from the Revit thread, the
  heartbeat task and the HTTP thread), and `SetProgress` after a terminal state (no guard at
  `TaskStateMachine.cs:46-54`, but handlers call it only inside `Handle`).
- A pass covers this model and these bounds, not the whole bridge.

## Findings at the sync commit

Both come from the same lines. Commit `6143ff2` (2026-11-17) added the guards as a read under
one lock and a write under the next; before it the flips were unconditional, after it they
need the two threads to pass their reads together.

**Finding 1 — a timed-out task can flip back to running and then completed.** Path in the
code's words (TLC `FirstTerminalWins`, 5 steps; replay `TestVerify_Concurrent_StartVsTimeout_*`):
the command sits in the queue past its budget → the HTTP thread's wait ends → it reads the
status: not ended → Revit dequeues the command and reads the status: not ended → the HTTP
thread writes "timed out" (and then the result, the "failed" frame and the TCS) → Revit writes
"running". `CliCommandHandler.cs:31` runs the command regardless of what `SetRunning` did, the
transaction commits, and `SetCompleted` sees "running" and writes completed. The replay's
end record: `status=Completed result=completed tcs=timeout wire=[progress,failed,completed]`.
The client printed "Revit API execution timed out." (exit 3); `GET /api/task/{id}` says
completed with the ok result; an agent that trusts either and retries creates the element twice.

**Finding 2 — a completed task can be overwritten as timed out, and the client may be told
either.** Path (TLC `OneOutcomeOnTheWire`, 12 steps; replay
`TestVerify_Concurrent_CompletionVsTimeout_*`): the command returns as the budget runs out →
both threads read the status: not ended → Revit writes "completed" → the HTTP thread writes
"timed out" → Revit stores the ok result (the record now reads "timed out, result: ok") → both
broadcast, the stream carries `completed > failed`. Records the replay produced:
`status=Timeout result=timeout tcs=completed wire=[completed,failed]` (the client gets the ok
result and exit 0; `/api/task` says timed out), `status=Completed result=completed tcs=timeout
wire=[failed,completed]` (the client is told timed out, exit 3; `/api/task` says completed),
and `status=Completed tcs=completed wire=[completed,failed]` (record and TCS agree, the stream
told both).

Model drill (2026-11-24): a scratch copy of the model in which each check and its write are one
action satisfies `FirstTerminalWins`, `OneOutcomeOnTheWire` and `NoDeadlock` (statechart exit 0,
75 states); the shipped model breaks both rules (statechart exit 1; `tlc.out` stops at
`Error: Invariant FirstTerminalWins is violated`, the runner's second pass finds
`OneOutcomeOnTheWire`, the full exploration reports 312 states). Real-code drill: deleting the
`IsTerminal` guard in `SetTimeout` (`TaskStateMachine.cs:102`) makes
`TestVerify_Sequential_FirstTerminalWins` fail deterministically (`Expected: Completed,
Actual: Timeout`), and deleting the guard in `SetCompleted` (`:65`) makes it fail with
`Expected: Timeout, Actual: Completed`; restoring either brings back the baseline (sequential
PASS, both concurrent tests FAIL). The product code was not changed.

## Does a change touch the verified code?

```powershell
git diff --stat 51e7d20b2b4776ced466030305033b4ea6db25e5 -- bridge/RevitCliBridge/CliBridge/TaskStateMachine.cs bridge/RevitCliBridge/CliBridge/TaskInfo.cs bridge/RevitCliBridge/CliBridge/TaskRegistry.cs bridge/RevitCliBridge/CliBridge/CliCommandHandler.cs bridge/RevitCliBridge/CliBridge/SseExecuteHandler.cs bridge/RevitCliBridge/CliBridge/CliHttpServer.cs bridge/RevitCliBridge.Tests/RevitCliBridge.Tests.csproj
```

A non-empty result for `TaskStateMachine.cs` or `TaskInfo.cs` means the verification needs
another look before it can be trusted on the new commit. The `.csproj` matters because the
replay compiles the real `TaskStateMachine.cs`, `TaskInfo.cs` and `TaskStatus.cs` through its
`<Compile Include>` links; if a file leaves that list, the replay compiles against a copy or
not at all. A change only to doc comments, to `StartedAt`/`CompletedAt`, or to handlers has no
effect on the model; say so by checking against the action table, not the commit message.

## After a change

1. Run `run.ps1` as it is and record the exit code and which rules/tests fail.
2. Diff the covered files from the sync commit and classify each hunk against the action
   table: no effect, the model no longer describes the code (a step split or merged, a branch
   added or removed, a new path to the guarded state), or the promise changed on purpose (cite
   the doc comment, test, or commit that says so).
3. Update `TaskLifecycle.tla` from the code, never from the result. Keep one indivisible step
   in the code as one action. Update the action table and the replay test to the new code.
4. Show the updated check can still fail: break the code the way the rules guard against (for
   example delete a guard in `TaskStateMachine.cs` in a scratch edit, or split a check from its
   write in a scratch copy of the model), confirm the rule or test breaks, then drop the break
   and confirm `git status -- bridge` is clean.
5. Move the sync commit forward only after confirming the model and the replay describe the
   code at the new commit. If the findings are fixed, rewrite "Expected result" and
   "Findings" from what the run shows; if they are still open, keep them.
6. Put verification changes in their own PR, never mixed with product changes.

For changes that skipped this skill (merges, refactors), use `/maintain-formal-verification-skill`.

## Files

- `TaskLifecycle.tla` — the model (`Init`, `Next`, the two rules).
- `TaskLifecycle.cfg` — `SPECIFICATION Spec`, the two invariants.
- `view.ts` — the reader's words for `statechart`: states and arrows. It hides `Done` and shows
  `result` / `tcs` only when they disagree with the status, so that the correct paths read as
  one state per step. The rules are checked on all of them.
- `replay/TaskLifecycleReplayTests.cs` — the C# replay; copied into the test project by
  `run.ps1`, never committed there.
- `run.ps1`, `bootstrap-tools.ps1` — the runner and the pinned tool installer.
