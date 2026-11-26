# Configure and skill management (read-only paths + one scratch install)

## Sub-features

- `configure check` — running instances, installed add-ins, per-instance
  health probes, then `DataDir()`, `CacheDir()`, `InstancesDir()`, staged
  binary status. Exit 0 always. **Not strictly read-only**: the discovery
  step deletes stale (dead-PID) registry files, and `DataDir()` mkdirs the
  data dir when it does not exist.
- `configure port` — table (or `--json`) of supported versions **2019-2024**
  with base ports 5011/5021/5031/5041/5051/5061 (`5000+(v-2018)*10+1`) and
  live port/PID from discovery. Read-only.
- `skill show` — version, exe path, SKILL.md source, staged binary state
  (`not staged` / `version unknown` / `stale` / current), install
  candidates and locations. Read-only.
- `skill install --list` — detected agent directories (claude, cursor,
  codex, trae, trae-cn, codebuddy, workbuddy, kimi-work, kimi-code);
  prints candidates, writes nothing. Read-only.
- MUTATING (scratch-scoped only): `configure setup`/`teardown` (installs /
  removes the bridge add-in; `setup` takes no flags, reads `BRIDGE_DIR`
  or `bridge\Revit<v>\` and needs a built `Revit2023`/`Revit2024` dir for
  those versions), `skill install/update/uninstall` (writes SKILL.md +
  staged binary into agent dirs; flags `--agent|--target|--project|--all|
  --force|--list|--json`, default scope user-level `~`). Drive them only
  against scratch `--target` dirs.

## How to get to it (user POV)

"Is my install healthy?" — check; "which agents can I register?" — list;
"where is my skill installed?" — show.

## Driving it with the terminal (scratch data dir)

```powershell
$env:REVIT_CLI_DATA_DIR = $SCRATCH        # MUST be set: check prints DataDir
Run "configure-check" @("configure","check")   # exit 0; "Data directory:" == $SCRATCH
Run "configure-port"  @("configure","port")    # exit 0; six rows: 2019->5011 ... 2023->5051, 2024->5061
Run "configure-port-json" @("configure","port","--json")   # six objects
Run "skill-show"  @("skill","show")             # exit 0; reports SKILL.md source
Run "skill-list"  @("skill","install","--list") # exit 0; candidate agent dirs only
```

Optional (mutating, scratch-scoped; `--target X` installs into `X\revit-cli\`):

```powershell
$env:USERPROFILE = "$SCRATCH\home"       # skill show/--list/default scope read the REAL ~ otherwise
Run "skill-install" @("skill","install","--target","$SCRATCH\agents\claude","--json")
# exit 0; assert: "$SCRATCH\agents\claude\revit-cli\.revit-cli-skill.json",
#                 "$SCRATCH\bin\revit-cli.exe", "$SCRATCH\skill-installs.json",
#                 the installed SKILL.md references the staged exe path
Run "skill-uninstall" @("skill","uninstall","--target","$SCRATCH\agents\claude","--json")
# exit 0; "removed"; the staged exe is dropped once nothing else references it
```

Never point install at the user-level dirs from a verification run.

## Gotchas

- `configure check`'s health probes use plain HTTP without auth headers and
  **ignore the HTTP status** — a 401 prints "healthy"; only a transport
  error prints "unreachable". That is the code, not drift.
- Add-in status in `check`/`teardown` is unreliable for versions detected
  only through the registry (their `AddinsDir` stays empty until a
  `configure setup` back-fills it): it tests cwd-relative paths. Do not
  read add-in health from `check` on a machine you did not `setup` from.
- Only the install index and the staged binary key off
  `REVIT_CLI_DATA_DIR`; `skill show`, `--list` and the default install
  scope read the real `~` and cwd. Assert the printed `Data directory:`
  matches scratch, and set `USERPROFILE` to a scratch home for skill
  drives.
- The skill registry maps agent ids to dot-dirs (`.claude`, `.cursor`,
  `.trae`, ...); `--list` output is the authoritative candidate set on
  this machine.
- There is no `--source` flag on `configure setup`; the bridge source is
  `BRIDGE_DIR` → exe dir → `<exe>\bridge\Revit<v>\`.
