export const GAME_ID = 8954;
export const FEATURES = Object.freeze(["gep_internal", "game_info", "match_info"]);
export const MAX_PAYLOAD_BYTES = 8 * 1024 * 1024;
const encoder = new TextEncoder();
const object = value => value !== null && typeof value === "object" && !Array.isArray(value);

// Unknown keys may themselves contain account IDs or user-supplied names.
// Only known technical field names leave memory in the shareable schema report.
const fields = new Set(("Recipes MiscItems RawUpgrades Upgrades Consumables SpecialItems FlavourItems " +
  "FusionTreasures CrewShipRawSalvage CrewShipAmmo Suits LongGuns Pistols Melee Sentinels " +
  "SentinelWeapons SpaceSuits SpaceMelee SpaceGuns KubrowPets OperatorAmps MechSuits Ships " +
  "Scoops DrifterMelee OperatorSuits XPInfo PlayerLevel TradesRemaining ItemType ItemCount " +
  "ItemId Id _id oid $oid XP Rank UpgradeFingerprint Configs Upgrades Polarities Slot " +
  "Slots UnlockLevel Features Level Name Abilities ArchonCrystalUpgrades Helminth " +
  "Incarnon Arcanes FocusLens UpgradeVer ItemKind Modifications PveBonusLoadoutBin " +
  "PvpBonusLoadoutBin InventoryJson LoadOutInventory").split(" "));

export function parseBounded(value) {
  const text = typeof value === "string" ? value : JSON.stringify(value);
  if (typeof text !== "string" || encoder.encode(text).length > MAX_PAYLOAD_BYTES)
    throw new Error("PAYLOAD_LIMIT");
  if (typeof value !== "string") return { value, encoding: "json-object", text };
  try { return { value: JSON.parse(text), encoding: "json-string", text }; }
  catch { return { value: null, encoding: "opaque-string", text }; }
}

export function inspectInventory(input) {
  const parsed = parseBounded(input);
  const rows = new Map();
  let visited = 0;
  let truncated = false;
  function visit(value, path, depth) {
    if (++visited > 100000 || depth > 16 || rows.size >= 2048) { truncated = true; return; }
    const type = value === null ? "null" : Array.isArray(value) ? "array" : typeof value;
    const key = `${path}:${type}`;
    const row = rows.get(key) ?? { path, type, observations: 0 };
    row.observations++;
    if (Array.isArray(value)) {
      row.minLength = Math.min(row.minLength ?? value.length, value.length);
      row.maxLength = Math.max(row.maxLength ?? value.length, value.length);
    }
    rows.set(key, row);
    if (Array.isArray(value)) {
      if (value.length > 100) truncated = true;
      for (const child of value.slice(0, 100)) visit(child, `${path}[]`, depth + 1);
    } else if (object(value)) {
      for (const [name, child] of Object.entries(value)) {
        if (visited >= 100000 || rows.size >= 2048) { truncated = true; break; }
        visit(child, `${path}.${fields.has(name) ? name : "<redacted-field>"}`, depth + 1);
      }
    }
  }
  if (parsed.value !== null) visit(parsed.value, "$", 0);
  return {
    encoding: parsed.encoding,
    bytes: encoder.encode(parsed.text).length,
    rootObject: object(parsed.value),
    completeness: "unverified",
    publishable: false,
    truncated,
    fields: [...rows.values()].sort((a, b) => a.path.localeCompare(b.path) || a.type.localeCompare(b.type))
  };
}

// Native onInfoUpdates2 and getInfo.res; Electron has a different adapter contract.
export function nativeUpdates(event) {
  if (!object(event) || !FEATURES.includes(event.feature)) return [];
  const info = typeof event.info === "string" ? parseBounded(event.info).value : event.info;
  if (!object(info)) return [];
  const result = [];
  // Identity always precedes inventory in a combined callback.
  if (object(info.game_info) && typeof info.game_info.username === "string")
    result.push({ kind: "identity", value: info.game_info.username });
  if (object(info.gep_internal) && info.gep_internal.version_info !== undefined)
    result.push({ kind: "provider", value: info.gep_internal.version_info });
  for (const category of ["match_info", "game_info"]) {
    const values = info[category];
    if (!object(values)) continue;
    for (const kind of ["inventory", "highlighted"])
      if (Object.hasOwn(values, kind)) result.push({ kind, value: values[kind] });
  }
  return result;
}

export function isWarframe(info) {
  return info?.isRunning === true && (info.classId ?? Math.floor(info.id / 10)) === GAME_ID;
}

export async function sha256(text) {
  const bytes = await crypto.subtle.digest("SHA-256", encoder.encode(text));
  return [...new Uint8Array(bytes)].map(x => x.toString(16).padStart(2, "0")).join("");
}

export async function createCapture(value, sessionId, sequence, receivedAt, eventId = crypto.randomUUID()) {
  const parsed = parseBounded(value);
  const body = JSON.stringify({ schemaVersion: 1, gameId: GAME_ID, source: "overwolf-native",
    kind: "inventory", sessionId, sequence, eventId, receivedAt,
    captureMode: "snapshot", completeness: "unverified", encoding: parsed.encoding, payload: parsed.text });
  // Ready marker is written after the immutable body; hash is integrity, NOT authentication.
  const fileName = `${eventId}.capture.json`;
  if (encoder.encode(body).length > 16 * 1024 * 1024) throw new Error("ENVELOPE_LIMIT");
  return { body, fileName, markerName: `${eventId}.ready.json`, marker: JSON.stringify({
    schemaVersion: 1, fileName, bytes: encoder.encode(body).length, sha256: await sha256(body)
  }) };
}
