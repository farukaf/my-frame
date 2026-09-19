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
    const writes = [];
    globalThis.document = { getElementById: node, querySelectorAll: () => [] };
    const event = { addListener: () => {}, removeListener: () => {} };
    globalThis.overwolf = { games: {
      onGameInfoUpdated: event,
      getRunningGameInfo2: callback => callback({ success: true, gameInfo: null }),
      events: { onInfoUpdates2: event, onError: event,
        setRequiredFeatures: (features, callback) => callback({ success: true, supportedFeatures: features }) }
    }, io: {
      paths: { localAppData: "C:\\Users\\test\\AppData\\Local" },
      enums: { eEncoding: { UTF8: 1 } },
      writeFileContents: (path, text, encoding, append, callback) => {
        writes.push({ path, text, encoding, append }); callback({ success: true });
      }
    } };
    globalThis.addEventListener = () => {};
    await import(`../ui.mjs?test=${crypto.randomUUID()}`);
    assert.match(node("availability").textContent, /Pronto/);
    assert.equal(JSON.parse(node("status").textContent).state, "stopped");
    assert.equal(node("folder").value, "C:\\Users\\test\\AppData\\Local\\MyFrame\\captures");
    assert.equal(typeof node("start").onclick, "function");
    await node("start").onclick();
    assert.equal(writes.length, 1);
    assert.equal(writes[0].path, "C:\\Users\\test\\AppData\\Local\\MyFrame\\captures\\collector-status.json");
    assert.equal(JSON.parse(writes[0].text).state, "started");
  } finally {
    for (const [key, value] of Object.entries(saved)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  }
});
