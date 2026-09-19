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
        var result = await new WorldStateSyncRunner().RunAsync(database, host, client,
            allowCommunityFallback: true, cancellationToken: cancellationToken);
        if (result.State == "published") return new(result.State, result.Records, result.RevisionId, null, result.ParserVersion);
        logger.LogWarning("World State synchronization failed with {ErrorCode}", result.ErrorCode);
        return new(result.State, result.Records, result.RevisionId, result.ErrorCode ?? "SYNC_FAILED", result.ParserVersion);
    }
}
