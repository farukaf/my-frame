using Microsoft.Extensions.Logging;
using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.App;

public sealed record WorldStateSyncResult(string State, int Records, string? RevisionId, string? ErrorCode, string? ParserVersion);

public sealed class WorldStateSyncService(ILogger<WorldStateSyncService> logger)
{
    public async Task<WorldStateSyncResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        await using var host = new SyncHost(database);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var publication = await host.RunWorldStateOnceAsync(new WorldStateClient(client, allowCommunityFallback: true), cancellationToken: cancellationToken);
        var status = await database.GetStatusAsync("worldstate-pc", cancellationToken);
        if (publication is not null)
            return new("published", publication.RecordCount, publication.RevisionId, null, status?.ParserVersion);

        logger.LogWarning("World State synchronization failed with {ErrorCode}", status?.ErrorCode);
        return new(status?.LastRunState ?? "failed", 0, status?.ActiveRevisionId, status?.ErrorCode ?? "SYNC_FAILED", status?.ParserVersion);
    }
}
