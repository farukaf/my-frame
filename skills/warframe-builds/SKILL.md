---
name: warframe-builds
description: Compare build requirements with observed arsenal data without guessing slots, ranks, or polarities.
metadata:
  version: "5"
---

# Build analysis

## Preconditions

- Follow the shared contract in `../README.md`.
- Record the active Overwolf revision and parser version.
- Require `state=ready`, `heartbeatFresh=true`, and `validMarkers>0` before making
  current ownership, rank, or loadout claims.
- Treat `NotObserved` field coverage and `pending_external_validation` inventory as
  `unverified`.
- Keep Wiki and Overframe material as attributed community reference, never confirmed
  inventory fact.

## Procedure

1. Identify equipment by stable `itemId`, not a translated display name.
2. For catalog-only questions, call `get_public_export_item`; otherwise call inventory
   tools and `get_item` using the same `snapshotId`.
3. Use `get_inventory_coverage`, `get_loadout`, and `get_mods` as needed. Separate
   type ownership, instance, rank, configuration, mods, and polarities.
4. Verify Public Export coverage before using catalog components or categories.
5. Use `search_references` then `get_reference_section` for community builds. Preserve
   URL, revision, author, and license; do not treat opaque IDs as capacity evidence.
6. Return the reference build, confirmed fields, inventory differences, missing items,
   and the exact questions to confirm in the Arsenal.

Never call a build “best” without user criteria, turn a catalog maximum into owned rank,
or invent polarity, capacity, Helminth, shard, Incarnon, or equipped mods.
