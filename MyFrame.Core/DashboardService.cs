namespace MyFrame.Core;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class DashboardService : IDisposable
{
    private readonly IAlecaFramePath _alecaPath;
    private readonly IAlecaFrameReader _inventoryReader;
    private readonly IAlecaCatalogReader _catalogReader;
    private readonly IWarframeMarketClient _market;
    private readonly IPriceCache _cache;
    private readonly IRecommendationEngine _engine;
    private readonly ILogger<DashboardService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounce;
    private CatalogSnapshot? _catalog;
    private DashboardSnapshot? _lastSnapshot;

    public DashboardService(IAlecaFramePath alecaPath, IAlecaFrameReader inventoryReader,
        IAlecaCatalogReader catalogReader, IWarframeMarketClient market, IPriceCache cache,
        IRecommendationEngine engine, ILogger<DashboardService>? logger = null)
    {
        _alecaPath = alecaPath;
        _inventoryReader = inventoryReader;
        _catalogReader = catalogReader;
        _market = market;
        _cache = cache;
        _engine = engine;
        _logger = logger ?? NullLogger<DashboardService>.Instance;
        _alecaPath.Changed += OnAlecaDirectoryChanged;
        ConfigureWatcher(alecaPath.DirectoryPath);
    }

    private void ConfigureWatcher(string alecaDirectory)
    {
        _watcher?.Dispose();
        _watcher = null;
        if (Directory.Exists(alecaDirectory))
        {
            _watcher = new FileSystemWatcher(alecaDirectory)
            {
                Filter = "*.*", IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
        }
    }

    public event EventHandler<DashboardSnapshot>? SnapshotUpdated;
    public DashboardSnapshot? LastSnapshot => _lastSnapshot;

    public async Task<DashboardSnapshot> RefreshAsync(bool refreshPrices = true,
        RecommendationSettings? settings = null, CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        _logger.LogInformation("Dashboard refresh started; refreshPrices={RefreshPrices}", refreshPrices);
        try
        {
            var alecaDirectory = _alecaPath.DirectoryPath;
            var inventory = await _inventoryReader.ReadAsync(alecaDirectory, cancellationToken);
            _catalog ??= await _catalogReader.LoadAsync(alecaDirectory, cancellationToken);
            var quotes = new Dictionary<string, MarketQuote>(StringComparer.Ordinal);
            var quoteSlugs = GetRelevantSlugs(inventory, _catalog).Distinct(StringComparer.Ordinal).Take(100).ToArray();
            foreach (var slug in quoteSlugs)
            {
                var cached = await _cache.GetAsync(slug, cancellationToken);
                if (cached is not null) quotes[slug] = cached with { IsStale = DateTimeOffset.UtcNow - cached.RetrievedAt > TimeSpan.FromMinutes(15) };
            }

            MarketAccount? account = null;
            IReadOnlyList<MarketOrder> orders = [];
            string? marketError = null;
            try
            {
                account = await _market.GetAccountAsync(cancellationToken);
                orders = await _market.GetMyOrdersAsync(cancellationToken);
                if (refreshPrices)
                {
                    foreach (var batch in quoteSlugs.Chunk(2))
                    {
                        var fresh = await Task.WhenAll(batch.Select(x => _market.GetTopOrdersAsync(x, cancellationToken)));
                        foreach (var quote in fresh.Where(x => x is not null).Cast<MarketQuote>())
                        {
                            quotes[quote.Slug] = quote;
                            await _cache.SetAsync(quote, cancellationToken);
                        }
                    }
                }
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(error, "Market unavailable during dashboard refresh; cached quotes will be used");
                marketError = "Warframe.Market is unavailable; using cached prices.";
            }

            var recommendations = _engine.Evaluate(inventory, _catalog, quotes, orders,
                settings ?? new RecommendationSettings());
            var status = new SyncStatus(false,
                marketError ?? "Inventory and market synchronized.", DateTimeOffset.Now, true,
                quotes.Count > 0, marketError);
            _lastSnapshot = new DashboardSnapshot(inventory, _catalog, recommendations, account, orders, quotes, status);
            _logger.LogInformation(
                "Dashboard refresh completed with {QuoteCount} quotes, {OrderCount} orders, {SaleCount} sales and {FarmCount} farm recommendations",
                quotes.Count, orders.Count, recommendations.Sales.Count, recommendations.Farm.Count);
            SnapshotUpdated?.Invoke(this, _lastSnapshot);
            return _lastSnapshot;
        }
        finally { _refreshGate.Release(); }
    }

    private static IEnumerable<string> GetRelevantSlugs(InventorySnapshot inventory, CatalogSnapshot catalog)
    {
        foreach (var relic in catalog.Items.Where(x => x.Category.Contains("Relic", StringComparison.OrdinalIgnoreCase) &&
                                                       inventory.Stackables.GetValueOrDefault(x.UniqueName) > 0))
        {
            if (!string.IsNullOrWhiteSpace(relic.MarketSlug)) yield return relic.MarketSlug;
            foreach (var reward in relic.Relics)
                if (catalog.MarketByNormalizedName.TryGetValue(ItemNameNormalizer.Normalize(reward.RewardName), out var identity))
                    yield return identity.Slug;
        }

        foreach (var item in catalog.Items.Where(x => x.Components.Any(c => inventory.Stackables.GetValueOrDefault(c.UniqueName) > 0)))
        {
            if (!string.IsNullOrWhiteSpace(item.MarketSlug)) yield return item.MarketSlug;
            foreach (var component in item.Components.Where(x => inventory.Stackables.GetValueOrDefault(x.UniqueName) > 0))
            {
                var name = component.Name.Equals("Blueprint", StringComparison.OrdinalIgnoreCase)
                    ? $"{item.Name} Blueprint" : $"{item.Name} {component.Name}";
                if (catalog.MarketByNormalizedName.TryGetValue(ItemNameNormalizer.Normalize(name), out var identity))
                    yield return identity.Slug;
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.FullPath);
        if (!name.Equals("lastData.dat", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("WFMarketToken.tk", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();
        _ = DebouncedRefreshAsync(_debounce.Token);
    }

    private void OnAlecaDirectoryChanged(object? sender, string directory)
    {
        _catalog = null;
        ConfigureWatcher(directory);
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();
        _ = DebouncedRefreshAsync(_debounce.Token);
    }

    private async Task DebouncedRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(750, cancellationToken);
            if (!cancellationToken.IsCancellationRequested) await RefreshAsync(false, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* The UI keeps the last valid snapshot and manual refresh remains available. */ }
    }

    public void Dispose()
    {
        _alecaPath.Changed -= OnAlecaDirectoryChanged;
        _watcher?.Dispose();
        _debounce?.Cancel();
        _debounce?.Dispose();
        _refreshGate.Dispose();
    }
}
