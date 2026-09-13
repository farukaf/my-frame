namespace MyFrame.Core.Sync;

public sealed record SyncHostState(bool Running, DateTimeOffset? StartedAt, DateTimeOffset? LastRunAt);

/// <summary>Small lifecycle boundary for the single local sync writer.</summary>
public sealed class SyncHost : IAsyncDisposable
{
    private readonly SyncDatabase _database;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private bool _running;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _lastRunAt;

    public SyncHost(SyncDatabase database) => _database = database;
    public SyncHostState State => new(_running, _startedAt, _lastRunAt);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try { if (!_running) { await _database.InitializeAsync(cancellationToken); _running = true; _startedAt = DateTimeOffset.UtcNow; } }
        finally { _lifecycle.Release(); }
    }

    public async Task<SyncPublicationResult?> RunOnceAsync(string sourceId, Func<CancellationToken, Task<SyncBatch>> fetch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        await StartAsync(cancellationToken);
        try
        {
            var batch = await fetch(cancellationToken);
            if (!string.Equals(batch.SourceId, sourceId, StringComparison.Ordinal)) throw new InvalidDataException("SYNC_SOURCE_MISMATCH");
            var result = await _database.PublishAsync(batch, cancellationToken);
            _lastRunAt = DateTimeOffset.UtcNow;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var code = ErrorCode(error);
            await _database.RecordFailureAsync(sourceId, code, cancellationToken);
            _lastRunAt = DateTimeOffset.UtcNow;
            return null;
        }
    }

    public async Task<SyncPublicationResult?> RunCatalogOnceAsync(string sourceId, Func<CancellationToken, Task<(SyncBatch Batch, IReadOnlyList<PublicExportRecord> Records)>> fetch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        await StartAsync(cancellationToken);
        try
        {
            var (batch, records) = await fetch(cancellationToken);
            if (!string.Equals(batch.SourceId, sourceId, StringComparison.Ordinal)) throw new InvalidDataException("SYNC_SOURCE_MISMATCH");
            var result = await _database.PublishCatalogAsync(batch, records, cancellationToken);
            _lastRunAt = DateTimeOffset.UtcNow;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            await _database.RecordFailureAsync(sourceId, ErrorCode(error), cancellationToken);
            _lastRunAt = DateTimeOffset.UtcNow;
            return null;
        }
    }

    public async Task<int> RunMaintenanceAsync(int maximumRevisionsPerSource = 3,
        CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken);
        var removed = await _database.PruneRetainedAsync(maximumRevisionsPerSource, cancellationToken);
        _lastRunAt = DateTimeOffset.UtcNow;
        return removed;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try { _running = false; }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync() { await StopAsync(); _lifecycle.Dispose(); await _database.DisposeAsync(); }

    private static string ErrorCode(Exception error) => error switch
    {
        InvalidDataException data when !string.IsNullOrWhiteSpace(data.Message) => data.Message,
        HttpRequestException request when request.Message.StartsWith("PUBLIC_EXPORT_HTTP_", StringComparison.Ordinal) => request.Message,
        _ => "SYNC_FAILED"
    };
}
