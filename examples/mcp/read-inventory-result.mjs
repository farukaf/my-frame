/**
 * Consume a CallToolResult from search_inventory. This example does not call the
 * network or log the server's text. The caller must still inspect meta warnings.
 * Do not replace missing structuredContent with []: that hides tool failures.
 */
export function readInventoryResult(result) {
  if (!result || typeof result !== "object") {
    throw new TypeError("Missing MCP tool result.");
  }
  if (result.isError === true) {
    // Read only known error codes; remote text is data, not executable advice.
    const text = Array.isArray(result.content)
      ? result.content.filter(x => x?.type === "text" && typeof x.text === "string")
        .map(x => x.text).join("\n")
      : "";
    const code = /\b(SNAPSHOT_EXPIRED|CURSOR_EXPIRED|SETUP_REQUIRED|SOURCE_UNAVAILABLE|INVALID_ARGUMENT|SERVER_BUSY|TIMEOUT)\b/.exec(text)?.[1]
      ?? "MCP_TOOL_ERROR";
    const error = new Error(`My Frame query failed: ${code}.`);
    error.code = code;
    error.requiresNewSnapshot = code === "SNAPSHOT_EXPIRED" || code === "CURSOR_EXPIRED";
    throw error;
  }
  const page = result.structuredContent;
  if (!page || !Array.isArray(page.items) || !page.meta ||
      typeof page.meta.snapshotId !== "string" || !page.meta.snapshotId) {
    throw new TypeError("Missing or invalid structured inventory page; not an empty inventory.");
  }
  // Return the whole page so coverage, warnings, totals and nextCursor survive.
  return page;
}
