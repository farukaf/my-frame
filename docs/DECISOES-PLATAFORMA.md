# Platform decisions

| Decision | Current rule |
| --- | --- |
| Player capture | Use Overwolf only after real capture and distribution validation. |
| Storage | SQLite revisions, one coordinated writer, read-only app/MCP readers. |
| MCP | Local `stdio`, read-only, offline, no migrations or arbitrary SQL. |
| Public data | Preserve source, revision, parser version, coverage, and freshness. |
| Community content | Require permitted access, attribution, and explicit trust boundaries. |
| Recommendations | LLMs compose grounded advice; the app provides facts and deterministic calculations. |
| Privacy | Do not commit raw inventory, credentials, account identifiers, or personal paths. |

Open decisions are release gates, not completed work: real Overwolf distribution and
capture, permitted Wiki/Overframe ingestion, and final F10 evaluation.
