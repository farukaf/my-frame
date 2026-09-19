import { test } from "node:test";
import assert from "node:assert/strict";
import { readInventoryResult } from "./read-inventory-result.mjs";

for (const code of ["SNAPSHOT_EXPIRED", "CURSOR_EXPIRED", "SETUP_REQUIRED", "SOURCE_UNAVAILABLE"]) {
  test(`${code} is not an empty inventory`, () => {
    const result = { isError: true, content: [{ type: "text", text: `${code}: synthetic failure.` }] };
    // Reproduce the old consumer's false-empty behavior.
    assert.deepEqual(result.structuredContent?.items ?? [], []);
    assert.throws(() => readInventoryResult(result), error =>
      error.code === code && error.requiresNewSnapshot === code.endsWith("EXPIRED"));
  });
}

test("an error wins even if structured content is present", () => {
  assert.throws(() => readInventoryResult({ isError: true,
    structuredContent: { items: [], meta: { snapshotId: "synthetic" } } }),
  error => error.code === "MCP_TOOL_ERROR");
});

test("a successful empty result retains its metadata", () => {
  const page = { items: [], count: 0, totalCount: 0, nextCursor: null,
    meta: { snapshotId: "synthetic", complete: false, warnings: [{ code: "PRICE_STALE" }] } };
  assert.equal(readInventoryResult({ structuredContent: page }), page);
});

test("missing or malformed content is a contract failure", () => {
  for (const result of [null, {}, { structuredContent: {} },
    { structuredContent: { items: [] } },
    { structuredContent: { items: {}, meta: { snapshotId: "synthetic" } } }]) {
    assert.throws(() => readInventoryResult(result), TypeError);
  }
});

test("nonempty page and pagination are preserved", () => {
  const page = { items: [{ itemId: "/synthetic", quantity: null }],
    nextCursor: "opaque", meta: { snapshotId: "synthetic" } };
  assert.equal(readInventoryResult({ isError: false, structuredContent: page }), page);
});
