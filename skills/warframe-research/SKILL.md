---
name: warframe-research
description: Research imported Wiki and Overframe references with attribution, revision, and explicit trust boundaries.
metadata:
  version: "2"
---

# Attributed research

Use this skill for Wiki or Overframe context, community builds, mechanics explanations,
or source comparison. It does not replace synchronized game data.

1. Follow the shared contract and retain `snapshotId` when player data is involved.
2. Call `search_references` with a short, specific query.
3. For every cited result, call `get_reference_section` using the returned URL,
   `sectionId`, and revision. Preserve URL, revision, type, author, and license.
4. Treat material as `trustedForFacts=false`: it is a reference or hypothesis, not
   confirmation of inventory, reward, chance, or current rule.
5. If references are unavailable, empty, or uninitialized, say so. Do not scrape or
   make network calls as a fallback.

Clearly separate synchronized facts, deterministic calculation, attributed community
reference, and LLM hypothesis. State what still needs in-game or official-source
confirmation.
