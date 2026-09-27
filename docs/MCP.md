# MCP contract

`MyFrame.Mcp` is a local server over `stdio`. It is read-only, idempotent, offline,
and does not migrate storage. Standard output is reserved for MCP protocol messages;
diagnostics go to standard error.

## Client rules

- Call `get_capabilities`, `get_sync_status`, and `get_overview` before domain queries.
- Retain the returned `snapshotId` and follow `nextCursor` pages.
- Treat `isError`, `problem`, coverage, source state, revision, parser version, and
  freshness as part of every result.
- Never treat missing, partial, stale, or unverified data as zero or complete.
- Check `get_capture_inbox_status` before claiming current personal inventory.

## Registered tools

### Discovery and status

- `get_capabilities`
- `get_sync_status`
- `get_sync_history`
- `get_source_coverage`
- `get_capture_inbox_status`
- `get_market_credential_status`
- `get_overview`

### Inventory and catalog

- `search_inventory`
- `get_item`
- `get_equipment`
- `get_loadout`
- `get_mods`
- `get_inventory_coverage`
- `get_inventory_history`
- `get_inventory_changes`
- `get_public_export_item`
- `search_public_export`

### Activities and acquisition

- `get_world_state`
- `get_bounties`
- `get_activity`
- `get_acquisition`

### Recommendations and build listings

- `list_collection`
- `list_farm`
- `list_relics`
- `list_sales`
- `list_surplus`
- `search_overframe_builds`

Inventory change comparison returns `context_mismatch` and no items when both revisions
have different known contexts. `search_overframe_builds` requires a query and an exact
`Item`, `Mod`, or `Warframe` type. It returns only cached Overframe build titles and
links. Refreshes run through `MyFrame.Sync --overframe-builds`, never through MCP; build
details are outside the supported contract and listing data is not trusted for facts.
The server never returns raw payloads, credentials, or arbitrary local files.
