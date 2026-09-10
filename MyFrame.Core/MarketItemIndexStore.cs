using System.Text.Json;

namespace MyFrame.Core;

/// <summary>
/// Caches Warframe.Market's item catalogue on disk. The list changes only when new items ship, so
/// fetching it on every launch would spend a request and a second of startup on an answer that is
/// almost always identical to yesterday's.
/// </summary>
public sealed class MarketItemIndexStore : IMarketItemIndexStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MarketItemIndexStore(string path) => _path = path;

    public async Task<MarketItemIndex?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return null;
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<MarketItemIndex>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception error) when (error is IOException or JsonException or NotSupportedException)
        {
            // Without the cache the refresh simply fetches the index again.
            return null;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(MarketItemIndex index, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                             FileShare.None, 128 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, index, cancellationToken: cancellationToken);
            }
            File.Move(temporary, _path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Losing the cache costs one extra request next launch; it must never fail the refresh.
        }
        finally { _gate.Release(); }
    }
}
