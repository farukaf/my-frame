# Architecture

My Frame is a local Windows application with a shared SQLite read model.

```text
Warframe -> Overwolf collector -> validated inbox -> Sync host -> SQLite
Public sources --------------------------------------------^       |
                                                           App + MCP
```

- `MyFrame.Core` owns domain models, parsers, persistence, and read models.
- `MyFrame.App` provides the MAUI UI and starts operational synchronization.
- `MyFrame.Sync` performs synchronization and explicit capture import.
- `MyFrame.Mcp` is a separate read-only `stdio` process.
- `MyFrame.Collector.Overwolf` captures only after explicit user action.

MCP never migrates storage, writes files, or calls the network. Unknown and partial
data remains explicit. See [the MCP contract](MCP.md) and
[the collector contract](COLETOR-OVERWOLF.md).
