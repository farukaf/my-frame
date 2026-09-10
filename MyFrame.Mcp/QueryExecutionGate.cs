namespace MyFrame.Mcp;

/// <summary>
/// Keeps one MCP process bounded even when a client issues a burst of expensive queries.
/// The admission count covers both running and queued calls, while the semaphore limits
/// the work that can execute concurrently.
/// </summary>
public sealed class QueryExecutionGate
{
    public const int DefaultMaximumConcurrent = 4;
    public const int DefaultMaximumQueued = 16;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _slots;
    private readonly int _maximumAdmitted;
    private readonly TimeSpan _timeout;
    private int _admitted;

    public QueryExecutionGate(int maximumConcurrent = DefaultMaximumConcurrent,
        int maximumQueued = DefaultMaximumQueued, TimeSpan? timeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumConcurrent);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumQueued);
        _slots = new SemaphoreSlim(maximumConcurrent, maximumConcurrent);
        _maximumAdmitted = checked(maximumConcurrent + maximumQueued);
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Interlocked.Increment(ref _admitted) > _maximumAdmitted)
        {
            Interlocked.Decrement(ref _admitted);
            throw new QueryProblemException("SERVER_BUSY",
                "The MCP server is busy. Retry the request shortly.", true);
        }

        var acquired = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            await _slots.WaitAsync(timeout.Token).ConfigureAwait(false);
            acquired = true;
            return await action(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new QueryProblemException("TIMEOUT",
                "The MCP request exceeded its 10 second processing budget. Retry with narrower filters.", true);
        }
        finally
        {
            if (acquired) _slots.Release();
            Interlocked.Decrement(ref _admitted);
        }
    }
}
