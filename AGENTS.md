# Repository agent policy

Agents may autonomously develop, test, run, and maintain this repository. Work
continuously toward the requested outcome without seeking confirmation for normal,
local, reversible repository operations.

## Language

Use English for all repository content and repository-related communication. This
includes code, tests, logs, UI text, documentation, automation, branches, commits,
pull requests, reviews, and release notes.

Non-English text is allowed only when it is external data, a proper name, or an
explicit localization requirement. When editing a small file that contains legacy
non-English prose, translate the entire file when practical.

Run `./scripts/Test-EnglishContent.ps1` after changing documentation, UI text, skills,
or scripts. The only allowed non-English content is localized external data in fixtures
and tests that explicitly verify localization behavior; keep that data minimal and
clearly separated from authored prose.

## Working rules

- Preserve pre-existing user changes and unrelated work.
- Read the relevant code and documentation before changing behavior.
- Keep changes scoped to the task and consistent with established project patterns.
- Prefer reversible operations and resolve deletion targets inside the repository.
- Never expose credentials, personal data, raw inventory, or private local paths.
- Treat unknown or partial source data explicitly; never convert it to a known zero.
- Keep the MCP process read-only, offline, and free of file migrations unless the
  task explicitly changes that contract.
- Record durable product or architecture decisions in the appropriate documentation,
  not in transient validation notes.

## Validation

Run the smallest relevant checks first, then the broader affected suite. Diagnose and
fix task-related failures before reporting completion. A blocked external or manual
gate is not a pass; document the observed state and the exact next action.

Use [todo.md](todo.md) for unresolved delivery gates only. Completed implementation
history belongs in Git; retain only durable contracts, decisions, and runbooks in
the documentation tree.

## Boundaries

Ask for confirmation only when an action requires a platform permission, risks an
irreversible effect outside this repository, publishes to production, incurs cost,
uses credentials not supplied for the task, or requires a material product decision
that cannot be inferred safely.
