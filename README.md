# My Frame

My Frame is a local Windows application for exploring Warframe inventory, collection,
mastery, farming, and market data. It includes a read-only MCP server so clients such
as Codex and Claude can query the same structured data used by the desktop app.

## Project status

The local data platform is implemented and has a validated Windows `0.0.33`
distribution. It includes:

- SQLite-backed revisions for inventory, official catalog data, World State,
  Warframe.Market data, and attributed reference content;
- a native Overwolf collector package, inbox transport, probes, and diagnostics;
- a Windows app for source status, synchronization, and capture import;
- a read-only `stdio` MCP server with source coverage, provenance, inventory history,
  acquisition, market, and reference queries; and
- versioned skills for build, farm, economy, and research workflows.

The remaining release blocker is real-game validation of the unpacked Overwolf
collector. Synthetic collector and persistence tests pass, but no fresh My Frame
heartbeat or valid real capture has yet been accepted. Community-source licensing and
the final F10 evaluation also remain open. See the [delivery checklist](todo.md).

## Components

- `MyFrame.App`: .NET MAUI Windows UI and composition root.
- `MyFrame.Core`: domain models, parsers, stores, synchronization, and read models.
- `MyFrame.Sync`: operational synchronization and import CLI.
- `MyFrame.Collector.Overwolf`: native Overwolf extension.
- `MyFrame.Collector.Probe`: collector inbox and readiness diagnostics.
- `MyFrame.Mcp`: local read-only MCP server over `stdio`.
- `skills`: client procedures for grounded Warframe answers.

The target flow is:

```text
Warframe -> Overwolf collector -> validated inbox -> SQLite <- public sources
                                                    |
                                      Windows app + read-only MCP
```

Legacy AlecaFrame import remains available for migration, but a clean installation and
the SQLite read path do not require AlecaFrame.

## Documentation

- [Delivery checklist](todo.md): unresolved gates only.
- [MCP contract](docs/MCP.md): tools, schemas, limits, and client behavior.
- [Overwolf collector contract](docs/COLETOR-OVERWOLF.md): package, transport,
  diagnostics, and real-capture gate.
- [Data-platform design](docs/PLATAFORMA-DE-DADOS.md): original phased design and
  architecture rationale. Its phase-status prose is historical; use the checklist for
  current status.
- [Validation matrix](docs/VALIDACAO-PLATAFORMA.md): detailed acceptance criteria.
- [Data-source research](docs/FONTES-DE-DADOS.md): source provenance and constraints.
- [Skills](skills/README.md): installable LLM workflows and grounding rules.

`docs/PLANO.md`, `docs/ARQUITETURA.md`, and `docs/REGRAS-E-VALIDACAO.md` describe the
legacy AlecaFrame-era baseline and remain useful for compatibility context.

## Security and data handling

- MCP uses local `stdio`; it does not listen on a network port.
- The MCP process does not synchronize sources, migrate storage, or write user data.
- Credentials are not copied into MCP responses or logs.
- Raw private inventory and personal fixtures are not committed.
- Coverage, freshness, and unknown values remain explicit instead of becoming zero.
- Inventory differences are rejected when known contexts do not match.

Although MCP runs locally, the connected AI client may send tool results to its model
provider. Users should review the client and provider privacy settings.

## Development

Requirements: Windows, the .NET 10 SDK, the `maui-windows` workload, PowerShell, and
Node.js for collector tests.

```powershell
dotnet restore MyFrame.slnx
dotnet build MyFrame.slnx
dotnet test MyFrame.Core.Tests/MyFrame.Core.Tests.csproj
dotnet test MyFrame.Mcp.Tests/MyFrame.Mcp.Tests.csproj
node --test --test-isolation=none MyFrame.Collector.Overwolf/tests/*.test.mjs
dotnet run --project MyFrame.App/MyFrame.App.csproj -f net10.0-windows10.0.19041.0
```

Create a self-contained Windows distribution with:

```powershell
./scripts/Build-Distribution.ps1 -Version 0.0.34
```

Relevant operational checks live in `scripts/`; consult the validation matrix before
declaring an external or manual gate complete.

## MCP setup

After installing or publishing My Frame, open **Settings > AI access (MCP)** and copy
the command for the desired client. Equivalent commands are:

```powershell
codex mcp add my-frame -- "C:\path\to\My Frame\MyFrame.Mcp.exe"
claude mcp add --transport stdio --scope user my-frame -- "C:\path\to\My Frame\MyFrame.Mcp.exe"
```

Verify with `codex mcp list` or `claude mcp get my-frame`. Remove the registration with
`codex mcp remove my-frame` or `claude mcp remove --scope user my-frame`.

Before making claims about personal inventory, clients must check capture readiness.
A stale heartbeat, no valid markers, or a state other than `ready` means inventory
claims are unverified.
