using System.Text.Json;
using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class SqliteMarketStoreTests
{
    [Fact]
    public async Task ImportsLegacyCachesAndReopensFromOneSqliteDatabase()
    {
        using var folder = new TemporaryFolder();
        var database = Path.Combine(folder.Path, "data.db");
        var prices = Path.Combine(folder.Path, "market-quotes.json");
        var state = Path.Combine(folder.Path, "market-data.dat");
        var items = Path.Combine(folder.Path, "market-items.dat");
        var quote = new MarketQuote("braton_prime", 12, 9, DateTimeOffset.UtcNow);
        var marketState = new MarketState(new MarketAccount("account", "Tenno", "pc"), [],
            DateTimeOffset.UtcNow, "confirmed", "context");
        var marketItems = new MarketItemIndex(
            new Dictionary<string, MarketIdentity> { ["braton prime"] = new("item", "braton_prime") },
            DateTimeOffset.UtcNow);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await File.WriteAllTextAsync(prices, JsonSerializer.Serialize(new Dictionary<string, MarketQuote>
        {
            [quote.Slug] = quote
        }, json));
        await File.WriteAllTextAsync(state, JsonSerializer.Serialize(marketState, json));
        await File.WriteAllTextAsync(items, JsonSerializer.Serialize(marketItems, json));

        var first = new SqliteMarketStore(database, prices, state, items);
        var firstPrices = (IPriceCache)first;
        var firstState = (IMarketStateStore)first;
        var firstItems = (IMarketItemIndexStore)first;
        Assert.Equal(quote, await firstPrices.GetAsync(quote.Slug));
        AssertMarketStateEqual(marketState, await firstState.LoadAsync());
        AssertMarketItemsEqual(marketItems, await firstItems.LoadAsync());

        var newQuote = quote with { Slug = "soma_prime", LowestSell = 20 };
        await ((IPriceCache)first).SetAsync(newQuote);
        Assert.True(File.Exists(prices));
        Assert.True(File.Exists(state));
        Assert.True(File.Exists(items));

        var reopened = new SqliteMarketStore(database);
        var all = await ((IReadOnlyPriceCache)reopened).LoadAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(newQuote, all[newQuote.Slug]);
        AssertMarketStateEqual(marketState, await ((IMarketStateStore)reopened).LoadAsync());
        AssertMarketItemsEqual(marketItems, await ((IMarketItemIndexStore)reopened).LoadAsync());
    }

    [Fact]
    public async Task ConcurrentQuoteWritesRemainReadable()
    {
        using var folder = new TemporaryFolder();
        var store = new SqliteMarketStore(Path.Combine(folder.Path, "data.db"));
        var prices = (IPriceCache)store;
        var writes = Enumerable.Range(0, 40).Select(index => prices.SetAsync(
            new MarketQuote($"item_{index}", index, index - 1, DateTimeOffset.UtcNow)));
        await Task.WhenAll(writes);

        var all = await ((IReadOnlyPriceCache)store).LoadAllAsync();
        Assert.Equal(40, all.Count);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "my-frame-market-sqlite-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }

    private static void AssertMarketStateEqual(MarketState expected, MarketState? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Account, actual!.Account);
        Assert.Equal(expected.Orders.Count, actual.Orders.Count);
        Assert.Equal(expected.RetrievedAt, actual.RetrievedAt);
        Assert.Equal(expected.ValidationState, actual.ValidationState);
        Assert.Equal(expected.ContextId, actual.ContextId);
    }

    private static void AssertMarketItemsEqual(MarketItemIndex expected, MarketItemIndex? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.RetrievedAt, actual!.RetrievedAt);
        Assert.Equal(expected.ByNormalizedName.Count, actual.ByNormalizedName.Count);
        foreach (var pair in expected.ByNormalizedName)
            Assert.Equal(pair.Value, actual.ByNormalizedName[pair.Key]);
    }
}
