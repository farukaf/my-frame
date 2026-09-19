import { test } from "node:test";
import assert from "node:assert/strict";
import { Collector } from "../collector.mjs";
import { FEATURES, MAX_PAYLOAD_BYTES, nativeUpdates, inspectInventory, isWarframe, createCapture, sha256 } from "../probe.mjs";

function eventSource() {
  const listeners = new Set();
  return { addListener: x => listeners.add(x), removeListener: x => listeners.delete(x),
    emit: value => { for (const listener of listeners) listener(value); }, size: () => listeners.size };
}
function setup() {
  const calls = [];
  const events = { onInfoUpdates2: eventSource(), onError: eventSource(),
    setRequiredFeatures: (features, callback) => { calls.push(features); callback({ success: true, supportedFeatures: features }); },
    getInfo: callback => callback({ success: true, res: {} }) };
  const api = { games: { events, onGameInfoUpdated: eventSource(),
    getRunningGameInfo2: callback => callback({ success: true, gameInfo: null }) } };
  const collector = new Collector(api);
  collector.start();
  const startGame = () => api.games.onGameInfoUpdated.emit({ gameInfo: { isRunning: true, id: 89541, classId: 8954 } });
  return { api, collector, calls, startGame };
}
const inventory = value => ({ feature: "match_info", info: { match_info: { inventory: value } } });
const identity = username => ({ feature: "game_info", info: { game_info: { username } } });

test("only Warframe class/instance IDs activate collection", () => {
  assert.equal(isWarframe({ isRunning: true, id: 89541 }), true);
  assert.equal(isWarframe({ isRunning: false, classId: 8954 }), false);
  assert.equal(isWarframe({ isRunning: true, classId: 5426 }), false);
  assert.equal(isWarframe(null), false);
});
test("Native envelopes support object/string info and never chat", () => {
  const info = { game_info: { username: "private-name" }, match_info: { inventory: { Suits: [] } } };
  const result = nativeUpdates({ feature: "match_info", info: JSON.stringify(info) });
  assert.deepEqual(result.map(x => x.kind), ["identity", "inventory"]);
  assert.deepEqual(nativeUpdates({ feature: "chat", info }), []);
  assert.deepEqual(nativeUpdates({ gameId: 8954, feature: "match_info", key: "inventory", value: {} }), []);
});
test("probe preserves structural facts but no values, usernames or dynamic keys", () => {
  const report = inspectInventory({ Suits: [{ ItemId: { $oid: "private-id" }, Name: "private-name",
    Configs: [{ Upgrades: ["private-mod"] }] }], "private-key": { Password: "private-secret" } });
  const text = JSON.stringify(report);
  for (const secret of ["private-id", "private-name", "private-mod", "private-key", "private-secret", "Password"])
    assert.equal(text.includes(secret), false);
  assert.equal(report.publishable, false);
  assert.equal(report.completeness, "unverified");
  assert.ok(report.fields.some(x => x.path === "$.Suits[].Configs[].Upgrades"));
});
test("omitted collection, empty array and null remain distinct", () => {
  assert.equal(inspectInventory({}).fields.some(x => x.path === "$.Upgrades"), false);
  assert.equal(inspectInventory({ Upgrades: [] }).fields.find(x => x.path === "$.Upgrades").minLength, 0);
  assert.equal(inspectInventory({ Upgrades: null }).fields.find(x => x.path === "$.Upgrades").type, "null");
});
test("opaque, malformed, deeply nested, sampled and oversized inputs are bounded", () => {
  assert.equal(inspectInventory("encrypted-or-truncated-data").encoding, "opaque-string");
  assert.equal(inspectInventory("{broken").rootObject, false);
  assert.equal(inspectInventory({ Suits: Array.from({ length: 101 }, () => ({ Rank: 0 })) }).truncated, true);
  let deep = {}; for (let i = 0; i < 30; i++) deep = { Configs: deep };
  assert.equal(inspectInventory(deep).truncated, true);
  assert.throws(() => inspectInventory("x".repeat(MAX_PAYLOAD_BYTES + 1)), /PAYLOAD_LIMIT/);
});
test("start is idempotent, registers only documented non-chat features and stop detaches", () => {
  const { api, collector, calls, startGame } = setup();
  collector.start(); startGame();
  assert.deepEqual(calls, [[...FEATURES]]);
  assert.equal(api.games.events.onInfoUpdates2.size(), 1);
  collector.stop();
  assert.equal(api.games.events.onInfoUpdates2.size(), 0);
  assert.equal(collector.status.state, "stopped");
});
test("inventory events are deduplicated and reports never include raw data", async () => {
  const { collector, startGame } = setup(); startGame();
  const value = { Suits: [{ ItemType: "/Lotus/private-test" }] };
  await collector.receive(inventory(value)); await collector.receive(inventory(value));
  assert.equal(collector.status.duplicates, 1);
  assert.equal(collector.status.state, "inventoryObservedUnverified");
  assert.equal(JSON.stringify(collector.report()).includes("private-test"), false);
  assert.equal(collector.report().rawIncluded, false);
  collector.stop();
});
test("identity switch clears observations and creates a new session", async () => {
  const { collector, startGame } = setup(); startGame();
  await collector.receive(identity("account-one"));
  await collector.receive(inventory({ Suits: [] }));
  const session = collector.sessionId;
  await collector.receive(identity("account-two"));
  assert.notEqual(session, collector.sessionId);
  assert.equal(collector.status.inventory, null);
  await assert.rejects(() => collector.capture(), /NO_INVENTORY/);
  collector.stop();
});
test("game exit discards queued callbacks and raw inventory", async () => {
  const { api, collector, startGame } = setup(); startGame();
  const queued = collector.receive(inventory({ Suits: [] }));
  api.games.onGameInfoUpdated.emit({ gameInfo: { isRunning: false, classId: 8954 } });
  await queued;
  assert.equal(collector.status.inventory, null);
  assert.equal(collector.status.state, "waitingForGame");
  collector.stop();
});
test("old getInfo response cannot replace a live update", async () => {
  const { api, collector, startGame } = setup();
  let bootstrap;
  api.games.events.getInfo = callback => { bootstrap = callback; };
  startGame();
  await collector.receive(inventory({ Suits: [{ Rank: 1 }] }));
  bootstrap({ success: true, res: { match_info: { inventory: { Suits: [] } } } });
  await collector.queue;
  assert.equal(collector.status.inventory.fields.find(x => x.path === "$.Suits").maxLength, 1);
  collector.stop();
});
test("late running-game lookup cannot clear a newer launch event", () => {
  const { api, collector, startGame } = setup();
  collector.stop();
  let initialLookup;
  api.games.getRunningGameInfo2 = callback => { initialLookup = callback; };
  collector.start(); startGame();
  initialLookup({ success: true, gameInfo: null });
  assert.equal(collector.status.state, "waitingForInventory");
  collector.stop();
});
test("queue is bounded and provider errors do not disclose raw error text", async () => {
  const { api, collector, startGame } = setup(); startGame();
  for (let i = 0; i < 30; i++) collector.receive(inventory({ Suits: [] }));
  assert.equal(collector.pending, 16);
  assert.equal(collector.status.rejected, 14);
  api.games.events.onError.emit({ reason: "private-token" });
  assert.equal(JSON.stringify(collector.report()).includes("private-token"), false);
  await collector.queue;
  collector.stop();
});
test("ready marker hashes exact UTF8 bytes and never claims completeness", async () => {
  const capture = await createCapture({ Suits: [{ Name: "ação" }] }, crypto.randomUUID(), 1, new Date().toISOString());
  const marker = JSON.parse(capture.marker);
  assert.equal(marker.sha256, await sha256(capture.body));
  assert.equal(marker.bytes, new TextEncoder().encode(capture.body).length);
  assert.equal(marker.fileName, capture.fileName);
  assert.equal(JSON.parse(capture.body).completeness, "unverified");
});
