# Configure and skill management (read-only paths)

## Sub-features

- `configure check` — running instances (registry discovery), installed
  Revit add-ins, per-instance `/api/health` probe (the route is public and
  the status code is not checked, so an authenticated bridge prints
  `healthy`), then `Data directory:` / `Schema cache:` / `Instance
  registry:` / `Client binary:` (three states: `not staged`, `(vX —
  stale, run 'revit-cli.exe skill update')`, or the bare path).
  `Running instances: none` when empty; a `Connectivity:` block only with
  live instances. Arguments are ignored (`check --json` prints text).
  Read-only; exit 0.
- `configure port` — table (or `--json`) of supported versions **2019-2024**
  with base ports 5011/5021/5031/5041/5051/5061 and live port/PID from
  discovery (`-` when no instance). Header `VERSION BASE PORT ACTUAL
  PORT PID`; JSON keys `version,base_port` (+ `port,pid` only when live).
  Read-only; exit 0.
- `skill show` — version, exe path, SKILL.md source, staged binary state
  (`Binary copy: not staged` / a path / version unknown), candidate dirs
  under `Project targets (cwd)` / `User targets (home)` and `Installed
  locations`. Supports `--json`. Read-only; exit 0 even when the source
  is missing.
- `skill install --list` — detected agent directories (claude, cursor,
  codex, trae, trae-cn, codebuddy, workbuddy, kimi-work, kimi-code), each
  `installed vX` / `detected` / `not detected`; `--json` →
  `{version,scope,candidates}`; `--project` = cwd scope. Prints
  candidates, writes nothing. Read-only.
- MUTATING (out of scope for routine drives): `configure setup`/`teardown`
  (installs/removes the user's bridge add-in under
  `%APPDATA%\Autodesk\Revit\Addins`, regenerates `api_key` when absent),
  `skill install/update/uninstall` (writes SKILL.md into agent dirs and
  stages the binary at `<data-dir>\bin\revit-cli.exe` +
  `revit-cli.version` — never into the agent dirs). `update`/`uninstall`
  on the running exe's own dir only deregister it (manifest + index
  removed, files kept, `detail: deregistered`). Drive them only against
  scratch `--target` dirs with explicit `--force` awareness.

## How to get to it (user POV)

"Is my install healthy?" — check; "which agents can I register?" — list;
"where is my skill installed?" — show.

## Driving it with the terminal (scratch data dir)

```powershell
$env:REVIT_CLI_DATA_DIR = $SCRATCH        # MUST be set: check prints DataDir
Run "configure-check" @("configure","check")   # exit 0; "Data directory:" == $SCRATCH; "Client binary:         not staged (created by 'revit-cli.exe skill install')"
Run "configure-port"  @("configure","port")    # exit 0; 6 rows 2019->5011 ... 2024->5061
Run "configure-port-json" @("configure","port","--json")   # array of 6; keys version,base_port only
Run "skill-show"  @("skill","show")             # exit 0; "Binary copy:  not staged"; "Installed locations:" -> "(none"
Run "skill-list"  @("skill","install","--list") # exit 0; candidate agent dirs only, each installed/detected/not detected
Run "skill-list-json" @("skill","install","--list","--json")   # {version,scope:"user",candidates{agent,detected,...}}
```

Optional (mutating, scratch-scoped): `skill install --target
"$SCRATCH\agents\claude"` then assert the manifest
`.revit-cli-skill.json`, the staged binary under `<data-dir>\bin\`, and the
rewritten binary-path line in the installed SKILL.md; `skill uninstall`
afterwards. Never point install at the user-level dirs from a verification
run.

## Gotchas

- `configure check`'s health probe hits the public `/api/health` without
  an auth header and never checks the status code — against a real
  authenticated bridge it prints `healthy`. That is the design, not drift.
- All configure/skill state keys off `REVIT_CLI_DATA_DIR` — but only
  `cache\`, `instances\`, `bin\` and `skill-installs.json` follow it.
  Skill candidates live under `~\<agent>\skills\revit-cli` (or
  `.<agent>\skills` depending on the agent) and `configure setup/teardown`
  touch `%APPDATA%\Autodesk\Revit\Addins`; both are the user's real
  install regardless of the data dir. A check run without the variable
  inspects (and reports) the real user install — assert the printed
  `Data directory:` matches scratch before trusting anything else.
- The skill registry maps agent ids to dot-dirs (`.claude`, `.cursor`,
  `.trae`, ...); `--list` output is the authoritative candidate set on
  this machine.
