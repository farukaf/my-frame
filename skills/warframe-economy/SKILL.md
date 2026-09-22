---
name: warframe-economy
description: Evaluate prices, reserves, sales, and completion cost without double-counting.
metadata:
  version: "2"
---

# Economy

Follow the shared contract and record each price's source and age. `market.private` is
optional: missing account data does not mean missing orders. Missing, stale, or partial
prices remain uncertain.

1. Query inventory and collection in the same snapshot.
2. Separate construction reserve, collection reserve, existing orders, and future sale.
3. Use `list_sales`, `list_surplus`, and `list_farm` as distinct projections; never add
   their totals together.
4. State unit, update time, coverage, unquoted components, and deterministic
   `reasonCode`.
5. Calculate completion cost only from known prices.

Return the decision, free quantity, reserve, evidence, price and age, uncertainty, and
a reversible action. Do not create orders, authenticate, or send data to the market.
