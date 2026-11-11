# Design: live document info in `revit-cli list`

Status: proposed. No code yet. This revision replaces the earlier event-based
design: `list` now queries each bridge directly instead of keeping the
registry file current.

## Problem

`revit-cli list` prints a DOCUMENT column, but the value is always `null`.
The bridge writes the field once at registration and nothing updates it.
Users open a model in Revit, run `list`, and still see `-`.

## Approach

The registry answers discovery: which Revit processes run, on which port.
Live state belongs to the bridge, so `list` asks each bridge for its open
documents at read time. No Revit events, no registry writes, no staleness.

The registry `document` field stays as it is. It is no longer the source for
the DOCUMENT column. Deprecating the field is a separate cleanup.

## Bridge change

Add one route to `CliHttpServer`: `GET /api/documents`. It reads
`CliBridgeStateManager.UIApplication` and returns:

```json
{
  "documents": [
    { "title": "Project1", "path": "D:\\models\\Project1.rvt" },
    { "title": "Project2", "path": "D:\\models\\Project2.rvt" }
  ],
  "active_path": "D:\\models\\Project1.rvt"
}
```

`app.Documents` lists every open document. `app.ActiveUIDocument?.Document`
identifies the active one. An unsaved document has an empty `PathName`; keep
it with a null `path`. No active document means Revit sits on the home
screen: return an empty `documents` array and a null `active_path`.

This endpoint accesses Revit state from the HTTP thread, the same as the
existing `/api/llms.txt` route. The route requires the bearer key like every
other `/api` route except the health whitelist.

## Client change

`ListHandler.Handle` in `client/internal/client/builtin/instances.go`:

1. Call `instance.Discover()` as today.
2. For each instance, call `GET http://localhost:{port}/api/documents` in
   parallel, with a 2-second timeout and `auth.WithAuth` for the key.
3. Attach the result to the instance.
4. On timeout, connection error, or 401, mark the instance `unreachable` and
   keep the registry data.

`PrintInstances` shows the active path in the DOCUMENT column and appends the
open count when more than one document is open, for example
`D:\models\Project1.rvt (2 open)`. An unreachable instance prints `-` plus an
`unreachable` marker, so a wedged Revit (for example, a modal dialog blocking
the API) reads as unreachable instead of showing wrong data.

`--json` prints the enriched objects: the registry fields plus `documents`
and `active_path` when the bridge answered.

## Trade-offs accepted

- `list` now touches the network. The registry still lists every instance
  when no bridge answers, so discovery never breaks.
- A missing API key shows `-`. `configure setup` stores the key per
  host and port, so configured machines are unaffected.
- Query latency is bounded by the 2-second timeout, not by instance count,
  because the queries run in parallel.

## Test plan

1. Unit test the JSON enrichment logic with a stub HTTP server: one healthy
   bridge, one timing out, one returning 401. Assert the columns and the
   `--json` shape.
2. Live round with the `verify-revit-cli-bridge` skill: start Revit, run
   `doc_open` on a scratch copy, and assert `list` shows the path. Open a
   second document and assert the `(2 open)` suffix. Close both and assert
   `-`.

## Files to change

| File | Change |
|------|--------|
| `bridge/RevitCliBridge/CliBridge/CliHttpServer.cs` | Add the `GET /api/documents` route. |
| `client/internal/client/builtin/instances.go` | Query each bridge in parallel and enrich the output. |
| `client/internal/instance/discovery.go` | Extend `PrintInstances` for the open count and the unreachable marker. |
