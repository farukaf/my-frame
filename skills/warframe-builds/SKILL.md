---
name: warframe-builds
description: Compare build requirements with observed arsenal data without guessing slots, ranks, or polarities.
metadata:
  version: "6"
---

# Build analysis

## Preconditions

- Follow the shared contract in `../README.md`.
- Call `get_capture_inbox_status` before using personal inventory.
- Record the active Overwolf revision and parser version.
- Require `state=ready`, `heartbeatFresh=true`, and `validMarkers>0` before making
  current ownership, rank, or loadout claims.
- Treat `NotObserved` field coverage and `pending_external_validation` inventory as
  `unverified`.
- Treat Overframe build titles and links as unverified community listings, never as
  build details or confirmed inventory facts.

## Procedure

1. Identify equipment by stable `itemId`, not a translated display name.
2. For catalog-only questions, call `get_public_export_item`; otherwise call inventory
   tools and `get_item` using the same `snapshotId`.
3. Use `get_inventory_coverage`, `get_loadout`, and `get_mods` as needed. Separate
   type ownership, instance, rank, configuration, mods, and polarities.
4. Verify Public Export coverage before using catalog components or categories.
5. Use `search_overframe_builds` only to list cached build titles and links for a typed
   search. Do not claim the listing contains mod, capacity, polarity, or configuration data.
6. Return the matching build links, confirmed inventory fields, missing items, and the
   exact questions to confirm in the Arsenal.

Never call a build “best” without user criteria, turn a catalog maximum into owned rank,
or invent polarity, capacity, Helminth, shard, Incarnon, or equipped mods.
