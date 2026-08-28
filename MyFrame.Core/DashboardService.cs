namespace MyFrame.Core;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class DashboardService : IDisposable
{
    private readonly string _aleccaDirectory;
    private readonly IAlecaFrameReader _inventoryReader;
    private readonly IAlecaCatalogReader _catalogReader;
    private readonly IWarframeMarketClient _market;
    private readonly IPriceCache _cache;
    private readonly IRecommendationEngine _engine;
    private readonly ILogger<DashboardService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounce;
    private CatalogSnapshot? _catalog;
    private DashboardSnapshot? _lastSnapshot;

    public DashboardService(string alecaDirectory, IAlecaFrameReader inventoryReader,
        IAlecaCatalogReader catalogReader, IWarframeMarketClient market, IPriceCache cache,
        IRecommendationEngine engine, ILogger<DashboardService>? logger = null)
    {
        _aleccaDirectory = alecaDirectory;
        _inventoryReader = inventoryReader;
        _catalogReader = catalogReader;
        _market = market;
        _cache = cache;
        _engine = engine;
        _logger = logger ?? NullLogger<DashboardService>.Instance;
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
            var inventory = await _inventoryReader.ReadAsync(_aleccaDirectory, cancellationToken);
            _catalog ??= await _catalogReader.LoadAsync(_aleccaDirectory, cancellationToken);
            var quotes = new Dictionary<string, MarketQuote>(StringComparer.Ordinal);
            var quoteSlugs = GetRelevantSlugs(inventory, _catalog).Take(36).ToArray();
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
                marketError = "Warframe.Market indisponível; usando cotações em cache.";
            }

            var recommendations = _engine.Evaluate(inventory, _catalog, quotes, orders,
                settings ?? new RecommendationSettings());
            var status = new SyncStatus(false,
                marketError ?? "Inventário e mercado sincronizados.", DateTimeOffset.Now, true,
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
        _watcher?.Dispose();
        _debounce?.Cancel();
        _debounce?.Dispose();
        _refreshGate.Dispose();
    }
}
