# RevitCliBridge Feature Map

Verification source of truth for the bridge (`bridge\`). Each file describes
one user-facing feature: what it is, how a user reaches it, how to drive it
with the harness (disposable Revit 2019 + the revit-cli client + `dotnet
test`), and what observable end state proves it works.

Harness setup (Launch/Doctor/Evidence/Cleanup, safety rules) is defined in
[../SKILL.md](../SKILL.md). `$cli` = built client, `$U` = `@('--pid',
'<OUR-pid>')`, `$SCRATCH` = throwaway model copies, `$EV` = evidence dir,
`Run` = transcript helper.

| Feature | File | Revit needed |
|---------|------|--------------|
| HTTP route matrix (public probes, 401, ETag/304, 404, shapes) | [http-protocol.md](http-protocol.md) | yes |
| SSE execution and task lifecycle (frames, no-wait, poll, dedup) | [sse-and-tasks.md](sse-and-tasks.md) | yes |
| The units contract (vocabulary, conversion, echo, get_units) | [units-contract.md](units-contract.md) | yes |
| Document commands (doc_open, levels, create_wall, undo, dry-run) | [document-commands.md](document-commands.md) | yes |
| Sheet viewport commands (place/move/remove/check/query) | [sheet-viewports.md](sheet-viewports.md) | yes |
| Auth, raw-mode, and config | [auth-and-config.md](auth-and-config.md) | yes |

Rule: a proof that drives one convenient entry point is incomplete when
this map lists others — cover the mapped journeys, and record failures
honestly in the feature's Gotchas instead of weakening the journey.
