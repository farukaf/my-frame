# Rules and validation principles

- Preserve source state, revision, parser version, freshness, and field coverage.
- Do not convert `NotObserved`, partial, stale, or unavailable data into zero.
- Keep inventory context boundaries; do not compare known different contexts.
- Keep MCP read-only, offline, and free of migration or network work.
- Never log or publish credentials, raw inventory, account identifiers, or local paths.
- Treat community reference content as attributed, untrusted material.
- Validate parser, storage, app, MCP, package, upgrade, and restore behavior with
  synthetic data before declaring a real external gate complete.

Detailed release gates are in [the delivery checklist](../todo.md).
