namespace MyFrame.Core.Sync;

public sealed record WorldStateSyncOutcome(
    string State,
    int Records,
    string? RevisionId,
    string? ErrorCode,
    string? ParserVersion,
    string Source = "worldstate-pc");

/// <summary>Shared orchestration boundary for official World State synchronization.</summary>
public sealed class WorldStateSyncRunner
{
    public async Task<WorldStateSyncOutcome> RunAsync(
        SyncDatabase database,
        SyncHost host,
        HttpClient httpClient,
        bool allowCommunityFallback = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(httpClient);
        try
        {
            var publication = await host.RunWorldStateOnceAsync(
                new WorldStateClient(httpClient, allowCommunityFallback),
                cancellationToken: cancellationToken);
            var status = await database.GetStatusAsync("worldstate-pc", cancellationToken);
            return publication is not null
                ? new("published", publication.RecordCount, publication.RevisionId, null, status?.ParserVersion)
                : new(status?.LastRunState ?? "failed", 0, status?.ActiveRevisionId,
                    status?.ErrorCode ?? "SYNC_FAILED", status?.ParserVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var status = await database.GetStatusAsync("worldstate-pc", cancellationToken);
            return new("failed", 0, status?.ActiveRevisionId, ErrorCode(error), status?.ParserVersion);
        }
    }

    private static string ErrorCode(Exception error) => error switch
    {
        InvalidDataException data when !string.IsNullOrWhiteSpace(data.Message) => data.Message,
        HttpRequestException request when request.Message.StartsWith("WORLDSTATE_HTTP_", StringComparison.Ordinal) => request.Message,
        _ => "SYNC_FAILED"
    };
}
