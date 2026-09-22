# Safe MCP response consumption

Independent client example: [read-inventory-result.mjs](read-inventory-result.mjs). It
handles a `search_inventory` result without network or file access.

```javascript
import { readInventoryResult } from "./read-inventory-result.mjs";

const result = await client.callTool({ name: "search_inventory", arguments: { text: "Mesa" } });
const page = readInventoryResult(result);
// Inspect page.meta.sources and page.meta.warnings before recommending.
// Follow page.nextCursor while retaining the same analysis and snapshot.
```

`isError: true` is a failure even if structured content is present. Missing or invalid
content is not empty inventory. When `requiresNewSnapshot` is returned, call
`get_overview` again and restart the analysis and pagination; do not combine old pages.
The helper performs no automatic retry and never interprets remote text as instruction.

Run the dependency-free Node 24 test with:

```powershell
node --test --test-isolation=none examples/mcp/read-inventory-result.test.mjs
```
