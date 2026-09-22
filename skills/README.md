# My Frame skills

These versioned procedures help LLM clients use My Frame MCP data safely. They do not
contain a fixed meta or replace the model's reasoning.

## Shared contract

1. Call `get_capabilities` and `get_sync_status`.
2. For personal-inventory questions, call `get_capture_inbox_status`.
3. Call `get_overview` and retain its `snapshotId` for every related query.
4. Handle `isError`, `problem`, coverage, source state, revision, and freshness before
   using returned facts. Follow all required `nextCursor` pages.
5. Separate observed facts, deterministic calculations, community references, and LLM
   hypotheses. Cite the source and revision, and state missing evidence.
6. Use `search_references` followed by `get_reference_section`; never fetch a URL or
   local path through MCP.
7. For inventory changes, use `get_inventory_history` then `get_inventory_changes`
   with compatible revisions. Treat `partial`, `insufficient_history`, or
   `context_mismatch` as incomplete comparison results.

If capture state is not `ready`, `heartbeatFresh` is false, or `validMarkers` is zero,
describe personal inventory as `unverified` and request synchronization or in-game
confirmation. Do not treat `NotObserved` catalog coverage as a known zero.

For catalog-only questions, use `get_public_export_item`. Use `get_item` only when the
answer must combine catalog information with ownership, loadout, or recommendations.

Available skills: `warframe-builds`, `warframe-farm`, `warframe-economy`, and
`warframe-research`.

## Language policy

Skills, prompts, examples, and generated repository content must be English. Localized
game names and external source fields may be retained only as data and must not become
repository-authored prose.
