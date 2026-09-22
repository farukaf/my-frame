# Data sources

| Source | Purpose | Trust boundary |
| --- | --- | --- |
| Overwolf GEP | Player inventory capture | Unverified until real-game validation. |
| Public Export | Item catalog, recipes, relics, technical metadata | Preserve revision and field coverage. |
| World State | Bounties, cycles, and attributed rewards | Check state, parser version, revision, and expiry. |
| Warframe.Market | Public prices and optional private account state | Credentials never enter MCP or logs. |
| Wiki | Attributed reference material | Disabled or fixture-only until permission and licensing pass. |
| Overframe public item pages | Attributed build reference cache | Sync only `/items/` URLs allowed by `robots.txt`; local structured cache remains untrusted for facts. |
| AlecaFrame | Optional legacy import | Read-only migration compatibility only. |

No source may turn missing data into a known zero. Source state, coverage, freshness,
and provenance must accompany a claim when applicable.

Overframe synchronization uses the published sitemap rather than a general web-search
provider. Defaults are two workers, a shared 800 ms delay between requests to the host,
and an eight-hour SQLite TTL. `--workers`, `--delay-ms`, and `--ttl-hours` override these
values. The MCP process remains offline and read-only.

Refresh one exact cache key with:

```powershell
dotnet run --project MyFrame.Sync -- --overframe-reference --type Item --term Haalvu
```

Cloudflare or another access challenge is reported as `OVERFRAME_ACCESS_BLOCKED`; the
sync process does not attempt to bypass it and the MCP does not substitute missing data.
