# My Frame skills

These versioned procedures help LLM clients use My Frame MCP data safely. They do not
contain a fixed meta or replace the model's reasoning.

Current versions: build (v6), farm (v5), economy (v2).

## Shared contract

1. Call `get_capabilities` and `get_sync_status`.
2. For personal-inventory questions, call `get_capture_inbox_status`.
3. Call `get_overview` and retain its `snapshotId` for every related query.
4. Handle `isError`, `problem`, coverage, source state, revision, and freshness before
   using returned facts. Follow all required `nextCursor` pages.
5. Separate observed facts, deterministic calculations, external build listings, and
   LLM hypotheses. Cite the source and state missing evidence.
6. `search_overframe_builds` returns cached titles and links only. Never infer build
   contents from the listing or fetch a returned URL through MCP.
7. For inventory changes, use `get_inventory_history` then `get_inventory_changes`
   with compatible revisions. Treat `partial`, `insufficient_history`, or
   `context_mismatch` as incomplete comparison results.

If capture state is not `ready`, `heartbeatFresh` is false, or `validMarkers` is zero,
describe personal inventory as `unverified` and request synchronization or in-game
confirmation. Do not treat `NotObserved` catalog coverage as a known zero.

For catalog-only questions, use `get_public_export_item`. Use `get_item` only when the
answer must combine catalog information with ownership, loadout, or recommendations.

Available skills: `warframe-builds`, `warframe-farm`, and `warframe-economy`.

## Language policy

Skills, prompts, examples, and generated repository content must be English. Localized
game names and external source fields may be retained only as data and must not become
repository-authored prose.
