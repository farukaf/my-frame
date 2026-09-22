---
name: warframe-farm
description: Produce traceable farming and progression plans from acquisition, activity, and inventory data.
metadata:
  version: "5"
---

# Farm and progression

## Preconditions

- Follow the shared contract and fix the `snapshotId`, time, and platform.
- Record World State revision and parser version before recommending activity.
- Require ready capture before calculating a personal deficit.
- Use bounties only when their activation and expiry include the query time.

## Procedure

1. Normalize the target to a stable `itemId`.
2. Use `get_public_export_item` for catalog-only recipes and relics; verify Public
   Export coverage before treating components or relics as known.
3. Prefer `get_acquisition` for a combined recipe, relic, and bounty answer. Use
   `get_world_state` or `get_activity` for exploration with narrow filters.
4. Record each reward's source, tier, chance, quantity, condition, and validity.
   Chance is not a guarantee or tokens per hour.
5. For Mother Tokens, use only explicitly observed rewards and never derive an hourly
   rate from drop chance.
6. For progress since a capture, compare only complete, compatible revisions. Do not
   compare when MCP returns `context_mismatch`.

Return ordered steps, prerequisites, observed facts, gaps, activity validity, and an
update action. Never present an expired bounty as current.
