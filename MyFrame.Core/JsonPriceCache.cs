using System.Text.Json;

namespace MyFrame.Core;

public sealed class JsonPriceCache : IPriceCache, IReadOnlyPriceCache
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonPriceCache(string path) => _path = path;

    public async Task<MarketQuote?> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await ReadAsync(cancellationToken, tolerateInvalid: true)).GetValueOrDefault(slug); }
        finally { _gate.Release(); }
    }

    public async Task SetAsync(MarketQuote quote, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadAsync(cancellationToken, tolerateInvalid: true);
            values[quote.Slug] = quote;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(values), cancellationToken);
                File.Move(temporary, _path, true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyDictionary<string, MarketQuote>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadAsync(cancellationToken, tolerateInvalid: false); }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, MarketQuote>> ReadAsync(CancellationToken cancellationToken,
        bool tolerateInvalid)
    {
        if (!File.Exists(_path)) return new(StringComparer.Ordinal);
        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            return JsonSerializer.Deserialize<Dictionary<string, MarketQuote>>(json) ?? new(StringComparer.Ordinal);
        }
        catch (Exception e) when (tolerateInvalid && e is (IOException or JsonException))
        {
            return new(StringComparer.Ordinal);
        }
    }
}
