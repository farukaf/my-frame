import { test } from "node:test";
import assert from "node:assert/strict";

test("UI initializes without reading collector before its constructor returns", async () => {
  const nodes = new Map();
  const node = id => {
    if (!nodes.has(id)) nodes.set(id, { textContent: "", disabled: false, checked: false, value: "" });
    return nodes.get(id);
  };
  const saved = { document: globalThis.document, overwolf: globalThis.overwolf, addEventListener: globalThis.addEventListener };
  try {
    globalThis.document = { getElementById: node, querySelectorAll: () => [] };
    globalThis.overwolf = { games: {} };
    globalThis.addEventListener = () => {};
    await import(`../ui.mjs?test=${crypto.randomUUID()}`);
    assert.match(node("availability").textContent, /Pronto/);
    assert.equal(JSON.parse(node("status").textContent).state, "stopped");
    assert.equal(typeof node("start").onclick, "function");
  } finally {
    for (const [key, value] of Object.entries(saved)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  }
});
