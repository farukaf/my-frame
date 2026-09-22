using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.App;

public sealed record SyncSourceStatusRow(
    string SourceId,
    string DisplayName,
    string State,
    string Detail,
    string Revision,
    string LastRun,
    string ParserVersion,
    string Coverage);
public sealed record SyncAttemptStatusRow(
    string SourceId,
    string State,
    string StartedAt,
    string Detail);

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
            var coverage = await database.GetSourceCoverageAsync(source.Id, cancellationToken);
            rows.Add(status is null ? NotInitialized(source) : Map(source, status, coverage));
        }
        return rows;
    }

    public async Task<bool> HasSynchronizedDataAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await new SqliteSynchronizedDataReader(MyFrameStoragePaths.DataDatabasePath)
            .ReadAsync(cancellationToken);
        return snapshot is not null;
    }

    public async Task<IReadOnlyList<SyncAttemptStatusRow>> ReadRecentRunsAsync(CancellationToken cancellationToken = default)
    {
        var path = MyFrameStoragePaths.DataDatabasePath;
        if (!File.Exists(path)) return [];
        await using var database = new SyncDatabase(path);
        var runs = await database.GetRecentRunsAsync(sourceId: null, limit: 30, cancellationToken: cancellationToken);
        return runs.Select(run => new SyncAttemptStatusRow(
            run.SourceId,
            run.State,
            run.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
            $"{run.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss} · " +
            (run.ErrorCode is null
                ? $"Accepted {run.RecordsAccepted:N0}; rejected {run.RecordsRejected:N0}"
                : $"Error: {run.ErrorCode}")))
            .ToArray();
    }

    private static SyncSourceStatusRow NotInitialized((string Id, string Name) source) =>
        new(source.Id, source.Name, "not_initialized", "No published revision", "—", "—", "—", "Coverage: —");

    private static SyncSourceStatusRow Map((string Id, string Name) source, MyFrame.Core.Sync.SyncStatus status,
        IReadOnlyDictionary<string, InventoryFieldState> coverage)
    {
        var state = status.ErrorCode is null ? status.LastRunState ?? "unknown" : "failed";
        var detail = status.ErrorCode is null
            ? $"Accepted {status.AcceptedRecords:N0}; rejected {status.RejectedRecords:N0}"
            : $"Error: {status.ErrorCode}";
        var coverageText = coverage.Count == 0
            ? "Coverage: —"
            : $"Coverage: {string.Join(", ", coverage.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"))}";
        return new(source.Id, source.Name, state, detail,
            status.ActiveRevisionId ?? "—",
            status.LastRunAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "—",
            status.ParserVersion ?? "—", coverageText);
    }
}
