using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class MarketStateStoreTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripAccountOrdersAndTimestamp()
    {
        using var directory = new TemporaryDirectory();
        var store = new MarketStateStore(Path.Combine(directory.Path, "market-data.dat"));
        var state = new MarketState(new MarketAccount("id", "Tenno", "pc"),
            [new MarketOrder("order", "item", "item_slug", "sell", 12, 2, true)], DateTimeOffset.UtcNow);

        await store.SaveAsync(state);

        var loaded = await store.LoadAsync();
        Assert.Equal(state.Account, loaded?.Account);
        Assert.Equal(state.RetrievedAt, loaded?.RetrievedAt);
        Assert.Equal(state.Orders, loaded?.Orders);
    }

    [Fact]
    public async Task MissingOrMalformedStateIsIgnored()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "market-data.dat");
        var store = new MarketStateStore(path);

        Assert.Null(await store.LoadAsync());
        await File.WriteAllTextAsync(path, "not json");

        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task SaveCreatesParentDirectory()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "nested", "market-data.dat");
        var store = new MarketStateStore(path);

        await store.SaveAsync(new MarketState(null, [], DateTimeOffset.UtcNow));

        Assert.True(File.Exists(path));
    }
}
