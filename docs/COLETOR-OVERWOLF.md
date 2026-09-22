# Overwolf collector contract

This document describes the implemented Native GEP collector. Synthetic tests verify
the contract, but no field is considered observed until it has been captured through
Overwolf while Warframe is running and compared with the in-game Arsenal.

| Field or event | Implemented contract | Status for recommendations |
| --- | --- | --- |
| `gameId=8954` | The manifest and envelope require Warframe. | Known |
| `game_info.username` | Observed only in memory and never persisted in the payload. | Not exposed without an account policy |
| `match_info.inventory` | Converted to a snapshot or delta envelope with a hash. | Unverified until real capture |
| `highlighted` | Structure is inspected without storing values. | `notObserved` for facts |
| `gep_internal`, `game_info`, `match_info` | Requested without chat. | Awaiting real callback |
| `chat` | Never registered or persisted. | Unsupported |
| Rank, configuration, and mods | Accepted when present in an envelope. | `NotObserved` until compared with Arsenal |
| Upgrades | Attributed by `ownerInstanceId` when present. | Coverage depends on real capture |
| `contextId` | Optional, validated, persisted, and never inferred. | `null` when absent; contexts are not cross-compared |

## Transport and retention

The local transport uses `schemaVersion`, `sessionId`, `eventId`, `sequence`, optional
`contextId`, `captureMode`, `completeness`, `contentHash`, and private raw data with
short retention. The `.ready.json` marker contains only the filename, byte count, and
SHA-256; it never contains inventory, a username, or a token. Import requires explicit
consent and is idempotent.

`captureMode=snapshot` may represent complete inventory only when its completeness
supports that claim. A `captureMode=delta` records a change; it neither replaces nor
compares as a complete snapshot. MCP returns `context_mismatch` when both compared
revisions have different known contexts.

## Runtime boundary

Overwolf hosts a WebApp, so the extension retains a small JavaScript adapter for its
native game-event API, DOM, and extension file API. Portable transport construction is
also implemented in C# as `CollectorCaptureWriter` in `MyFrame.Core`; native tools and
future non-WebApp collectors must use that implementation. Both producers emit the
same bounded, hash-verified capture/marker contract, which `CollectorCaptureReader`
validates before anything reaches the local inbox.

## Real-capture runbook

1. Confirm that Overwolf is signed in and the account is permitted to load unpacked
   extensions.
2. Run `./scripts/Build-Collector.ps1`, then open **Development options → Load
   unpacked extension** in Overwolf and select `artifacts/collector-overwolf`.
3. Open **My Frame Collector Dev** and start in-memory capture. Before launching the
   game, expect `waitingForGame`.
4. Launch Warframe. Confirm transition through `registering` and
   `waitingForInventory`, a fresh heartbeat, and valid markers.
5. Log in and open the Arsenal. Export only the sanitized structure report first.
   Do not copy raw inventory into an issue, pull request, or LLM conversation.
6. After explicit consent, create a raw capture in a controlled local directory and
   validate its marker with:

   ```powershell
   dotnet run --project MyFrame.Collector.Probe -- --marker "C:\path\to\capture.ready.json"
   ```

7. Compare a sample with the Arsenal, repeat after an inventory change, and remove the
   raw capture after local verification. Record only sanitized counts and conclusions.

## Current release gate

The unpacked collector has not yet been loaded in Overwolf. The latest preflight state
was `heartbeatFresh=false` with `validMarkers=0`; the required action is to load the
unpacked extension. Therefore, synthetic tests and package validation are not evidence
of real capture. Until this gate passes, inventory remains `unverified` and the app and
MCP must not claim complete builds, polarities, shards, Helminth, or Incarnon coverage.
