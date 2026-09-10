# Agent autonomy in this repository

The agent has broad autonomy to develop, test, run, maintain, and complete tasks
in this repository without requesting confirmation at every step.

## Mandatory language policy

**English is mandatory for all repository content and all repository-related
communication produced by an agent. This is a hard requirement, not a
preference.**

This requirement includes, but is not limited to:

- Source code identifiers, comments, diagnostics, logs, and user-facing text,
  unless a task explicitly implements localization for another language.
- Tests, test names, fixtures, scripts, configuration, and automation.
- Documentation, READMEs, TODOs, changelogs, and architecture records.
- GitHub Actions workflow, job, and step names, including their output messages.
- Branch names, commit messages, pull request titles and descriptions, review
  comments, release notes, and issue content created by the agent.

Do not introduce Portuguese or other non-English prose into the repository. When
editing a small file that already contains non-English prose, translate the whole
file when practical so the result is consistently English. Preserve non-English
content only when it is external data, a proper name, or explicitly required by
the product's localization behavior.

## Files and code

Within this repository, the agent is authorized to:

- Read, create, edit, move, rename, and delete files and directories.
- Implement features, fix defects, and perform refactoring.
- Create or modify tests, scripts, configuration, documentation, and automation.
- Make architectural changes required to complete the requested task while
  preserving compatibility with existing requirements and patterns.
- Remove obsolete, generated, duplicated, or superseded files when this is part
  of the requested work.
- Delete files and directories recursively when necessary to develop, fix, clean,
  rebuild, or test the project. These deletions within the repository are
  pre-authorized and do not require additional user confirmation.

The agent does not need to ask for confirmation for these operations as long as
the targets are resolved and remain within the repository root. Pre-existing user
changes must be preserved and must never be discarded unless directly necessary
for the task.

## Execution and validation

The agent is authorized to:

- Install, update, and restore dependencies required by the project.
- Run the application, local services, scripts, local migrations, and development
  tools.
- Run builds, unit tests, integration tests, end-to-end tests, linters,
  formatters, type checkers, static analyzers, and security checks.
- Start and stop local processes required to test the project.
- Diagnose failures, fix discovered problems, and repeat the execution and
  validation cycle until the relevant criteria are satisfied.
- Use the network when required to download dependencies or access services that
  are part of the normal development workflow.

These actions do not require intermediate confirmation when they are local,
reversible, and related to the current task.

## Git

The agent has autonomy to use Git in the repository, including:

- Inspecting status, history, branches, tags, diffs, and tracked files.
- Creating and switching work branches.
- Adding files to the index and creating task-related commits.
- Merging, rebasing, cherry-picking, and resolving conflicts when necessary.
- Fetching and synchronizing remote references when this is part of the task.
- Reverting commits or changes produced by the agent during the task.

The agent must preserve pre-existing work that does not belong to the task and
must not discard user changes. Required deletions inside the repository do not
need confirmation; their targets must only be explicitly resolved and verified
before execution.

## Limits

This authorization applies only to this repository and to the local environment
required to run the project. It does not authorize access to or modification of
personal files outside the repository.

The agent should stop and request confirmation only when an action:

- Requires a mandatory environment or platform permission that this file cannot
  grant.
- Could cause an irreversible impact outside this repository.
- Involves production publication or deployment, purchasing, billing, or use of
  credentials not supplied for the task.
- Requires a product decision that materially changes the requested objective and
  cannot be safely inferred from the code and context.

Outside these cases, the agent should make reasonable decisions, continue working
autonomously, and deliver the validated task with a final report of the changes
and checks performed.
