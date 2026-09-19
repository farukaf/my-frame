using Microsoft.Extensions.Logging;
using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.App;

public sealed record PublicExportSyncResult(string State, int Records, string? RevisionId, string? ErrorCode, string? ParserVersion);

public sealed class PublicExportSyncService(ILogger<PublicExportSyncService> logger)
{
    public async Task<PublicExportSyncResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        await using var host = new SyncHost(database);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        try
        {
            var result = await new PublicExportSyncRunner().RunAsync(database, host, client, cancellationToken);
            if (result.State == "published")
                return new(result.State, result.Records, result.RevisionId, null, result.ParserVersion);
            logger.LogWarning("Public Export synchronization failed with {ErrorCode}", result.ErrorCode);
            return new(result.State, result.Records, result.RevisionId,
                result.ErrorCode ?? "SYNC_FAILED", result.ParserVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "Public Export synchronization failed before publication");
            var status = await database.GetStatusAsync("public-export", cancellationToken);
            return new("failed", 0, status?.ActiveRevisionId, error is InvalidDataException data ? data.Message : "SYNC_FAILED", status?.ParserVersion);
        }
    }
}
