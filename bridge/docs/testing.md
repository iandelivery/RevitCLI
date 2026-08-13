# Testing

> Test strategy and coverage. Pure-logic extraction enables fast, isolated unit
> tests without the Revit API — see [overview.md](overview.md#key-design-principle-pure-logic-extraction).

## Project

`RevitCliBridge.Tests/` — net48 xUnit project. References only
`RevitCliBridge.Abstractions.csproj` and links pure-logic sources via
`<Compile Include="..\RevitCliBridge\CliBridge\TaskStateMachine.cs" />`.

61 tests across 6 suites run in under a second.

## Suites

| Suite | File | Tests | Covers |
|-------|------|:-----:|--------|
| CommandNameResolver | `CommandNameResolverTests.cs` | 13 | Exact match, domain path suffix, underscore reversal, alias fallback, empty/null |
| TaskStateMachine | `TaskStateMachineTests.cs` | 15 | Transitions, timestamps, SSE payload, TCS completion, double-set safety |
| ParameterBinder | `ParameterBinderTests.cs` | 17 | Required/default/optional, type conversion, `List<object>`→`int[]`, nullable, errors, snake_case |
| CommandResponse | `CommandResponseTests.cs` | 4 | Success/Error JSON shape, `task_id` propagation |
| PortAllocator | `PortAllocatorTests.cs` | 3 | Base port by version, fallback chain |
| CliBridgeConfigAuth | `CliBridgeConfigAuthTests.cs` | 9 | API key validation, constant-time comparison, empty-key disables auth |

## Running

```bash
dotnet test bridge/RevitCliBridge.Tests/RevitCliBridge.Tests.csproj
```

Requires the net48 test host (Visual Studio or `dotnet test` with x86/x64 net48
support).

→ Next: [operations.md](operations.md).
