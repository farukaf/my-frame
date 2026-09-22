# Delivery checklist

This file tracks unresolved release gates only. Completed implementation history is
available in Git.

Current validated baseline: Windows distribution `0.0.33`; collector, Core, and MCP
synthetic regression passed; MCP read-only and side-by-side upgrade gates passed.

## 1. Validate real Overwolf capture

- [ ] Load the unpacked My Frame extension in Overwolf and confirm a fresh heartbeat
  and valid markers while Warframe is running.
- [ ] Capture inventory without reading AlecaFrame and compare it manually with the
  in-game Arsenal.
- [ ] Document the observed schema and field coverage, including instances, ranks,
  configurations, mods, snapshot/delta behavior, and `contextId`.
- [ ] Record the supported Overwolf distribution path and approval requirements.

Runbook and current blocker: [collector contract](docs/COLETOR-OVERWOLF.md).

## 2. Close the real capture pipeline

- [ ] Import an accepted real capture through the inbox into SQLite.
- [ ] Verify that the app and MCP expose the same revision, coverage, instances,
  upgrades, configurations, and context without treating a delta as a full snapshot.
- [ ] Repeat capture after an inventory change and confirm safe history/differences.

This gate depends on section 1. Synthetic producer-to-probe and persistence coverage
already pass; they do not replace real-game validation.

## 3. Approve community reference sources

- [ ] Confirm permitted access, licensing, attribution, and retention for the Warframe
  Wiki ingestion path.
- [ ] Confirm the same independently for Overframe and validate one real ingestion.
- [ ] Keep either source disabled or fixture-only until its own gate passes.

## 4. Final acceptance and release readiness

- [ ] Run the F10 before/after evaluation with cited sources, explicit uncertainty,
  and no critical failures.
- [ ] Complete the Overwolf distribution gate and update the runbook, field coverage,
  compatibility notes, and user-facing setup documentation.
- [ ] Run the full build, test, package, read-only, clean-install, upgrade, and restore
  gates on the release candidate.

Evaluation protocol: [F10](docs/avaliacao/2026-09-13-f10.md). Detailed acceptance
matrix: [platform validation](docs/VALIDACAO-PLATAFORMA.md).
