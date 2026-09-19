import { FEATURES, GAME_ID, inspectInventory, isWarframe, nativeUpdates, parseBounded, sha256, createCapture } from "./probe.mjs";

export class Collector {
  constructor(api, changed = () => {}, clock = () => new Date().toISOString()) {
    this.api = api;
    this.changed = changed;
    this.clock = clock;
    this.epoch = 0;
    this.pending = 0;
    this.gameRevision = 0;
    this.queue = Promise.resolve();
    this.listeners = [];
    this.reset("stopped");
  }
  reset(state) {
    this.epoch++;
    this.sessionId = crypto.randomUUID();
    this.username = null;
    this.raw = null;
    this.lastHash = null;
    this.status = { state, gameId: GAME_ID, sessionId: this.sessionId, received: 0,
      duplicates: 0, rejected: 0, supportedFeatures: [], inventory: null,
      highlighted: "notObserved", identity: "notObserved", provider: "notObserved" };
    this.changed(this.status);
  }
  listen(event, handler) {
    event.addListener(handler);
    this.listeners.push(() => event.removeListener(handler));
  }
  start() {
    if (this.listeners.length) return;
    this.reset("waitingForGame");
    this.listen(this.api.games.onGameInfoUpdated, event => {
      if (!event || !Object.hasOwn(event, "gameInfo")) return;
      this.gameRevision++;
      this.gameChanged(event.gameInfo);
    });
    this.listen(this.api.games.events.onInfoUpdates2, event => this.receive(event));
    this.listen(this.api.games.events.onError, () => {
      this.status.state = "providerError";
      this.changed(this.status); // Provider error strings are not safe diagnostics.
    });
    const gameRevision = this.gameRevision;
    this.api.games.getRunningGameInfo2(result => {
      if (this.listeners.length && gameRevision === this.gameRevision && result?.success)
        this.gameChanged(result.gameInfo);
    });
  }
  stop() {
    for (const remove of this.listeners) remove();
    this.listeners = [];
    clearTimeout(this.retry);
    this.reset("stopped");
  }
  gameChanged(info) {
    const running = isWarframe(info);
    if (!running) {
      clearTimeout(this.retry);
      this.reset("waitingForGame");
      return;
    }
    if (this.runningId === info.id && this.status.state !== "waitingForGame") return;
    this.runningId = info.id;
    this.reset("registering");
    this.register(this.epoch, 0);
  }
  register(epoch, attempt) {
    this.api.games.events.setRequiredFeatures([...FEATURES], result => {
      if (epoch !== this.epoch || !this.listeners.length) return;
      this.status.supportedFeatures = FEATURES.filter(x => result?.supportedFeatures?.includes(x));
      if (!result?.success || !this.status.supportedFeatures.includes("match_info")) {
        this.status.state = "featuresUnavailable";
        if (attempt < 4) this.retry = setTimeout(() => this.register(epoch, attempt + 1), 3000);
        this.changed(this.status);
        return;
      }
      this.status.state = "waitingForInventory";
      this.changed(this.status);
      const received = this.status.received;
      this.api.games.events.getInfo(info => {
        if (epoch !== this.epoch || received !== this.status.received || !info?.success) return;
        // getInfo is cached provider state, never proof of snapshot completeness.
        for (const feature of FEATURES) if (info.res?.[feature])
          this.receive({ feature, info: { [feature]: info.res[feature] } });
      });
    });
  }
  receive(event) {
    if (["stopped", "waitingForGame"].includes(this.status.state)) return Promise.resolve();
    if (this.pending >= 16) {
      this.status.rejected++;
      this.status.state = "queueFull";
      this.changed(this.status);
      return Promise.resolve();
    }
    const epoch = this.epoch;
    this.pending++;
    this.status.received++;
    this.queue = this.queue.then(async () => {
      if (epoch !== this.epoch) return;
      for (const update of nativeUpdates(event)) {
        if (update.kind === "identity") {
          if (this.username !== null && this.username !== update.value) {
            // Discard old observations and invalidate queued callbacks on identity change.
            this.reset("waitingForInventory");
            this.status.received = 1;
          }
          this.username = update.value;
          this.status.identity = "observed";
        } else if (update.kind === "inventory") {
          const inventory = inspectInventory(update.value);
          const text = parseBounded(update.value).text;
          const currentEpoch = this.epoch;
          const hash = await sha256(text);
          if (currentEpoch !== this.epoch) return;
          if (hash === this.lastHash) { this.status.duplicates++; continue; }
          this.lastHash = hash;
          this.raw = text; // Keep an immutable payload, not the provider's mutable object.
          this.status.inventory = inventory;
          this.status.lastInventoryAt = this.clock();
          this.status.state = inventory.rootObject ? "inventoryObservedUnverified" : "inventoryEncodedOrInvalid";
        } else if (update.kind === "highlighted") {
          this.status.highlighted = inspectInventory(update.value); // Structure only; no item/user values.
        } else {
          const value = parseBounded(update.value).value;
          this.status.provider = value?.is_updated === true ? "updated" :
            value?.is_updated === false ? "outdated" : "unknown";
          this.status.providerVersions = Object.fromEntries(["local_version", "public_version"]
            .filter(key => typeof value?.[key] === "string" && /^\d+(\.\d+){0,3}$/.test(value[key]))
            .map(key => [key, value[key]]));
        }
      }
    }).catch(() => {
      if (epoch === this.epoch) { this.status.rejected++; this.status.state = "invalidEvent"; }
    }).finally(() => {
      this.pending--;
      this.changed(this.status);
    });
    return this.queue;
  }
  report() {
    return { schemaVersion: 1, collectorVersion: "0.1.0", observedAt: this.clock(),
      ...structuredClone(this.status), rawIncluded: false, completeness: "unverified" };
  }
  async capture() {
    if (this.raw === null) throw new Error("NO_INVENTORY");
    return createCapture(this.raw, this.sessionId, this.status.received, this.status.lastInventoryAt);
  }
}
