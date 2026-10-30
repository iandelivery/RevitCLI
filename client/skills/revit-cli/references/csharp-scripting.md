# Raw C# Scripting: Hard Constraints and Execution Discipline

Every constraint below was hit and verified in real production runs; violating any of them fails the script.

## Bridge Compile Template

The bridge compiles C# user code into the following host. Write the **body only** — the signature, usings, and class wrapper already exist:

```csharp
private static string BuildCSharpScriptClass(string userCode)
{
   return $@"
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Structure;
using System.Collections.Generic;
using System.Linq;

public static class ScriptHost
{{
    public static object Execute(UIApplication app, Document doc)
    {{
        {userCode}
        // return fallback
        return null;
    }}
}}
";
}
```

Consequences:

- `doc`, `app`, `UIApplication`, `Document`, and the common Autodesk namespaces are already in scope
- The method must `return` a value (string serialization is done by the bridge)

Python (`--lang python`) runs through an equivalent host with the same constraints on encoding and transactions.

## Transactions

If `userCode` contains transaction code, it MUST install the failure preprocessor:

```csharp
var options = <transaction_object>.GetFailureHandlingOptions();
options.SetFailuresPreprocessor(new CliFailurePreprocessor());
<transaction_object>.SetFailureHandlingOptions(options);
```

## Hard Constraints

| # | Constraint | Detail |
|---|------------|--------|
| 1 | **ASCII-only payload** | The script text passed to `execute_raw` must be pure ASCII (every `ord(c) < 128`). Encode files with `ascii` and assert before sending |
| 2 | **LF line endings only** | No `\r` anywhere; Windows CRLF leaks break the JSON payload on the wire |
| 3 | **No `using` directives** | The template already provides them; a duplicate or unknown `using` fails compilation |
| 4 | **English-only comments** | Template and script comments must be English — non-ASCII comments violate constraint 1 |
| 5 | **Flat parallel arrays** | Pass data as flat `double[]` / `string[]` arrays (optionally with offset/count companion arrays), never nested or dynamic structures |
| 6 | **Non-ASCII strings via char codes** | Chinese level names, parameter names (宽度/高度/注释), full-width × in type names: build with `(char)0xXXXX` expressions (see `csexpr` below) |
| 7 | **Units mm ↔ feet** | The client's high-level commands take millimeters, but raw scripts run against the Revit API whose internal units are feet. Convert with `Autodesk.Revit.DB.UnitUtils.ConvertToInternalUnits(v, u)` (mm→feet) / `ConvertFromInternalUnits(v, u)` (feet→mm) — **the unit-argument type changed in Revit 2021**: ≤2020 takes `Autodesk.Revit.DB.DisplayUnitType.DUT_MILLIMETERS` (obsolete since 2021, slated for removal in a future release); ≥2021 takes `Autodesk.Revit.DB.UnitTypeId.Millimeters` (ForgeTypeId). Pick the overload matching the target Revit version; hand-rolled `1.0/304.8` only as a fallback. `Elevation` differences are already feet — never double-convert |
| 8 | **One transaction per logical unit** | Delete + create inside the same transaction, commit once; splitting delete/create across transactions leaves half-rebuilt states |
| 9 | **Probe before create** | First run a read-only probe (levels, family types, parameter names), then generate the creation script against real names/IDs |
| 10 | **Assert before writing payload** | In generators, run the ASCII/LF assertions BEFORE writing the script file — if the assert fires after write (or after `json.dump`), the stale file gets sent and "fixed" code appears to still fail |
| 11 | **{3D} view for deletion scripts** | Before running delete-heavy scripts, switch the active Revit view to {3D}; deletion from certain plan views deadlocks |
| 12 | **Experiment → batch → revalidate** | Run one element first, verify it, then batch; immediately run full validation after each batch — never batch straight from a cold script |

## Generator Helpers (Python)

Standard helpers used by script generators to satisfy constraints 1, 2, and 6:

```python
def csexpr(s):
    """C# expression for a string: plain literal if ASCII, char-code array otherwise."""
    if all(ord(c) < 128 for c in s):
        return '"%s"' % s
    return 'new string(new char[] {%s})' % ','.join('(char)0x%04X' % ord(c) for c in s)

def write(path, text):
    """Write with ASCII+LF enforcement (constraints 1-2). Assert BEFORE writing."""
    assert all(ord(c) < 128 for c in text), 'non-ascii leaked: ' + repr([c for c in text if ord(c) >= 128][:3])
    assert '\r' not in text
    with open(path, 'wb') as f:
        f.write(text.encode('ascii'))
    print('wrote', path, len(text), 'chars')
```

## Execution Discipline

1. **Probe first** — discover levels, family types, and parameter names with a read-only script; never hardcode assumptions about the target document
2. **Single-element experiment** — create exactly one element, read it back (LocationCurve / parameters), compare against source data
3. **Batch** — only after the experiment verifies; wrap each element in `try/catch` collecting errors into a list, return a summary string (`made=N/M errors=[...]`)
4. **Immediate full validation** — after every batch round, run the complete validation suite (counts, geometry readback, overlap/offset checks). Do not accumulate rounds without validation
5. **Keep source data in sync** — any Revit-side create/delete/modify must be written back to the source-of-truth data files, or downstream validation reports stale results
6. **Keep skipped items traceable** — every skipped/uncertain item keeps a reference back to its source data so user questions can be answered by direct lookup
7. **Confirm save** — remind the user to confirm saving; never save silently (see safety rules in SKILL.md)

See [troubleshooting.md](troubleshooting.md) for the fault quick reference when a run fails.
