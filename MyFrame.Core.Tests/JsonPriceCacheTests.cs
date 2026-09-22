using System.Text.Json;
using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class JsonPriceCacheTests
{
    [Fact]
    public async Task SetAndGetRoundTripTheQuote()
    {
        using var directory = new TemporaryDirectory();
        var cache = new JsonPriceCache(Path.Combine(directory.Path, "nested", "quotes.json"));
        var quote = new MarketQuote("test_slug", 12, 9, DateTimeOffset.UtcNow);

        await cache.SetAsync(quote);

        Assert.Equal(quote, await cache.GetAsync("test_slug"));
    }

    [Fact]
    public async Task SettingTheSameSlugReplacesThePreviousQuote()
    {
        using var directory = new TemporaryDirectory();
        var cache = new JsonPriceCache(Path.Combine(directory.Path, "quotes.json"));
        await cache.SetAsync(new MarketQuote("test_slug", 12, 9, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var latest = new MarketQuote("test_slug", 15, 11, DateTimeOffset.UtcNow);

        await cache.SetAsync(latest);

        Assert.Equal(latest, await cache.GetAsync("test_slug"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory.Path, "quotes.json")));
        Assert.Single(document.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task InvalidCacheContentBehavesAsAnEmptyCacheAndCanRecover()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "quotes.json");
        await File.WriteAllTextAsync(path, "not json");
        var cache = new JsonPriceCache(path);

        Assert.Null(await cache.GetAsync("missing"));
        var quote = new MarketQuote("recovered", 1, 2, DateTimeOffset.UtcNow);
        await cache.SetAsync(quote);

        Assert.Equal(quote, await cache.GetAsync("recovered"));
    }
}
