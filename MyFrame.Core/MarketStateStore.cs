using System.Text.Json;

namespace MyFrame.Core;

/// <summary>
/// Stores the account and open orders from the last successful Warframe.Market call next to the
/// price cache, so the first paint of a launch already reserves against the orders instead of
/// showing them appear a second later. Only the response is written: the authentication token is
/// read elsewhere, in memory, and never reaches this file.
/// </summary>
public sealed class MarketStateStore : IMarketStateStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MarketStateStore(string path) => _path = path;

    public async Task<MarketState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return null;
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<MarketState>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception error) when (error is IOException or JsonException or NotSupportedException)
        {
            // A launch that cannot read its own cache simply waits for the network, as before.
            return null;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(MarketState state, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                             FileShare.None, 32 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, state, cancellationToken: cancellationToken);
            }
            File.Move(temporary, _path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Losing the cache costs one slower launch; it must never fail the refresh itself.
        }
        finally { _gate.Release(); }
    }
}
