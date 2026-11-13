# Changelog

All notable, user-visible changes are documented here. New sections are
auto-generated at release time by the Release workflow from conventional
commits (feat/fix only) — do not edit released sections. Historical releases
before v1.12.0 live on the
[GitHub Releases page](https://github.com/iandelivery/RevitCLI/releases).

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
