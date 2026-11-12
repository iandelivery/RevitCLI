# Design: query elements in linked documents

Status: proposed. No code yet.

## Problem

The bridge cannot query elements that live in a linked RVT. `get_elements`
collects from the host document only ([GetElementsHandler.cs]
(../RevitCliBridge/CliBridge/Handlers/Query/GetElementsHandler.cs) builds
`FilteredElementCollector(doc)` on the host `doc`). An agent driving a
coordination model — an architecture file with linked structure and MEP
links — sees the links as opaque `RevitLinkInstance` elements and nothing
inside them.

The gap is not a missing primitive. `HandlerUtilities.ResolveLinkDocuments`
([HandlerUtilities.cs](../RevitCliBridge/CliBridge/Handlers/HandlerUtilities.cs:77))
already maps link-selection parameters to linked `Document` objects. It has
zero callers, and its default branch returns every link's document even when
the caller passed no link parameter, which is the wrong default for
`get_elements`. This design replaces it.

Two existing handlers touch links correctly but do not query them:
`doc_list` and `document_info` report `is_linked` per document, and the save
handlers refuse linked documents. `get_links` adds the missing inventory —
placed link instances with paths and transforms — and does not overlap them.

A sweep of all 98 handlers confirms no other query path reaches a linked
document: every Query handler builds `FilteredElementCollector` on the host
`doc` it receives from `DocumentCommandBase`.

## Revit API building blocks (2019-2022)

- `RevitLinkInstance` is a host-document element, one per placed link.
  `GetLinkDocument()` returns the linked document as a read-only `Document`
  you can pass straight to `new FilteredElementCollector(linkDoc)`. An
  unloaded link returns `null` — every handler must report that state
  instead of failing on a null document.
- `RevitLinkInstance.GetTotalTransform()` gives the link-to-host transform.
  Use it, not `GetTransform()`, so nested links land in host coordinates.
- `RevitLinkType.GetExternalFileReference().GetAbsolutePath()` gives the
  link's source path.
- Element ids are per document. The same integer id can name a wall in the
  host and a duct in the link. Responses that carry linked elements must
  tag them with the link, and parameters that name a linked element must
  say which link it belongs to.
- `LinkElementId` is the Revit-native answer to that ambiguity: the tuple
  (link instance id, element id), where an invalid link instance id means
  a host element. It names an element but does not fetch it. The fetch
  chain is `hostDoc.GetElement(linkInstanceId)` → `GetLinkDocument()` →
  `linkDoc.GetElement(elementId)`. This design reconstructs the tuple in
  JSON instead of exposing the type: the `link` parameter carries the
  link instance id or name, the item `id` carries the element id, and
  `TryResolveTargetDocument` walks the fetch chain.
- Linked documents are read-only. Query handlers only; no modify handler
  can target a link.
- `ElementIntersectsElementFilter` accepts a linked element against a host
  collector (host coordinates are handled by the filter). That is the seed
  for clash-style queries, but it needs its own verification round, so it
  stays out of this design.

## Recommended handlers

One new command, four extended commands, one shared helper. The `link`
parameter accepts the `RevitLinkInstance` element id or the exact link
name. `get_links` is the entry point: an agent lists links first, then
targets one by id.

### 1. `get_links` (new, Query)

Lists every placed link in the active document.

```json
{
  "links": [
    {
      "id": 184324,
      "name": "Structural Model.rvt",
      "type_id": 184320,
      "loaded": true,
      "path": "D:\\models\\Structural Model.rvt",
      "is_nested": false,
      "origin": { "x": 0.0, "y": 0.0, "z": 0.0 }
    }
  ]
}
```

`loaded` is computed by `HandlerUtilities.GetLinkDocumentSafe`:
`GetLinkDocument()`, with a fallback that matches the linked `Document` in
`Application.Documents` by the link type's external file path when
`GetLinkDocument()` returns null. Live verification on Revit 2019 showed
`GetLinkDocument()` staying null while the linked document is open and
queryable (reproduced for API-created link types via `RevitLinkType.Create`
and persisting across save/reopen), so a bare `GetLinkDocument()` check
misreports loaded links. The fallback only runs when `GetLinkDocument()` is
null and returns null again when no open linked document carries the path,
so genuinely unloaded links still report `loaded: false`.
`origin` is the translation part
of `GetTotalTransform()`. Optional `--include-nested` adds nested links,
which are `RevitLinkInstance`s collected from each link's own document with
`IsNestedLink == true`.

### 2. Shared helper: `TryResolveTargetDocument`

Replace `ResolveLinkDocuments` with one helper in `HandlerUtilities`:

```csharp
public static Document? TryResolveTargetDocument(
    Document hostDoc,
    Dictionary<string, object> parameters,
    out string? linkLabel)
```

No `link` parameter: return the host document, `linkLabel = null`. With
`link`: parse the value as an integer and look up the instance id, or
treat it as an exact instance name. Return
`HandlerUtilities.GetLinkDocumentSafe(hostDoc, instance)` with
`linkLabel = "{name} (id {id})"`. Unloaded link: return `null` and let
the caller answer with a `CommandResponse.Error` naming the link. A
selector that matches no placed link sets
`linkLabel = "no placed link matches id {id}"` (or
`"no placed link matches name '{name}'"`, from
`LinkSelectorParser.Describe`) so the error distinguishes a missing link
from an unloaded one. The
old `ResolveLinkDocuments` is deleted.

Two naming rules protect against wrong-link results:

- The same file placed twice shares one name across two link instances.
  A name that matches several instances is an error listing their ids;
  the caller retries with an instance id.
- Resolution covers top-level links only. Nested links are listed by
  `get_links --include-nested`, but their instances live in the parent
  link's id space, so resolving them needs a walk through the parent
  document. That walk ships with nested query support, not before.

Two constraints from the handler plumbing shape the calls:

- Handlers are singletons. `CommandRouter.EnsureInitialized` creates one
  instance per handler and stores it in a `ConcurrentDictionary`
  ([CommandRouter.cs](../RevitCliBridge/CliBridge/CommandRouter.cs:58)),
  so link resolution must be stateless. Call the helper fresh inside each
  request; never cache the resolved document in a field.
- `PaginatedQueryHandler.QuerySource` returns a sequence and cannot report
  an error ([PaginatedQueryHandler.cs]
  (../RevitCliBridge/CliBridge/Handlers/PaginatedQueryHandler.cs:95)).
  For `get_elements` and `search_elements`, resolve the link in the
  `Validate` hook, which short-circuits with `CommandResponse.Error`
  before `QuerySource` runs, then resolve again inside `QuerySource`.
  Resolution reads a handful of `RVT_LINK_INSTANCE` elements, so doing it
  twice is cheap. `get_element_by_id` and `get_parameters` build their
  responses directly and return the error from their own `Execute`.

### 3. Extend `get_elements`, `get_element_by_id`, `get_parameters`, `search_elements`

Each gains one optional parameter:

| Parameter | Type | Description |
|-----------|------|-------------|
| `link` | `string` | Linked document to query: `RevitLinkInstance` id or exact link name. Default: the host document. |

All four call `TryResolveTargetDocument` at entry and run their existing
collector on the returned document. Items sourced from a link carry both
`link_id` (the link instance id) and `link_name`: `link_id` plus the item
`id` reconstructs the `LinkElementId` tuple, and the name keeps the
output readable. Existing calls without `link` behave exactly as today,
and `PaginatedQueryHandler` pagination keeps working because
`QuerySource` stays a single `IEnumerable`.

## Non-goals

- Modifying linked elements. The linked document is read-only; copying
  monitored elements into the host (`Copy/Monitor`) is an authoring
  feature, not a query.
- Geometric intersection queries against linked elements. Needed for
  clash checking, but `ElementIntersectsElementFilter` across a link
  deserves its own handler and verification round.
- Opening a linked file as the active document. `doc_open` already does
  that; the agent can query it as a host.
- Unloading, reloading, or placing links.
- Querying nested links (a link inside a link). `get_links
  --include-nested` lists them with their parent chain, but targeting
  them with the `link` parameter waits until the parent-document walk
  lands.

## Alternatives considered

- **Extend `ResolveLinkDocuments` instead of replacing it.** Its
  `no_links`/`all_links`/`link_names` multi-target semantics bloat every
  response and its no-parameter default (all links) would change what
  `get_elements` returns today. One target per call keeps responses clean
  and stays backward compatible.
- **A separate `get_linked_elements` command.** A `link` parameter on the
  existing commands teaches nothing new and asks the agent to learn fewer
  names. Schema discovery already advertises the new parameter.
- **Query links by filename glob.** Exact name or id from `get_links` is
  unambiguous; globs invite wrong-link mistakes.

## Test plan

1. Unit-test the pure selection logic split out of the helper, following
   the `CliBridgeConfigAuth` pattern for Revit-dependent code: a
   `ResolveLinkSelector(string raw)` function that parses integer vs
   name, and duplicate-name detection over a (id, name) candidate list.
   The `Document` walk itself takes Revit types and is covered by the
   live round instead.
2. Live round with the `verify-revit-cli-bridge` skill on a scratch copy
   of `Arch Link Model.rvt`: `get_links` lists its link; `get_elements
   --category OST_Walls --link <id>` returns walls carrying `link_id` and
   `link_name` for that link; the same call without `link` returns only
   host walls;
   `get_element_by_id --link <id> --element-id <linked-id>` resolves
   parameters inside the link.

## Files to change

| File | Change |
|------|--------|
| `Handlers/Query/GetLinksHandler.cs` | New handler; register in the command registry. |
| `Handlers/HandlerUtilities.cs` | Add `TryResolveTargetDocument`; delete `ResolveLinkDocuments`. |
| `Handlers/Query/GetElementsHandler.cs` | Accept `link`; tag items. |
| `Handlers/Query/GetElementByIdHandler.cs` | Accept `link`. |
| `Handlers/Query/GetParametersHandler.cs` | Accept `link`. |
| `Handlers/Query/SearchElementsHandler.cs` | Accept `link`. |
| `bridge/docs/commands.md` | Document `get_links` and the `link` parameter. |
