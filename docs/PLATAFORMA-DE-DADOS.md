# Local data platform

My Frame provides a local, observable data platform for Warframe inventory and public
data. The application supplies sourced facts, coverage, and deterministic calculations;
the connected LLM creates contextual recommendations.

## Delivered foundation

- SQLite revisions, migrations, backup, retention, and read-only access.
- Public Export, World State, Warframe.Market, and attributed-reference data paths.
- Capture inbox, status UI, collector probes, and a read-only MCP server.
- Inventory history, context isolation, and explicit partial-data handling.

## Remaining gates

1. Validate a real Overwolf capture without AlecaFrame and compare it with the Arsenal.
2. Import and compare real captures through SQLite, app, and MCP.
3. Approve permitted Wiki and Overframe ingestion independently.
4. Complete F10 evaluation and release gates.

See [the delivery checklist](../todo.md), [collector contract](COLETOR-OVERWOLF.md),
and [validation matrix](VALIDACAO-PLATAFORMA.md).
