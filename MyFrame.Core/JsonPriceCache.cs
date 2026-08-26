using System.Text.Json;

namespace MyFrame.Core;

public sealed class JsonPriceCache : IPriceCache
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonPriceCache(string path) => _path = path;

    public async Task<MarketQuote?> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await ReadAsync(cancellationToken)).GetValueOrDefault(slug); }
        finally { _gate.Release(); }
    }

    public async Task SetAsync(MarketQuote quote, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadAsync(cancellationToken);
            values[quote.Slug] = quote;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(values), cancellationToken);
            File.Move(temporary, _path, true);
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, MarketQuote>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new(StringComparer.Ordinal);
        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            return JsonSerializer.Deserialize<Dictionary<string, MarketQuote>>(json) ?? new(StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }
}
