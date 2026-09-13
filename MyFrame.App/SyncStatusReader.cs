using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.App;

public sealed record SyncSourceStatusRow(
    string SourceId,
    string DisplayName,
    string State,
    string Detail,
    string Revision,
    string LastRun);

public sealed class SyncStatusReader
{
    private static readonly (string Id, string Name)[] Sources =
    [
        ("overwolf-inventory", "Warframe inventory"),
        ("public-export", "Warframe catalog"),
        ("worldstate-pc", "World State"),
        ("warframe-market", "Warframe Market")
    ];

    public async Task<IReadOnlyList<SyncSourceStatusRow>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var path = MyFrameStoragePaths.DataDatabasePath;
        if (!File.Exists(path)) return Sources.Select(source => NotInitialized(source)).ToArray();

        await using var database = new SyncDatabase(path);
        var rows = new List<SyncSourceStatusRow>(Sources.Length);
        foreach (var source in Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await database.GetStatusAsync(source.Id, cancellationToken);
            rows.Add(status is null ? NotInitialized(source) : Map(source, status));
        }
        return rows;
    }

    private static SyncSourceStatusRow NotInitialized((string Id, string Name) source) =>
        new(source.Id, source.Name, "not_initialized", "No published revision", "—", "—");

    private static SyncSourceStatusRow Map((string Id, string Name) source, MyFrame.Core.Sync.SyncStatus status)
    {
        var state = status.ErrorCode is null ? status.LastRunState ?? "unknown" : "failed";
        var detail = status.ErrorCode is null
            ? $"Accepted {status.AcceptedRecords:N0}; rejected {status.RejectedRecords:N0}"
            : $"Error: {status.ErrorCode}";
        return new(source.Id, source.Name, state, detail,
            status.ActiveRevisionId ?? "—",
            status.LastRunAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "—");
    }
}
