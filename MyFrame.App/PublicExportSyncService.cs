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
            var indexClient = new PublicExportIndexClient(client);
            var documentClient = new PublicExportDocumentClient(client);
            var entries = await indexClient.FetchIndexAsync(cancellationToken: cancellationToken);
            var entry = entries.FirstOrDefault(value =>
                value.RelativePath.Contains("ExportWeapons_en.json", StringComparison.OrdinalIgnoreCase))
                ?? entries.FirstOrDefault(value => value.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            if (entry is null) throw new InvalidDataException("PUBLIC_EXPORT_ENTRY_NOT_FOUND");
            var publication = await host.RunPublicExportOnceAsync("public-export", documentClient, entry,
                cancellationToken: cancellationToken);
            var status = await database.GetStatusAsync("public-export", cancellationToken);
            if (publication is not null)
                return new("published", publication.RecordCount, publication.RevisionId, null, status?.ParserVersion);
            logger.LogWarning("Public Export synchronization failed with {ErrorCode}", status?.ErrorCode);
            return new(status?.LastRunState ?? "failed", 0, status?.ActiveRevisionId,
                status?.ErrorCode ?? "SYNC_FAILED", status?.ParserVersion);
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
