# Platform validation matrix

| Area | Acceptance criterion |
| --- | --- |
| Capture | Fresh heartbeat, valid marker, real inventory sample compared with the Arsenal. |
| Storage | Idempotent migration, one writer, consistent revisions, backup and restore. |
| Public sources | Versioned, bounded parsing with explicit coverage, provenance, and failure state. |
| MCP | Read-only `stdio`, no network or file mutation, documented tools and schemas. |
| UI parity | App and MCP return compatible data for the same revision. |
| Privacy | No token, raw payload, account identity, or personal path in logs or responses. |
| Release | Build, tests, package, clean install, upgrade, restore, and external gates pass. |
| LLM evaluation | Claims cite sources, preserve uncertainty, and contain no critical failures. |

A blocked external gate is not a pass. Use synthetic fixtures for destructive and
failure testing; never use a user's live data for corruption, disk-full, or privacy
tests. Current unresolved gates are listed in [the delivery checklist](../todo.md).

Run `./scripts/Test-NonCollectorReleaseGates.ps1` to execute the local, non-collector
checks together. Its success does not approve community sources, supply F10 results,
or replace distribution, clean-install, upgrade, and restore validation.
