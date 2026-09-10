# My Frame

A .NET MAUI Windows application for viewing the local AlecaFrame inventory,
tracking collection and mastery progress, planning farming, and separating
surplus items between platinum and ducats.

## Planning

- [Full plan](docs/PLANO.md)
- [Architecture and reverse engineering](docs/ARQUITETURA.md)
- [Rules, tests, and security](docs/REGRAS-E-VALIDACAO.md)

## Security

- The AlecaFrame directory is always opened in read-only mode.
- `WFMarketToken.tk` is read only in memory and is never copied or logged.
- Warframe.Market authentication is used only to query the current user's profile
  and orders. The application does not create, update, or remove listings.
- Private data and real snapshots are not included in the repository.

## Development

Requirements: Windows, .NET 10 SDK, and the `maui-windows` workload.

```powershell
dotnet restore MyFrame.slnx
dotnet build MyFrame.slnx
dotnet test MyFrame.Core.Tests/MyFrame.Core.Tests.csproj
dotnet run --project MyFrame.App/MyFrame.App.csproj -f net10.0-windows10.0.19041.0
```

## CI/CD and distribution

Every pull request runs all tests and generates self-contained `win-x64`
artifacts with Velopack. Neither the .NET SDK nor the runtime needs to be
installed:

- `MyFrame-win-Portable.zip`: portable build;
- `MyFrame-win-Setup.exe`: per-user one-click installer.

The files can be downloaded from the link posted by the bot on the pull request
or from the workflow run's **Artifacts** section. Preview downloads require a
GitHub sign-in and expire after 30 days. Tags matching `vMAJOR.MINOR.PATCH` (for
example, `v1.2.3`) create or update a GitHub Release and attach the same files,
together with the metadata and packages required for a future automatic update
implementation.

The installer does not require administrator privileges. It installs under
`%LOCALAPPDATA%`, creates Desktop and Start Menu shortcuts, and registers the
application with Windows uninstall settings. Until the binaries are code-signed,
Windows SmartScreen may display a warning when they are downloaded for the first
time.

[Download the latest stable installer](https://github.com/farukaf/my-frame/releases/latest/download/MyFrame-win-Setup.exe)

[Download the latest portable build](https://github.com/farukaf/my-frame/releases/latest/download/MyFrame-win-Portable.zip)

To reproduce the packaging process locally, run:

```powershell
./scripts/Build-Distribution.ps1 -Version 1.0.0
```

## Logs

The application writes structured JSON Lines events to
`%LOCALAPPDATA%\MyFrame\logs\my-frame-yyyyMMddHH.json`. A new file is created
every hour, with a maximum retention of 168 files and a 25 MB limit per file.
Logs record startup stages, counts, synchronization results, and failures without
recording JWTs, authorization headers, or the complete inventory.
