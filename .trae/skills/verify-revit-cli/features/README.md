# revit-cli Feature Map

Verification source of truth for `client\skills\revit-cli\revit-cli.exe`. Each
file describes one user-facing feature: what it is, how a user reaches it,
how to drive it with the harness (terminal + `helpers/mock_bridge.py`), and
what observable end state proves it works.

Harness setup (Launch/Doctor/Evidence/Cleanup) is defined in
[../SKILL.md](../SKILL.md). All examples assume `$cli` = the built binary,
`$PORT` = mock bridge port (25911-25919), `$SCRATCH` = isolated
`REVIT_CLI_DATA_DIR`, `$EV` = evidence dir.

| Feature | File | Needs bridge |
|---------|------|--------------|
| Connection commands and the exit-code contract | [connection-and-exit-codes.md](connection-and-exit-codes.md) | yes (dead-port drives need none) |
| Instance discovery, list enrichment, targeting | [instance-discovery.md](instance-discovery.md) | no (list) / yes (ping, enrichment) |
| Schema discovery: commands / catalog / schema, cache, help | [schema-discovery.md](schema-discovery.md) | yes |
| Dynamic command execution (flags, units, SSE, tasks, dedup) | [dynamic-commands.md](dynamic-commands.md) | yes |
| Configure and skill management (read-only paths) | [configure-and-skill.md](configure-and-skill.md) | no |

Rule: a proof that drives one convenient entry point is incomplete when this
map lists others — cover the mapped journeys, and record failures honestly in
the feature's Gotchas instead of weakening the journey.
