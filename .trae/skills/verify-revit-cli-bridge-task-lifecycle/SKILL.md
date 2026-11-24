---
name: verify-revit-cli-bridge-task-lifecycle
description: Model-check the RevitCliBridge task lifecycle promise — TaskStateMachine (bridge/RevitCliBridge/CliBridge/TaskStateMachine.cs) ending a TaskInfo from the Revit main thread (CliCommandHandler) while the HTTP thread's timeout (SseExecuteHandler, CliHttpServer) ends the same task — with TLA+/TLC, and replay each bug path against the real C# TaskStateMachine. Use when a change touches TaskStateMachine.cs, TaskInfo.cs, TaskRegistry.cs, CliCommandHandler.cs, SseExecuteHandler.cs, the SetTimeout call in CliHttpServer.cs, or the linked files in RevitCliBridge.Tests.csproj; when someone reports a command that changed the model but the client printed "Revit API execution timed out." (exit 3) or the reverse, GET /api/task/{id} disagreeing with what the client printed, a retried command running twice, two end frames on one SSE stream, or when asked whether the task lifecycle is still verified.
---

# Verify the bridge task lifecycle (formal)

**Sync commit:** `667e5e93e17294070f3ea42f0b598d0c0519cfc5` (main, 2026-11-24, the merge of the fix
in PR #115). The model and the replay were confirmed to describe the bridge code at that commit.

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

Since PR #115 the promise is kept in one place. `TaskInfo.TrySetStatus`
(TaskInfo.cs:39-49) holds `_lock` across the terminal check, the status write, and the SSE
broadcast, so two threads cannot both pass the check, and no later frame can overtake the
one the winner sends. `Status` has no setter (TaskInfo.cs:27-31). `SetRunning` returns
false when the transition is refused, and `CliCommandHandler.Execute` skips the command
instead of running it (CliCommandHandler.cs:26-33).

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

The check **passes**:

- `run.ps1` exits **0**.
- `replay.txt`: all three tests PASS — `TestVerify_Sequential_FirstTerminalWins`,
  `TestVerify_Concurrent_CompletionVsTimeout_KeepFirstTerminal`,
  `TestVerify_Concurrent_StartVsTimeout_TimedOutTaskStaysEnded` (20000 gated two-thread
  trials each, zero torn).
- `chart\report.md`: `FirstTerminalWins`, `OneOutcomeOnTheWire`, `NoDeadlock` hold; TLC
  reports 60 states and 98 steps; the chart draws 42 states and 65 arrows.

Anything else is news: exit 1 means a rule broke (read `replay.txt` and `chart\report.md`,
walk the path against the action table, and report it as a bug unless a step cannot happen
in the code); the sequential test failing alone means a guard was removed; `INCONCLUSIVE` in
`replay.txt` (exit 2) means the machine could not overlap the two threads (one core, or
fully loaded) — rerun on an idle machine with two or more cores; exit 2 for any other
reason is drift.

## History

The findings this skill was built for are fixed by PR #115 (`4096795`, merged as `667e5e9`).
At the previous sync commit `51e7d20` every transition was a read under one `lock` and a
write under the next; the model had 312 states and broke both rules, and the replay tore
20 to 30 percent of trials (`status=Completed tcs=timeout wire=[progress,failed,completed]`,
`status=Timeout tcs=completed wire=[completed,failed]`). A scratch version of the fix that
made only the check-and-write atomic still tore 36 of 20000 (`wire=[failed,progress]`: the
Revit thread sent `progress` after the HTTP thread sent `failed`), which is why the
broadcast is inside the lock.

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
     `TaskStateMachineTests` already asserts, so it fails only when a guard is gone.
   - The two `TestVerify_Concurrent_*` tests release two threads on the same clock tick, one
     calling the Revit-side transition and one calling `SetTimeout`, 20000 times, and fail when
     any trial ends with a record whose parts disagree. The message prints up to five torn
     records as `status=.. result=.. tcs=.. wire=[..]` — read them as the TLC states below.
     The harness aligns the two calls to within one clock tick; in production the two threads
     are milliseconds apart, so the trial count proves the torn states are reachable, not how
     often they happen.
   - `tcs=` is what the sync JSON client (`CliHttpServer.cs:364`) and the SSE client's
     `WhenAny` see; `wire=` is the order of the frames the SSE client reads (it acts on the
     first end frame, `sseclient.go:317-330`).
2. **Model.** Read `chart\report.md` with the `statechart` skill's `/statechart` reader. When
   a rule breaks, it shows the shortest path in plain words and `chart.mmd` marks the states
   on it. Refer to `/statechart` by name; its options and paths change between releases, so
   nothing about it is copied here.
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
handler returned: ok or fail), `revitPc`, `httpPc`, `stream` (the frames sent, in order).

| Model action | Code at the sync commit |
|---|---|
| `RevitStart` — one `TrySetStatus(Running, "progress", …)`: terminal → skip the command (`exit`); else `status' = Running` and the "progress" frame is sent | `CliCommandHandler.cs:16-17` (the earlier `IsTerminal` skip), `:26-33` (`if (!TaskRegistry.SetRunning(...)) continue;`) → `TaskRegistry.cs:37-40` → `TaskStateMachine.cs:32-40` → `TaskInfo.cs:39-49`. One lock over check, write, and broadcast (`TaskInfo.cs:41-48`); `StartedAt` (`TaskStateMachine.cs:38`) is not modelled |
| `RevitFinishes` — the command returns ok or an error | `CliCommandHandler.cs:38-39` (`CommandRouter.Execute`, `IsErrorResult`); the `catch` at `:44-51` is the fail outcome |
| `RevitEnd` — one `TrySetStatus(Completed|Failed, frame, …)`: terminal → keep it (`exit`); else `status' = Completed|Failed`, first writer records `winner`, and the end frame is sent | `TaskStateMachine.cs:66-67` (`SetCompleted`) / `:84-85` (`SetFailed`), both into `TaskInfo.cs:39-49` |
| `RevitResultWrite` — `result' = outcome` | `TaskStateMachine.cs:69` / `:87` (`CompletedAt` at `:70` / `:88` is not modelled) |
| `RevitTcsWrite` — `tcs' = IF tcs = None THEN outcome ELSE tcs` | `TaskStateMachine.cs:71` / `:90` (`Tcs.TrySetResult`) |
| `HttpWakes` — the HTTP thread's wait ends (the timeout, or the result) | `SseExecuteHandler.cs:140-142` (`Task.Delay`, `Task.WhenAny`); `CliHttpServer.cs:357` |
| `HttpEnd` — one `TrySetStatus(Timeout, "failed", …)`: terminal → no-op (`finalPending`); else `status' = Timeout`, first writer records `winner`, and the "failed" frame is sent | `TaskStateMachine.cs:101-102` (`SetTimeout`) into `TaskInfo.cs:39-49`, reached from `SseExecuteHandler.cs:146` and `CliHttpServer.cs:368` |
| `HttpResultWrite` — `result' = timeout` | `TaskStateMachine.cs:104` |
| `HttpTcsWrite` — `tcs' = IF tcs = None THEN timeout ELSE tcs` | `TaskStateMachine.cs:106` |
| `HttpFinalFrame` — send the final frame and return | `SseExecuteHandler.cs:150-168` (final frame from `GetTask().Status` / `ResultJson`, heartbeat stop, `ClearSseSubscribers`); the sync path `CliHttpServer.cs:359-372` writes `Tcs.Task.Result` or the timeout error. The frame re-sends the registry, which `FirstTerminalWins` guards, so it is not put on the wire |
| `Done` — both threads have returned | the end state, so that `NoDeadlock` has a step out of it |

`TaskRegistry.cs:42-68` is the pass-through the handlers call for the other transitions;
`TaskRegistry.CreateTask` (`:18-29`) is the `Init` state (pending, nothing written).

## Bounds and assumptions

- One task, one command, exactly two threads, one dequeue. Revit's `ExternalEvent.Raise()`
  coalescing is not modelled: the command is dequeued once.
- The Revit handler's outcome is ok or fail; both end paths have the same shape, and the model
  keeps both so that `SetCompleted` and `SetFailed` are each covered.
- The HTTP thread's wake is one step whether the timeout or the result ended the wait; a wake
  after the task ended takes the no-op path, as in the code (`WhenAny` returns the TCS task
  and `SetTimeout` is never called, or `TrySetStatus` returns false).
- The "progress 0" frame is on the wire because it is sent under the same lock as the
  `Running` write; handler progress frames, heartbeats, and the final frame are not. None
  carries an outcome the registry does not already have.
- `StartedAt` and `CompletedAt` are not modelled; no rule reads them.
- `GET /api/task/{id}` (`CliHttpServer.cs:399-434`) reads status and result in separate locks;
  a poller can see `status: timeout` with no `result` for the microseconds between
  `TaskStateMachine.cs:101` and `:104`. `FirstTerminalWins` allows the winner's own in-progress
  write, so that transient is not a finding here and is not covered.
- Not covered: `TaskRegistry.CreateTask` overwriting a task when a client reuses a `task_id`,
  the `FindByRequestId` dedup lookup (`TaskRegistry.cs:79-88`) which is itself a check-then-act
  across two requests with one `X-Request-Id`, `CleanupOldTasks`, the queue-full path in
  `CliHttpServer.cs:315-335` (the task is created at `:315` before the 429 at `:329-335` and is
  never enqueued, so it stays pending with no `CompletedAt` and is never cleaned up), concurrent
  writes to one SSE `OutputStream` from the heartbeat task and the HTTP thread, and
  `SetProgress` after a terminal state (no guard at `TaskStateMachine.cs:47-55`, but handlers
  call it only inside `Handle`).
- A pass covers this model and these bounds, not the whole bridge.

## Can the check still fail?

Yes, and that was shown at this sync (2026-11-24): a scratch copy of the model with `HttpEnd`
split back into `HttpEndCheck` and `HttpEndWrite` (the shape before PR #115) breaks
`FirstTerminalWins` in 6 steps (statechart exit 1, 114 states): both threads read "not ended",
Revit writes running, the command returns, Revit writes completed, the HTTP thread writes
timed out. The shipped model passes (exit 0, 60 states). On the real code, deleting the
terminal check in `TaskInfo.TrySetStatus` (`TaskInfo.cs:43-44`) makes
`TestVerify_Sequential_FirstTerminalWins` fail (`Expected: Timeout, Actual: Completed`) and
the two concurrent tests tear again.

## Does a change touch the verified code?

```powershell
git diff --stat 667e5e93e17294070f3ea42f0b598d0c0519cfc5 -- bridge/RevitCliBridge/CliBridge/TaskStateMachine.cs bridge/RevitCliBridge/CliBridge/TaskInfo.cs bridge/RevitCliBridge/CliBridge/TaskRegistry.cs bridge/RevitCliBridge/CliBridge/CliCommandHandler.cs bridge/RevitCliBridge/CliBridge/SseExecuteHandler.cs bridge/RevitCliBridge/CliBridge/CliHttpServer.cs bridge/RevitCliBridge.Tests/RevitCliBridge.Tests.csproj
```

A non-empty result for `TaskStateMachine.cs`, `TaskInfo.cs`, or `CliCommandHandler.cs` means
the verification needs another look before it can be trusted on the new commit. The `.csproj`
matters because the replay compiles the real `TaskStateMachine.cs`, `TaskInfo.cs` and
`TaskStatus.cs` through its `<Compile Include>` links; if a file leaves that list, the replay
compiles against a copy or not at all. A change only to doc comments, to
`StartedAt`/`CompletedAt`, or to handlers has no effect on the model; say so by checking
against the action table, not the commit message.

## After a change

1. Run `run.ps1` as it is and record the exit code and which rules/tests fail.
2. Diff the covered files from the sync commit and classify each hunk against the action
   table: no effect, the model no longer describes the code (a step split or merged, a branch
   added or removed, a new path to the guarded state), or the promise changed on purpose (cite
   the doc comment, test, or commit that says so).
3. Update `TaskLifecycle.tla` from the code, never from the result. Keep one indivisible step
   in the code as one action. Update the action table and the replay test to the new code.
4. Show the updated check can still fail: break the code the way the rules guard against (for
   example delete the terminal check in `TaskInfo.TrySetStatus` in a scratch edit, or split a
   transition back into check and write in a scratch copy of the model), confirm the rule or
   test breaks, then drop the break and confirm `git status -- bridge` is clean.
5. Move the sync commit forward only after confirming the model and the replay describe the
   code at the new commit. If a rule breaks and every step of its path can happen in the code,
   write a "Findings" section with the path and the harm, and keep the check failing.
6. Put verification changes in their own PR, never mixed with product changes.

For changes that skipped this skill (merges, refactors), use `/maintain-formal-verification-skill`.

## Files

- `TaskLifecycle.tla` — the model (`Init`, `Next`, the two rules).
- `TaskLifecycle.cfg` — `SPECIFICATION Spec`, the two invariants.
- `view.ts` — the reader's words for `statechart`: states and arrows. It hides `Done` and shows
  `result` / `tcs` only when they disagree with the status, so that the correct paths read as
  one state per step. The rules are checked on all of them.
- `replay/TaskLifecycleReplayTests.cs` — the C# replay; copied into the test project by
  `run.ps1`, never committed there. `bridge/RevitCliBridge.Tests/TaskLifecycleRaceTests.cs` is
  the committed copy of the same harness (PR #115), so the fix is guarded for developers who
  never run the skill.
- `run.ps1`, `bootstrap-tools.ps1` — the runner and the pinned tool installer.
