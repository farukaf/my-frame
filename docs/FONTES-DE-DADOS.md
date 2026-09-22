# Data sources

| Source | Purpose | Trust boundary |
| --- | --- | --- |
| Overwolf GEP | Player inventory capture | Unverified until real-game validation. |
| Public Export | Item catalog, recipes, relics, technical metadata | Preserve revision and field coverage. |
| World State | Bounties, cycles, and attributed rewards | Check state, parser version, revision, and expiry. |
| Warframe.Market | Public prices and optional private account state | Credentials never enter MCP or logs. |
| Wiki and Overframe | Attributed reference material | Disabled or fixture-only until permission and licensing pass. |
| AlecaFrame | Optional legacy import | Read-only migration compatibility only. |

No source may turn missing data into a known zero. Source state, coverage, freshness,
and provenance must accompany a claim when applicable.
