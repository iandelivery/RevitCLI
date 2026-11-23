# Configure and skill management (read-only paths)

## Sub-features

- `configure check` — running instances, installed add-ins, per-instance
  health probes, then `DataDir()`, `CacheDir()`, `InstancesDir()`, staged
  binary status. Read-only; exit 0.
- `configure port` — table (or `--json`) of supported versions 2019-2022
  with base ports 5011/5021/5031/5041 and live port/PID from discovery.
  Read-only.
- `skill show` — version, exe path, SKILL.md source, staged binary state
  (`not staged` / `version unknown` / `stale` / current), install
  candidates and locations. Read-only.
- `skill install --list` — detected agent directories (claude, cursor,
  codex, trae, trae-cn, codebuddy, workbuddy, kimi-work, kimi-code);
  prints candidates, writes nothing. Read-only.
- MUTATING (out of scope for routine drives): `configure setup`/`teardown`
  (installs/removes the user's bridge add-in), `skill install/update/
  uninstall` (writes SKILL.md + staged binary into agent dirs). Drive them
  only against scratch `--target` dirs with explicit `--force` awareness.

## How to get to it (user POV)

"Is my install healthy?" — check; "which agents can I register?" — list;
"where is my skill installed?" — show.

## Driving it with the terminal (scratch data dir)

```powershell
$env:REVIT_CLI_DATA_DIR = $SCRATCH        # MUST be set: check prints DataDir
Run "configure-check" @("configure","check")   # exit 0; "Data directory:" == $SCRATCH
Run "configure-port"  @("configure","port")    # exit 0; 2019->5011 ... 2022->5041
Run "configure-port-json" @("configure","port","--json")
Run "skill-show"  @("skill","show")             # exit 0; reports SKILL.md source
Run "skill-list"  @("skill","install","--list") # exit 0; candidate agent dirs only
```

Optional (mutating, scratch-scoped): `skill install --target
"$SCRATCH\agents\claude"` then assert the manifest
`.revit-cli-skill.json`, the staged binary under `<data-dir>\bin\`, and the
rewritten binary-path line in the installed SKILL.md; `skill uninstall`
afterwards. Never point install at the user-level dirs from a verification
run.

## Gotchas

- `configure check`'s health probes use plain HTTP without auth headers —
  against a real authenticated bridge they may report unreachable even
  when `ping` works; that is expected, not drift.
- All configure/skill state keys off `REVIT_CLI_DATA_DIR`; a check run
  without it inspects (and reports) the real user install — assert the
  printed `Data directory:` matches scratch before trusting anything else.
- The skill registry maps agent ids to dot-dirs (`.claude`, `.cursor`,
  `.trae`, ...); `--list` output is the authoritative candidate set on
  this machine.
