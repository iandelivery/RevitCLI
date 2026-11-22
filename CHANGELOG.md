# Changelog

All notable, user-visible changes are documented here. A new section is
added at release time summarizing the notable changes (the Release
workflow generates the GitHub release description from conventional
commits — feat/fix only). Do not edit released sections. Historical
releases before v1.12.0 live on the
[GitHub Releases page](https://github.com/iandelivery/RevitCLI/releases).

## v2.0.1 (2026-11-22)

- **`get_units` answers without a document** — a cold-start agent (no RVT
  open) no longer gets "No active document."; the command returns the
  canonical and allowed matrices with `project: null` until a document
  opens.
- **Unit errors list what is valid** — an unknown kind or unit in the
  `units` field now names the allowed values in the error message, so an
  agent can self-correct in one round trip.
- **Client help shows unit kinds** — dynamic `--help` annotates every
  dimensional parameter with `[unit kind: <kind>]`.

## v2.0.0 (2026-11-22)

- **The units contract — an agent never sees a Revit internal unit.** Every
  dimensional value a command takes or returns is expressed in the request
  unit of its kind, and every response carries a `units` echo naming the
  units used. Canonical defaults: `length=mm`, `area=m2`, `volume=m3`,
  `angle=deg`; a request may choose any allowed unit per kind or the string
  `project` to follow the active document's decimal display units. Outputs
  round to 3 decimals; inputs are never rounded. Conversion happens at one
  middleware choke point (schema `unit_kind` inputs, `ResponseFieldKinds`
  outputs, `ParameterUnits` for spec-driven parameter commands); raw C#
  scripts via `execute_raw` are unchanged.
- **Client `--units` flag** — `--units length=ft --units angle=rad`
  (repeatable kind=unit pairs) or `--units project` selects the units of an
  invocation; invalid specs fail with a usage error before connecting.
- **`get_units`** — new Query command returning the document's unit matrix
  (what `project` resolves to), the canonical defaults, and the units
  allowed per kind.
- **`get_parameters` reports `unit_kind` per parameter** and converts double
  values through the request units; `set_parameter`/`batch_set_param`
  convert double inputs from the request units — the parameter commands no
  longer leak bare internal feet.
- **Suffixed field names removed** — `diameter_mm`, `width_mm`, `height_mm`,
  `offset_mm`, `length_mm`, `base_offset_mm`, `rotation_deg`,
  `overlap_area_mm2` and their kin rename to unsuffixed names; the `units`
  echo replaces suffixes as the contract. **Breaking:** update any parser
  that keyed on the old names or assumed raw feet.
- **Handlers are unit-blind** — geometry, modification, structural, MEP
  (piping, mechanical, electrical, fittings), and view/sheet commands
  declare kinds instead of converting by hand; `create_walls`-style nested
  arrays and JSON-string payloads convert the same as scalars.

## v1.19.1 (2026-11-20)

- **`http://127.0.0.1:{port}` no longer rejected** — the bridge bound only
  the `http://localhost:{port}/` prefix, so requests addressed to the raw
  loopback IP were answered by HTTP.sys with 400 "Invalid Hostname" — an
  error indistinguishable from the bridge not being there at all, which
  derailed troubleshooting toward discovery and auth. The listener now
  registers both loopback prefixes (still loopback-only, nothing
  external); `localhost` keeps working unchanged.

## v1.19.0 (2026-11-19)

- **Raw execution on by default** — every installed bridge now ships
  `allow_raw_execution: true`: the `configure setup` installer, local
  builds (`build.ps1`), and release artifacts all write the enabled
  default (previously the C# default said enabled while every generated
  config disabled it). The `raw-mode` command still toggles it at
  runtime, and the toggle remains in-memory — restart resets to the
  config value.
- **Multi-instance auth just works** — a second bridge instance of the
  same Revit version auto-allocates the next port in the version's
  range, and the instance registry file now carries that instance's
  `api_key` next to its port, so the client authenticates against every
  running instance without re-running `configure setup`.
- **Clearer 401 errors** — auth failures no longer hide behind
  "Unknown command": the client prints the URL plus the matching remedy
  (no key sent → run `configure setup`; key sent and refused → re-sync
  the rotated config; old bridge that publishes no key → restart Revit
  after an add-in update) and exits with code 2.
- **Docs** — install and skill-install examples no longer `cd` to a
  fixed `C:\revit-cli` path, and every doc that claimed the raw-mode
  default now agrees with the code.

## v1.18.0 (2026-11-17)

- **Retry-safe commands** — pass `--request-id <id>` on any command and the
  bridge returns the existing task instead of running it twice. A timed-out
  task now stays `timeout` permanently (previously it could flip back to
  `completed` when Revit finished the work late, so a retried command could
  run twice).
- **Detached long tasks** — `--no-wait` submits a command, prints
  `task_id: <id>`, and exits 0 immediately; poll later with
  `revit-cli.exe task -ti <id>`. `--timeout <seconds>` raises the
  per-command budget (default 120) for long `batch_export` or `execute_raw`
  runs. The `task_id` is also printed on the SSE `accepted` event, so a
  killed process leaves its ID behind.
- **Documented exit codes** — 0 success, 1 usage error, 2 bridge
  unreachable or auth rejected, 3 timed out, 4 command failed. stdout now
  carries the result JSON only; progress, `task_id`, and errors go to
  stderr, so `revit-cli.exe <command> 2>$null` yields parseable JSON.
- **Self-service discovery** — dynamic commands answer `--help` with their
  schema (description, parameters, defaults, examples), unknown names get
  up to three "Did you mean" suggestions from both the client (offline via
  the schema cache) and the bridge, and `--dry-run` is rejected locally for
  commands whose schema says unsupported.
- **`execute_raw --stdin`** — pipe a C# script instead of fighting shell
  quoting on Windows (`Get-Content script.cs -Raw | revit-cli.exe
  execute_raw --stdin`).
- **`resolved_failures` reporting** — when Revit auto-resolves model errors
  during a `create_walls` commit, the response now carries a
  `resolved_failures` array so agents can tell a clean commit from a
  corrected one.
- **Configurable task retention** — `task_retention_seconds` in
  `cli_bridge_setting.json` (default 900) controls how long finished task
  results stay queryable; failed and timed-out tasks are kept for an hour,
  and the task 404 response explains the window.
- **Docs** — SKILL.md now documents the exit-code/stream contract, the
  correct `raw-mode` command name, and `--stdin`; the READMEs cover the
  new global flags; Dependabot no longer opens nuget PRs against the
  Revit-pinned bridge.

## v1.17.2 (2026-11-16)

- **Skill installs default to user level** — `skill install` now roots
  candidate directories at the user profile (`~\.claude`, `~\.trae`, ...)
  instead of the current working directory, so a plain install no longer
  drops `SKILL.md` inside the folder the terminal happened to be in
  (e.g. the extracted release zip). `--project` opts into project-level
  (cwd) installs and replaces the `--global` flag, which is removed —
  passing it is harmless: it now behaves as the user-level default.
- **Build**: local bridge builds (`bridge\build.ps1`) stamp the real
  semantic version from the latest reachable `v*` tag instead of the
  stale csproj placeholder, matching release binaries.
- **Docs**: documented third-party plugin registration and deployment
  (register via `BridgeRegistration`, never ship local
  `RevitCliBridge.Abstractions.dll` / `Newtonsoft.Json.dll` copies,
  verify with `revit-cli catalog`), added `/api/catalog` to the API
  table, and synced README Go-version references to 1.26.

**Full Changelog**: https://github.com/iandelivery/RevitCLI/compare/v1.17.1...v1.17.2

## v1.17.1 (2026-11-13)

Maintenance release — no feature or behavior changes.

- **Toolchain**: Go floor raised from EOL 1.22 to 1.26.11 (go.mod, CI,
  release build, READMEs), shipping binaries against a fully patched
  standard library.
- **Supply-chain hardening**: CI now runs golangci-lint, govulncheck,
  NuGet vulnerability scans, and gitleaks on every PR; all third-party
  GitHub Actions are pinned by commit SHA; Dependabot keeps Go, NuGet,
  and action dependencies current.
- **Dependency updates**: Newtonsoft.Json 13.0.4, xunit 2.9.3,
  xunit.runner.visualstudio 4.0.1, Microsoft.NET.Test.Sdk 18.12.0,
  XMLDoc2Markdown 6.0.0, and GitHub Actions major bumps (checkout v4,
  cache v6, upload-artifact v7, download-artifact v8, paths-filter v4,
  action-gh-release v3, golangci-lint-action v9).

**Full Changelog**: https://github.com/iandelivery/RevitCLI/compare/v1.17.0...v1.17.1

## v1.17.0 (2026-11-13)

### What's Changed
#### Features
- [feat(bridge): enable raw execution by default, drop Python mode](https://github.com/iandelivery/RevitCLI/commit/a7b3737)

#### Bug Fixes
- [fix(bridge): reference the bridge assembly in execute_raw compilation](https://github.com/iandelivery/RevitCLI/commit/335bb33)
- [fix(bridge): drop Python mention from execute_raw description](https://github.com/iandelivery/RevitCLI/commit/ab4cb5b)
- [fix(bridge): serialize shared log writes across bridge processes](https://github.com/iandelivery/RevitCLI/commit/4bec6ee)
- [fix(bridge): compare search_elements parameters by storage type](https://github.com/iandelivery/RevitCLI/commit/539c0b7)
- [fix(bridge): reject non-boolean values for strict bool parameters](https://github.com/iandelivery/RevitCLI/commit/b5b71e8)
- [fix(bridge): clear all build warnings and enforce zero-warning builds](https://github.com/iandelivery/RevitCLI/commit/b5a6788)

**Full Changelog**: https://github.com/iandelivery/RevitCLI/compare/v1.16.0...v1.17.0

## v1.16.0 (2026-11-12)

### What's Changed
#### Features
- [feat(bridge): query elements in linked documents](https://github.com/iandelivery/RevitCLI/commit/d5eb6f6)
- [feat(client): show live open documents in list command](https://github.com/iandelivery/RevitCLI/commit/c4e6a6c)
- [feat(bridge): add GET /api/documents endpoint](https://github.com/iandelivery/RevitCLI/commit/27d2bd2)

#### Bug Fixes
- [fix(bridge): name the selector when no placed link matches](https://github.com/iandelivery/RevitCLI/commit/0cbbb42)
- [fix(bridge): recover linked docs GetLinkDocument misses on Revit 2019](https://github.com/iandelivery/RevitCLI/commit/652a4e4)
- [fix(build): preserve deployed api_key during -Deploy](https://github.com/iandelivery/RevitCLI/commit/f34ebec)
- [fix(client): treat bare bool flags on dynamic commands as true](https://github.com/iandelivery/RevitCLI/commit/d38e290)

**Full Changelog**: https://github.com/iandelivery/RevitCLI/compare/v1.15.0...v1.16.0

## v1.15.0 (2026-11-10)

### What's Changed
#### Features
- [feat(bridge): extend DryRunTransaction with rollback, snapshot commit and message helper](https://github.com/iandelivery/RevitCLI/commit/39b65cc)

#### Bug Fixes
- [fix(bridge): use language key in llms.txt execute_raw examples](https://github.com/iandelivery/RevitCLI/commit/ac33582)
- [fix(client): match execute_raw flags and language param to bridge schema](https://github.com/iandelivery/RevitCLI/commit/d597d45)
- [fix(client): accept global flags before schema/configure/skill sub-commands](https://github.com/iandelivery/RevitCLI/commit/eff6645)
- [fix(bridge): make error responses behave like errors end to end](https://github.com/iandelivery/RevitCLI/commit/307b58f)
- [fix(bridge): build create_wall result via snapshot commit and report dry_run](https://github.com/iandelivery/RevitCLI/commit/cc35642)
- [fix(bridge): make dry-run roll back in all handlers declaring SupportsDryRun](https://github.com/iandelivery/RevitCLI/commit/ded08dd)

**Full Changelog**: https://github.com/iandelivery/RevitCLI/compare/v1.14.0...v1.15.0

## v1.14.0 (2026-11-02)

### What's Changed
#### Features
- [feat(client): hint skill install after configure setup](https://github.com/iandelivery/RevitCLI/commit/4ad6520)
- [feat(client): add skill command to install SKILL.md into agent harnesses](https://github.com/iandelivery/RevitCLI/commit/a1a6f24)

#### Bug Fixes
- [fix(client): surface and deregister exe-dir ghost installs everywhere](https://github.com/iandelivery/RevitCLI/commit/239a41d)
- [fix(client): deregister the exe-dir install on uninstall instead of skipping](https://github.com/iandelivery/RevitCLI/commit/96b1809)
- [fix(client): refuse skill targets that alias the running executable's own files](https://github.com/iandelivery/RevitCLI/commit/e4ccd1f)
- [fix(client): copy skill references when target nests inside the source](https://github.com/iandelivery/RevitCLI/commit/5389b3f)

**Full Changelog**: https://github.com/iandelivery/RevitCLI/compare/v1.13.0...v1.14.0

## v1.13.0 (2026-10-30)

### What's Changed
#### Features
- [feat(bridge): add level and offset constraint parameters to structural handlers](https://github.com/iandelivery/RevitCLI/commit/5bad5c3)
- [feat(bridge): add structural columns and beams create/modify handlers](https://github.com/iandelivery/RevitCLI/commit/1ad2a8f)

#### Bug Fixes
- [fix(client): forward top-level payload fields such as dry_run](https://github.com/iandelivery/RevitCLI/commit/1469505)
- [fix(client): accept negative values and --flag=value form in FindArg](https://github.com/iandelivery/RevitCLI/commit/556844a)
- [fix(bridge): use STRUCTURAL_BEAM_END0/1_ELEVATION for beam level offsets](https://github.com/iandelivery/RevitCLI/commit/5a736cc)

**Full Changelog**: https://github.com/iandelivery/RevitCLI/compare/v1.12.1...v1.13.0

## v1.12.1 (2026-10-26)

See the [v1.12.1 release notes](https://github.com/iandelivery/RevitCLI/releases/tag/v1.12.1).

## v1.12.0 (2026-10-26)

See the [v1.12.0 release notes](https://github.com/iandelivery/RevitCLI/releases/tag/v1.12.0).
