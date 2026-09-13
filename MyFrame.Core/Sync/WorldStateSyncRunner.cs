using System.Security.Cryptography;
using System.Text;

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
    public async Task<WorldStateSyncOutcome> RunFileAsync(
        SyncDatabase database,
        SyncHost host,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("World State file is required.", nameof(filePath));
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var info = new FileInfo(fullPath);
            if (!info.Exists) throw new FileNotFoundException("WORLDSTATE_FILE_NOT_FOUND", fullPath);
            if (info.Length is <= 0 or > 32 * 1024 * 1024) throw new InvalidDataException("WORLDSTATE_TOO_LARGE");
            var json = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);
            var retrieved = DateTimeOffset.UtcNow;
            var snapshot = WorldStateParser.Parse(json, retrieved);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            var batch = new SyncBatch("worldstate-pc", hash, json, snapshot.Bounties.Count, "worldstate-file-1");
            var publication = await host.RunWorldStateSnapshotOnceAsync(snapshot, batch, cancellationToken);
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
        HttpRequestException => "WORLDSTATE_NETWORK_UNAVAILABLE",
        _ => "SYNC_FAILED"
    };
}
