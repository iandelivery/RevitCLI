# Testing

> Test strategy and coverage. Pure-logic extraction enables fast, isolated unit
> tests without the Revit API — see [overview.md](overview.md#key-design-principle-pure-logic-extraction).

## Project

`RevitCliBridge.Tests/` — net48 xUnit project. References only
`RevitCliBridge.Abstractions.csproj` and links pure-logic sources via
`<Compile Include="..\RevitCliBridge\CliBridge\TaskStateMachine.cs" />`.

The linked-sources pattern lets tests exercise production code that lives in the
main `RevitCliBridge` project without taking a dependency on `Revit_API_x64`.
If a linked file starts depending on the Revit API, remove it from the
`<ItemGroup>` in `RevitCliBridge.Tests.csproj` and refactor the pure logic into
`RevitCliBridge.Abstractions` instead.

**292 tests** across **14 suites** run in **under 700 ms** on a developer
workstation. All tests are deterministic, independent, and free of network or
Revit-process dependencies, so they run identically locally and in CI/CD.

## Suites

### Unit tests — pure-logic components

| Suite | File | Tests | Covers |
|-------|------|:-----:|--------|
| CommandNameResolver | `CommandNameResolverTests.cs` | 21 | Exact match, domain-path suffix, underscore reversal, alias fallback, empty/null input, `SplitVersion` (single/multiple `@`, no `@`), versioned domain-path resolution, versioned underscore reversal, re-attached version suffix |
| TaskStateMachine | `TaskStateMachineTests.cs` | 19 | Status transitions (Pending→Running→Completed/Failed), timestamp stamping (`StartedAt`/`CompletedAt`), SSE broadcast payload shape & ordering, TCS one-shot completion, double-set idempotency, null-progress-message handling |
| ParameterBinder | `ParameterBinderTests.cs` | 22 | Required/default/optional, `List<object>`→`int[]`, nullable value types, string→numeric conversion, snake_case binding, `MissingParameterException` & `ParameterTypeException` with `ParameterName`, missing key vs null value |
| CommandResponse | `CommandResponseTests.cs` | 13 | `Success`/`Error` factory methods, default status values, `task_id` propagation (including null), stable JSON key ordering (`task_id`→`status`→`message`→`data`), complex payload nesting |
| PortAllocator | `PortAllocatorTests.cs` | 9 | `GetBasePort` deterministic mapping by Revit version, version-range slots, fallback port chain, runtime `AllocatePort` with preferred-port-held and ephemeral fallback, port exhaustion behavior |
| CliBridgeConfigAuth | `CliBridgeConfigAuthTests.cs` | 11 | API key validation, Bearer prefix stripping, constant-time comparison, empty/whitespace-key edge cases, `IsEnabled` semantics, `GenerateKey` URL-safe output & 200-iteration uniqueness |
| PagedResultBuilder | `PagedResultBuilderTests.cs` | 11 | `GetPagingParams` parsing (string/int/double coercion), default limit/offset, `MaxLimit` clamping, `Build` truncation, `HasMore` boundary detection (exactly-N and N+1), custom `IEnumerable<T>` sources |

### Unit tests — DTOs and attributes

| Suite | File | Tests | Covers |
|-------|------|:-----:|--------|
| SchemaDtoContract | `SchemaDtoContractTests.cs` | 18 | JSON field names & `NullValueHandling.Ignore` for `CommandDef`, `CommandParamSchema`, `CommandSchema`, `CommandCatalog`, `CatalogEntry`, `ServerInfo`, `ServerFeatures`, `QueuedCommand`. Default values, non-null required fields, array-vs-list serialization. |
| ParamAttribute | `ParamAttributeTests.cs` | 8 | `[Param(Required = true)]` flag, default `Description`/`ShortFlag`/`Default` nullability, exception message format for `MissingParameterException` & `ParameterTypeException` |
| CliBridgeConfigDefaults | `CliBridgeConfigDefaultsTests.cs` | 7 | Security defaults (`ApiKey` null → auth disabled), resource defaults (`MaxConcurrentTasks`, `TaskTimeoutSeconds`), network defaults (`Host` = `localhost`), serialization defaults |

### Unit tests — observability and runtime state

| Suite | File | Tests | Covers |
|-------|------|:-----:|--------|
| CliLogger | `CliLoggerTests.cs` | 14 | `[INFO]`/`[WARN]`/`[ERROR]` prefix, structured field rendering (empty/non-empty arrays), daily-rollover file naming, thread-safe concurrent logging, message-without-fields plain rendering |
| TaskInfo | `TaskInfoTests.cs` | 9 | `OnSseEvent` broadcast to multiple subscribers, `ClearSseSubscribers` teardown, default property values (`Status` = `Pending`, `Progress` = 0), thread-safe `Status` read/write under concurrent readers & writers, subscriber-exception propagation |

### Integration tests — end-to-end workflows

| Suite | File | Tests | Covers |
|-------|------|:-----:|--------|
| IntegrationTests | `IntegrationTests.cs` | 14 | Multi-component workflows that mirror production HTTP handler flows: command dispatch (resolve → bind → respond), pagination (parse query → build page → serialize), SSE event stream (Pending → Running → Progress → Completed/Failed), auth gate + dispatch, schema generation + serialization, missing-parameter and wrong-type error paths |
| ScalabilityIntegrationTests | `IntegrationTests.cs` | 13 | Same workflows exercised against a registry of **150 registered commands** spanning bare, versioned (`@v1`), and domain-prefixed name patterns. Guards against O(n²) regressions in `CommandNameResolver`, schema serialization size growth, catalog-vs-full-schema size ratio, pagination overlap/gap coverage across 6 pages, and auth-gate O(1) behavior. |

### Test infrastructure

| Suite | File | Tests | Covers |
|-------|------|:-----:|--------|
| StaticStateTestCollection | `StaticStateTestCollection.cs` | — | xUnit `ICollectionFixture` that serializes tests touching static state (e.g., `CliLogger`'s static log file path). Prevents parallel-execution races without disabling parallelism for the whole suite. |

## Coverage strategy

The 80% coverage target applies to **critical pure-logic components** — the
ones whose failure would break every command dispatch:

- `CommandNameResolver` — every resolution strategy and edge case.
- `TaskStateMachine` — every transition and broadcast contract.
- `ParameterBinder` — every supported type conversion and error path.
- `CliBridgeConfigAuth` — every auth-enabled / auth-disabled branch.
- `PagedResultBuilder` — pagination math and `HasMore` boundary.
- `PortAllocator` — deterministic mapping and runtime fallback.
- `CommandResponse` — JSON serialization contract for client compatibility.
- Schema DTOs — field-name and null-handling contract for Go-client compat.

Coverage is **not** enforced for Revit-coupled handlers (they require the Revit
API and are exercised by integration tests in the Revit process) or for
trivial DTOs whose only logic is auto-properties.

## Conventions

### Naming

Test method names follow `Method_Condition_ExpectedBehavior`:

```csharp
[Fact]
public void SetProgress_BroadcastMessageIsNull_WhenNullPassed() { ... }
```

This makes the test runner output self-documenting — the failing assertion's
name says exactly what contract was violated.

### Determinism

- **No clocks**: tests that need a timestamp pass a fixed `DateTime` or assert
  only `NotNull` / ordering, not exact values.
- **No filesystem state**: `CliLoggerTests` write to a unique temp path per test
  method and clean up in a `try/finally`. No test reads from a hardcoded path.
- **No network**: `PortAllocatorTests` hold ports with `TcpListener` sockets
  released in `using` blocks; no test calls out to a real HTTP server.
- **No parallel races on static state**: tests touching static fields
  (`CliLogger`'s path) are decorated with `[Collection(StaticState)]` so xUnit
  runs them sequentially.

### Independence

- Each test constructs its own `TaskInfo` / `CliBridgeConfig` / parameter
  dictionary — no shared mutable state between tests.
- `NewTask()` helpers in each suite produce a fresh, isolated task per test.
- No test depends on another test having run first.

### Setup / teardown

Tests minimize setup cost:

- No `IClassFixture` for per-class state — each `[Fact]` is self-contained.
- `using` blocks (not `IDisposable` fixtures) manage port holders and temp
  files, so teardown cost scales with the test, not the suite.
- The only collection-level fixture is `StaticStateTestCollection`, used solely
  to serialize static-state tests, not to share mutable state.

## Running

### Local

```bash
dotnet test bridge/RevitCliBridge.Tests/RevitCliBridge.Tests.csproj
```

Requires the net48 test host (Visual Studio or `dotnet test` with x64 net48
support on Windows).

Filter to a single suite:

```bash
dotnet test bridge/RevitCliBridge.Tests/RevitCliBridge.Tests.csproj --filter "FullyQualifiedName~ScalabilityIntegrationTests"
```

Filter to a single test:

```bash
dotnet test bridge/RevitCliBridge.Tests/RevitCliBridge.Tests.csproj --filter "FullyQualifiedName=IntegrationTests.Pagination_FullFlow_ParsesQuery_BuildsPage_SerializesResult"
```

### CI/CD

The suite is CI-safe by construction:

- **No external dependencies** — no Revit, no HTTP server, no database.
- **Deterministic** — no `DateTime.Now` assertions, no `Thread.Sleep` racing.
- **Fast** — 292 tests in ~700 ms; runs on every PR without gating.
- **Independent** — tests can run in any order and in parallel (except the
  `[Collection(StaticState)]` group, which xUnit serializes automatically).

Recommended CI invocation:

```bash
dotnet test bridge/RevitCliBridge.Tests/RevitCliBridge.Tests.csproj \
  -c Release \
  --nologo \
  -- RunConfiguration.TreatNoTestsAsError=true
```

## Adding a new test

1. **Identify the suite** by the component under test. If the component is new,
   create a `*Tests.cs` file in `RevitCliBridge.Tests/`.
2. **Follow the naming convention** — `Method_Condition_ExpectedBehavior`.
3. **Keep it pure-logic** — if the code under test touches the Revit API,
   extract the pure logic into `RevitCliBridge.Abstractions` first, then test
   that. Linked-source files in the `.csproj` must remain Revit-free.
4. **Test edge cases** — null input, empty collections, boundary values
   (exactly N, N+1, N-1), type-coercion failures, double-application.
5. **Document the contract in the assertion comments** — a failing test should
   explain what production contract was violated, not just what value was
   expected. See `TaskStateMachineTests.cs` for the commenting style.
6. **If the test touches static state**, decorate it with
   `[Collection(StaticState)]` and document why.

## Scalability tests

The `ScalabilityIntegrationTests` class simulates a registry of **150
registered commands** — well above the projected 100+ future-command count —
spanning three real-world naming patterns:

- 70 bare names (`cmd_000` … `cmd_069`)
- 50 versioned names (`cmd_070@v1` … `cmd_119@v1`)
- 30 domain-prefixed names (`domain_alpha.cmd_120` … `domain_alpha.cmd_149`)

These tests guard against algorithmic regressions as the command catalog grows:

- `CommandNameResolver` must stay O(1)-per-lookup (no O(n) scan over the
  registry).
- `CommandSchema` serialization must include every command — no silent
  truncation at 150 entries.
- Schema response size must stay under 200 KB; if it grows, switch clients to
  the `CommandCatalog` (lightweight) response.
- `CommandCatalog` must be smaller than the full `CommandSchema` — that's its
  reason for existing.
- Pagination must cover all 150 commands across 6 pages with no overlap or gap,
  and `HasMore` must flip exactly on the final page.

If you add a new resolver strategy, serializer, or pagination path, add a
matching scalability test.

→ Next: [operations.md](operations.md).
