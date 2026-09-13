using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.Core.Tests;

public sealed class SnapshotProviderTests
{
    [Fact]
    public async Task ReadingRetainedSnapshotDoesNotRenewItsFiveMinuteLifetime()
    {
        using var folder = new TemporaryFolder();
        var clock = new ManualTimeProvider();
        var settings = new MyFrameSettingsDocument(1, 1, folder.Path, 10, 0, clock.GetUtcNow());
        var paths = new MyFrameLocalDataOptions(Path.Combine(folder.Path, "settings.json"),
            Path.Combine(folder.Path, "prices.json"), Path.Combine(folder.Path, "state.json"),
            Path.Combine(folder.Path, "items.json"));
        var inventory = new InventorySnapshot(clock.GetUtcNow(), new Dictionary<string, int>(),
            new HashSet<string>(), new Dictionary<string, long>(), 0, 0, "synthetic");
        using var provider = new MyFrameSnapshotProvider(new ToggleInventoryReader(inventory),
            new CatalogReader(new CatalogSnapshot([], new Dictionary<string, CatalogItem>(),
                new Dictionary<string, MarketIdentity>())), new RecommendationEngine(),
            new SettingsStore(settings), new PriceReader(), new StateStore(), new ItemIndexStore(),
            paths, clock);

        var first = await provider.GetAsync();
        clock.Advance(MyFrameSnapshotProvider.SnapshotRetention);
        Assert.Same(first, await provider.GetAsync(first.SnapshotId));
        clock.Advance(TimeSpan.FromTicks(1));

        var error = await Assert.ThrowsAsync<MyFrameSnapshotException>(() =>
            provider.GetAsync(first.SnapshotId));

        Assert.Equal("SNAPSHOT_EXPIRED", error.Code);
        Assert.False(error.Retryable);
        Assert.Contains("get_overview", error.Message);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    [Fact]
    public async Task TransientReadFailureUsesLastValidSnapshotOnlyForTheSameContext()
    {
        using var folder = new TemporaryFolder();
        var aleca = Path.Combine(folder.Path, "aleca");
        Directory.CreateDirectory(aleca);
        await File.WriteAllTextAsync(Path.Combine(aleca, "lastData.dat"), "fixture");
        var now = DateTimeOffset.UtcNow;
        var item = new CatalogItem("/item", "Item", "Items", "", "", false, false,
            false, false, null, null, null, [], []);
        var inventoryReader = new ToggleInventoryReader(new InventorySnapshot(now,
            new Dictionary<string, int> { [item.UniqueName] = 1 }, new HashSet<string>(),
            new Dictionary<string, long>(), 0, 0, "synthetic"));
        var settings = new MyFrameSettingsDocument(1, 1, aleca, 10, 0, now);
        var paths = new MyFrameLocalDataOptions(Path.Combine(folder.Path, "settings.json"),
            Path.Combine(folder.Path, "prices.json"), Path.Combine(folder.Path, "state.json"),
            Path.Combine(folder.Path, "items.json"));
        using var provider = new MyFrameSnapshotProvider(inventoryReader,
            new CatalogReader(new CatalogSnapshot([item],
                new Dictionary<string, CatalogItem> { [item.UniqueName] = item },
                new Dictionary<string, MarketIdentity>())), new RecommendationEngine(),
            new SettingsStore(settings), new PriceReader(), new StateStore(), new ItemIndexStore(), paths);

        var first = await provider.GetAsync();
        inventoryReader.Fail = true;
        provider.Invalidate();
        var fallback = await provider.GetAsync();

        Assert.NotEqual(first.SnapshotId, fallback.SnapshotId);
        Assert.Equal(first.Inventory, fallback.Inventory);
        Assert.Contains(fallback.Warnings, warning => warning.Code == "FALLBACK_LAST_VALID");
        Assert.All(fallback.Sources.Values, source => Assert.True(source.Fallback));
    }

    [Fact]
    public async Task UsesSynchronizedDataWhenAlecaSettingsAreMissing()
    {
        using var folder = new TemporaryFolder();
        var item = new CatalogItem("/synced/item", "Synced Item", "Weapon", "", "", false, false,
            false, false, null, null, null, [], []);
        var synced = new SynchronizedDataSnapshot(
            new InventorySnapshot(DateTimeOffset.UtcNow,
                new Dictionary<string, int> { ["/synced/resource"] = 3 },
                new HashSet<string> { item.UniqueName }, new Dictionary<string, long>(), 0, 0, "my-frame-sqlite"),
            new CatalogSnapshot([item], new Dictionary<string, CatalogItem> { [item.UniqueName] = item },
                new Dictionary<string, MarketIdentity>()), DateTimeOffset.UtcNow);
        var paths = new MyFrameLocalDataOptions(Path.Combine(folder.Path, "settings.json"),
            Path.Combine(folder.Path, "prices.json"), Path.Combine(folder.Path, "state.json"),
            Path.Combine(folder.Path, "items.json"));
        using var provider = new MyFrameSnapshotProvider(new ToggleInventoryReader(synced.Inventory),
            new CatalogReader(new CatalogSnapshot([], new Dictionary<string, CatalogItem>(), new Dictionary<string, MarketIdentity>())),
            new RecommendationEngine(), new NullSettingsStore(), new PriceReader(), new StateStore(),
            new ItemIndexStore(), paths, synchronizedData: new FakeSynchronizedReader(synced));

        var snapshot = await provider.GetAsync();

        Assert.NotNull(snapshot.Inventory);
        Assert.Equal("SYNC_DATABASE", snapshot.Sources["inventory"].DetailCode);
        Assert.Equal("partial", snapshot.Sources["catalog"].State);
        Assert.Equal(3, snapshot.Inventory!.Stackables["/synced/resource"]);
    }

    private sealed class ToggleInventoryReader(InventorySnapshot value) : IAlecaFrameReader
    {
        public bool Fail { get; set; }
        public Task<InventorySnapshot> ReadAsync(string alecaDirectory,
            CancellationToken cancellationToken = default) => Fail
            ? Task.FromException<InventorySnapshot>(new IOException("transient fixture failure"))
            : Task.FromResult(value);
    }

    private sealed class CatalogReader(CatalogSnapshot value) : IAlecaCatalogReader
    {
        public Task<CatalogSnapshot> LoadAsync(string alecaDirectory,
            CancellationToken cancellationToken = default) => Task.FromResult(value);
    }

    private sealed class SettingsStore(MyFrameSettingsDocument value) : IMyFrameSettingsStore
    {
        public Task<MyFrameSettingsDocument?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MyFrameSettingsDocument?>(value);
    }

    private sealed class NullSettingsStore : IMyFrameSettingsStore
    {
        public Task<MyFrameSettingsDocument?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MyFrameSettingsDocument?>(null);
    }

    private sealed class FakeSynchronizedReader(SynchronizedDataSnapshot value) : ISynchronizedDataReader
    {
        public Task<SynchronizedDataSnapshot?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<SynchronizedDataSnapshot?>(value);
    }

    private sealed class PriceReader : IReadOnlyPriceCache
    {
        public Task<IReadOnlyDictionary<string, MarketQuote>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, MarketQuote>>(
                new Dictionary<string, MarketQuote>());
    }

    private sealed class StateStore : IMarketStateStore
    {
        public Task<MarketState?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MarketState?>(null);
        public Task SaveAsync(MarketState state, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ItemIndexStore : IMarketItemIndexStore
    {
        public Task<MarketItemIndex?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MarketItemIndex?>(null);
        public Task SaveAsync(MarketItemIndex index, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "my-frame-snapshot-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
