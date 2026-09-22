using System.Collections.Concurrent;
using MyFrame.Core.Sync;

namespace MyFrame.Core;

public sealed record MyFrameLocalDataOptions(
    string SettingsPath,
    string PriceCachePath,
    string MarketStatePath,
    string MarketItemIndexPath)
{
    public static MyFrameLocalDataOptions Shared => new(
        MyFrameStoragePaths.SettingsPath,
        MyFrameStoragePaths.PriceCachePath,
        MyFrameStoragePaths.MarketStatePath,
        MyFrameStoragePaths.MarketItemIndexPath);
}

public sealed record SnapshotSource(
    string State,
    DateTimeOffset? RetrievedAt,
    bool Fallback = false,
    string? DetailCode = null);

public sealed record SnapshotWarning(string Code, string Message);

public sealed record MyFrameSnapshot(
    string SnapshotId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset EvaluatedAt,
    InventorySnapshot? Inventory,
    CatalogSnapshot? Catalog,
    RecommendationResult? Recommendations,
    MarketAccount? Account,
    IReadOnlyList<MarketOrder> Orders,
    IReadOnlyDictionary<string, MarketQuote> Quotes,
    MyFrameSettingsDocument? Settings,
    IReadOnlyDictionary<string, SnapshotSource> Sources,
    IReadOnlyList<SnapshotWarning> Warnings,
    bool SetupRequired,
    bool AvailabilityConfirmed,
    DateTimeOffset ReevaluateAt);

public sealed class MyFrameSnapshotException(string code, string message, bool retryable = false)
    : Exception(message)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public interface IMyFrameSnapshotProvider
{
    Task<MyFrameSnapshot> GetAsync(string? snapshotId = null,
        CancellationToken cancellationToken = default);
    void Invalidate();
}

public sealed class MyFrameSnapshotProvider : IMyFrameSnapshotProvider, IDisposable
{
    public static readonly TimeSpan QuoteFreshness = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan OrderFreshness = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SnapshotRetention = TimeSpan.FromMinutes(5);
    public const int MaximumRetainedSnapshots = 8;

    private readonly IAlecaFrameReader _inventoryReader;
    private readonly IAlecaCatalogReader _catalogReader;
    private readonly IRecommendationEngine _engine;
    private readonly IMyFrameSettingsStore _settingsStore;
    private readonly IReadOnlyPriceCache _prices;
    private readonly IMarketStateStore _marketState;
    private readonly IMarketItemIndexStore _marketItems;
    private readonly ISynchronizedDataReader? _synchronizedData;
    private readonly MyFrameLocalDataOptions _paths;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, RetainedSnapshot> _retained = new(StringComparer.Ordinal);
    private FileSystemWatcher? _sharedWatcher;
    private FileSystemWatcher? _alecaWatcher;
    private MyFrameSnapshot? _current;
    private SourceFingerprint? _fingerprint;
    private string? _watchedAlecaDirectory;
    private volatile bool _dirty = true;

    public MyFrameSnapshotProvider(IAlecaFrameReader inventoryReader,
        IAlecaCatalogReader catalogReader, IRecommendationEngine engine,
        IMyFrameSettingsStore settingsStore, IReadOnlyPriceCache prices,
        IMarketStateStore marketState, IMarketItemIndexStore marketItems,
        MyFrameLocalDataOptions? paths = null, TimeProvider? timeProvider = null,
        ISynchronizedDataReader? synchronizedData = null)
    {
        _inventoryReader = inventoryReader;
        _catalogReader = catalogReader;
        _engine = engine;
        _settingsStore = settingsStore;
        _prices = prices;
        _marketState = marketState;
        _marketItems = marketItems;
        _synchronizedData = synchronizedData;
        _paths = paths ?? MyFrameLocalDataOptions.Shared;
        _time = timeProvider ?? TimeProvider.System;
        ConfigureSharedWatcher();
    }

    public void Invalidate() => _dirty = true;

    public async Task<MyFrameSnapshot> GetAsync(string? snapshotId = null,
        CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        Prune(now);
        if (!string.IsNullOrWhiteSpace(snapshotId))
        {
            if (_retained.TryGetValue(snapshotId, out var retained)) return retained.Snapshot;
            throw new MyFrameSnapshotException("SNAPSHOT_EXPIRED",
                "The requested snapshot has expired. Start a new analysis with get_overview.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await TryLoadSettingsAsync(cancellationToken).ConfigureAwait(false);
            var directory = settings?.AlecaFrameDirectory;
            ConfigureAlecaWatcher(directory);
            var fingerprint = SourceFingerprint.Capture(_paths, directory);
            if (!_dirty && _current is not null && fingerprint == _fingerprint && now < _current.ReevaluateAt)
                return _current;

            MyFrameSnapshot snapshot;
            try
            {
                snapshot = await ComposeAsync(settings, now, cancellationToken).ConfigureAwait(false);
            }
            catch (MyFrameSnapshotException error) when (CanReuseCurrent(settings))
            {
                snapshot = FallbackSnapshot(_current!, now, error);
            }
            _fingerprint = fingerprint;
            _current = snapshot;
            _retained[snapshot.SnapshotId] = new(snapshot, now);
            _dirty = false;
            Prune(now);
            return snapshot;
        }
        finally { _gate.Release(); }
    }

    private bool CanReuseCurrent(MyFrameSettingsDocument? settings) =>
        _current is { Inventory: not null, Catalog: not null, Settings: not null } &&
        settings is not null &&
        MyFrameContext.ComputeId(_current.Settings.AlecaFrameDirectory) ==
        MyFrameContext.ComputeId(settings.AlecaFrameDirectory);

    private static MyFrameSnapshot FallbackSnapshot(MyFrameSnapshot previous,
        DateTimeOffset now, MyFrameSnapshotException error)
    {
        var sources = previous.Sources.ToDictionary(x => x.Key,
            x => x.Value with { Fallback = true, DetailCode = error.Code }, StringComparer.Ordinal);
        var warnings = previous.Warnings
            .Append(new SnapshotWarning("FALLBACK_LAST_VALID",
                "A source could not be refreshed; this response uses the last valid snapshot from the same data context."))
            .DistinctBy(x => x.Code).ToArray();
        return previous with
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            Sources = sources,
            Warnings = warnings,
            ReevaluateAt = now + TimeSpan.FromSeconds(30)
        };
    }

    private async Task<MyFrameSettingsDocument?> TryLoadSettingsAsync(CancellationToken cancellationToken)
    {
        try { return await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false); }
        catch (NotSupportedException error)
        {
            throw new MyFrameSnapshotException("STORAGE_VERSION_UNSUPPORTED", error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            throw new MyFrameSnapshotException("SOURCE_UNAVAILABLE",
                "My Frame settings could not be read. Open the app and save Settings again.", true);
        }
    }

    private async Task<MyFrameSnapshot> ComposeAsync(MyFrameSettingsDocument? settings,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var warnings = new List<SnapshotWarning>();
        var sources = new Dictionary<string, SnapshotSource>(StringComparer.Ordinal);
        var synchronized = await TryReadSynchronizedAsync(cancellationToken).ConfigureAwait(false);
        if (synchronized is not null)
            return await ComposeSynchronizedAsync(synchronized, settings,
                now, sources, warnings, cancellationToken).ConfigureAwait(false);
        if (settings is null || string.IsNullOrWhiteSpace(settings.AlecaFrameDirectory))
        {
            sources["settings"] = new("missing", null, false, "SETUP_REQUIRED");
            sources["inventory"] = new("missing", null);
            sources["catalog"] = new("missing", null);
            sources["prices"] = File.Exists(_paths.PriceCachePath)
                ? new("unavailable", File.GetLastWriteTimeUtc(_paths.PriceCachePath), false, "SETUP_REQUIRED")
                : new("missing", null);
            sources["orders"] = File.Exists(_paths.MarketStatePath)
                ? new("unavailable", File.GetLastWriteTimeUtc(_paths.MarketStatePath), false, "SETUP_REQUIRED")
                : new("missing", null);
            warnings.Add(new("SETUP_REQUIRED", "Open My Frame and save Settings before querying inventory."));
            return EmptySnapshot(now, sources, warnings, true);
        }

        sources["settings"] = new("valid", settings.UpdatedAt);
        var directory = settings.AlecaFrameDirectory;
        if (!Directory.Exists(directory) || !File.Exists(Path.Combine(directory, "lastData.dat")))
        {
            sources["inventory"] = new("missing", null, false, "SOURCE_UNAVAILABLE");
            sources["catalog"] = new("missing", null);
            sources["prices"] = new("missing", null);
            sources["orders"] = new("missing", null);
            warnings.Add(new("SOURCE_UNAVAILABLE", "The configured AlecaFrame data is unavailable. Open AlecaFrame or update Settings."));
            return EmptySnapshot(now, sources, warnings, false, settings);
        }

        InventorySnapshot inventory;
        CatalogSnapshot catalog;
        try
        {
            var inventoryTask = _inventoryReader.ReadAsync(directory, cancellationToken);
            var catalogTask = _catalogReader.LoadAsync(directory, cancellationToken);
            await Task.WhenAll(inventoryTask, catalogTask).ConfigureAwait(false);
            inventory = await inventoryTask.ConfigureAwait(false);
            catalog = await catalogTask.ConfigureAwait(false);
            sources["inventory"] = new("valid", inventory.CapturedAt);
            sources["catalog"] = new("valid", CatalogTimestamp(directory));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            throw new MyFrameSnapshotException("SOURCE_UNAVAILABLE",
                "AlecaFrame inventory or catalog is temporarily unavailable. Retry after it finishes writing.", true);
        }

        MarketItemIndex? marketIndex;
        try
        {
            marketIndex = await _marketItems.LoadAsync(cancellationToken).ConfigureAwait(false);
            sources["marketItems"] = marketIndex is null ? new("missing", null) :
                new("valid", marketIndex.RetrievedAt);
        }
        catch (Exception error) when (IsLocalReadFailure(error))
        {
            marketIndex = null;
            sources["marketItems"] = FailureSource(error, _paths.MarketItemIndexPath);
            warnings.Add(new("MARKET_INDEX_UNAVAILABLE",
                "The stored market item index could not be read; catalog identities may be incomplete."));
        }
        catalog = CatalogMarketAlignment.AlignToMarket(catalog, marketIndex);
        inventory = InventoryCatalogAlignment.AlignToCatalog(inventory, catalog);

        IReadOnlyDictionary<string, MarketQuote> loadedQuotes;
        SnapshotSource? priceFailure = null;
        try { loadedQuotes = await _prices.LoadAllAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (IsLocalReadFailure(error))
        {
            var canFallback = CanReuseCurrent(settings) && _current!.Quotes.Count > 0;
            loadedQuotes = canFallback ? _current!.Quotes :
                new Dictionary<string, MarketQuote>(StringComparer.Ordinal);
            priceFailure = FailureSource(error, _paths.PriceCachePath) with { Fallback = canFallback };
            warnings.Add(new("PRICE_CACHE_UNAVAILABLE", "Stored market prices could not be read."));
        }
        var quotes = loadedQuotes.ToDictionary(x => x.Key,
            x => x.Value with { IsStale = now - x.Value.RetrievedAt > QuoteFreshness }, StringComparer.Ordinal);
        var latestPrice = quotes.Count == 0 ? (DateTimeOffset?)null : quotes.Values.Max(x => x.RetrievedAt);
        sources["prices"] = priceFailure ?? (quotes.Count == 0 ? new("missing", null) :
            new(quotes.Values.Any(x => x.IsStale) ? "stale" : "valid", latestPrice));

        MarketState? state;
        SnapshotSource? orderFailure = null;
        var orderFallback = false;
        try { state = await _marketState.LoadAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (IsLocalReadFailure(error))
        {
            SnapshotSource? previousOrders = null;
            orderFallback = CanReuseCurrent(settings) &&
                _current!.Sources.TryGetValue("orders", out previousOrders);
            state = orderFallback ? new MarketState(_current!.Account, _current.Orders,
                previousOrders!.RetrievedAt ?? _current.GeneratedAt, "unverified",
                MyFrameContext.ComputeId(directory)) : null;
            orderFailure = FailureSource(error, _paths.MarketStatePath) with { Fallback = orderFallback };
            warnings.Add(new("ORDERS_UNAVAILABLE", "Stored market orders could not be read."));
        }
        var contextId = MyFrameContext.ComputeId(directory);
        var stateApplies = state is not null && (state.ContextId is null || state.ContextId == contextId);
        var invalidated = state?.ValidationState.Equals("invalidated", StringComparison.OrdinalIgnoreCase) == true;
        var stateConfirmed = state?.ValidationState.Equals("confirmed", StringComparison.OrdinalIgnoreCase) == true;
        var orderFresh = state is not null && now - state.RetrievedAt <= OrderFreshness;
        var orders = stateApplies && !invalidated ? state!.Orders : [];
        var account = stateApplies && !invalidated ? state!.Account : null;
        var availabilityConfirmed = stateApplies && !invalidated && stateConfirmed && orderFresh && !orderFallback;
        if (!stateApplies && state is not null)
            warnings.Add(new("ORDERS_CONTEXT_MISMATCH", "Stored orders belong to another data context and were ignored."));
        else if (invalidated)
            warnings.Add(new("ORDERS_INVALIDATED", "Stored orders were invalidated by the app."));
        else if (state is null && orderFailure is null)
            warnings.Add(new("ORDERS_UNKNOWN", "No confirmed order snapshot is available; sellable quantities are estimates."));
        else if (state is not null && (!orderFresh || orderFallback))
            warnings.Add(new("ORDERS_UNVERIFIED", "Stored orders are old; their reservations are retained conservatively."));
        sources["orders"] = orderFailure ?? (state is null ? new("missing", null) : invalidated ?
            new("invalidated", state.RetrievedAt) :
            new(availabilityConfirmed ? "valid" : "unverified", state.RetrievedAt));

        var recommendations = _engine.Evaluate(inventory, catalog, quotes, orders,
            settings.RecommendationSettings);
        var reevaluateAt = EarliestExpiry(now, quotes.Values, state);
        return new(Guid.NewGuid().ToString("N"), now, now, inventory, catalog, recommendations,
            account, orders, quotes, settings, sources, warnings, false, availabilityConfirmed, reevaluateAt);
    }

    private async Task<SynchronizedDataSnapshot?> TryReadSynchronizedAsync(CancellationToken cancellationToken)
    {
        if (_synchronizedData is null) return null;
        try { return await _synchronizedData.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<MyFrameSnapshot> ComposeSynchronizedAsync(
        SynchronizedDataSnapshot synchronized, MyFrameSettingsDocument? settings, DateTimeOffset now,
        Dictionary<string, SnapshotSource> sources, List<SnapshotWarning> warnings,
        CancellationToken cancellationToken)
    {
        var inventory = InventoryCatalogAlignment.AlignToCatalog(synchronized.Inventory, synchronized.Catalog);
        var quotes = await _prices.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var marketState = await _marketState.LoadAsync(cancellationToken).ConfigureAwait(false);
        var orders = marketState?.ValidationState.Equals("invalidated", StringComparison.OrdinalIgnoreCase) == true
            ? [] : marketState?.Orders ?? [];
        var account = marketState?.ValidationState.Equals("invalidated", StringComparison.OrdinalIgnoreCase) == true
            ? null : marketState?.Account;
        var effectiveSettings = settings ?? new MyFrameSettingsDocument(1, 0, "", 10, 1, synchronized.RetrievedAt);
        sources["settings"] = new(settings is null ? "missing" : "valid", settings?.UpdatedAt);
        sources["inventory"] = new("valid", synchronized.RetrievedAt, false, "SYNC_DATABASE");
        sources["catalog"] = new("partial", synchronized.RetrievedAt, false, "PUBLIC_EXPORT_MINIMAL");
        var latestPrice = quotes.Count == 0 ? (DateTimeOffset?)null : quotes.Values.Max(x => x.RetrievedAt);
        var pricesStale = quotes.Values.Any(quote => now - quote.RetrievedAt > QuoteFreshness);
        sources["prices"] = quotes.Count == 0 ? new("missing", null) :
            new(pricesStale ? "stale" : "valid", latestPrice);
        var ordersFresh = marketState is not null && now - marketState.RetrievedAt <= OrderFreshness;
        var ordersValid = marketState is not null &&
            !marketState.ValidationState.Equals("invalidated", StringComparison.OrdinalIgnoreCase) &&
            marketState.ValidationState.Equals("confirmed", StringComparison.OrdinalIgnoreCase) && ordersFresh;
        sources["orders"] = marketState is null ? new("missing", null) :
            new(ordersValid ? "valid" : "unverified", marketState.RetrievedAt);
        warnings.Add(new("SYNC_DATABASE_PARTIAL", "Inventory and catalog came from My Frame SQLite; catalog components and player progression fields are not observed yet."));
        var captureStatus = await CollectorCaptureStatusProbe.ReadAsync(
            MyFrameStoragePaths.CollectorCaptureDirectory, cancellationToken).ConfigureAwait(false);
        if (captureStatus.State is not "ready")
            warnings.Add(new("INVENTORY_CAPTURE_UNVERIFIED",
                $"SQLite inventory is retained, but the Overwolf capture is not ready ({captureStatus.State}); current possession requires confirmation in the game."));
        var recommendations = _engine.Evaluate(inventory, synchronized.Catalog, quotes, orders, effectiveSettings.RecommendationSettings);
        return new(Guid.NewGuid().ToString("N"), now, now, inventory, synchronized.Catalog, recommendations,
            account, orders, quotes, effectiveSettings, sources, warnings, false, ordersValid, now + TimeSpan.FromSeconds(30));
    }

    private static DateTimeOffset CatalogTimestamp(string directory)
    {
        var path = Path.Combine(directory, "cachedData", "json");
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.json")
            .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max() : DateTimeOffset.MinValue;
    }

    private static bool IsLocalReadFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException;

    private static SnapshotSource FailureSource(Exception error, string path) =>
        new(error is System.Text.Json.JsonException or NotSupportedException ? "invalid" : "unavailable",
            File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null, false,
            error is System.Text.Json.JsonException or NotSupportedException ? "SOURCE_INVALID" : "SOURCE_UNAVAILABLE");

    private static DateTimeOffset EarliestExpiry(DateTimeOffset now,
        IEnumerable<MarketQuote> quotes, MarketState? marketState)
    {
        var candidates = quotes.Select(x => x.RetrievedAt + QuoteFreshness).Where(x => x > now).ToList();
        if (marketState is not null && marketState.RetrievedAt + OrderFreshness > now)
            candidates.Add(marketState.RetrievedAt + OrderFreshness);
        return candidates.Count == 0 ? now + TimeSpan.FromSeconds(30) : candidates.Min();
    }

    private static MyFrameSnapshot EmptySnapshot(DateTimeOffset now,
        IReadOnlyDictionary<string, SnapshotSource> sources, IReadOnlyList<SnapshotWarning> warnings,
        bool setupRequired, MyFrameSettingsDocument? settings = null) =>
        new(Guid.NewGuid().ToString("N"), now, now, null, null, null, null, [],
            new Dictionary<string, MarketQuote>(StringComparer.Ordinal), settings, sources, warnings,
            setupRequired, false, now + TimeSpan.FromSeconds(30));

    private void ConfigureSharedWatcher()
    {
        var root = Path.GetDirectoryName(_paths.SettingsPath);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        _sharedWatcher?.Dispose();
        _sharedWatcher = CreateWatcher(root);
    }

    private void ConfigureAlecaWatcher(string? directory)
    {
        if (string.Equals(directory, _watchedAlecaDirectory, StringComparison.OrdinalIgnoreCase)) return;
        _alecaWatcher?.Dispose();
        _alecaWatcher = null;
        _watchedAlecaDirectory = directory;
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            _alecaWatcher = CreateWatcher(directory);
    }

    private FileSystemWatcher CreateWatcher(string directory)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            Filter = "*.*", IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        FileSystemEventHandler changed = (_, _) => Invalidate();
        RenamedEventHandler renamed = (_, _) => Invalidate();
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.Deleted += changed;
        watcher.Renamed += renamed;
        watcher.Error += (_, _) => Invalidate();
        return watcher;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var pair in _retained.Where(x => now - x.Value.RetainedAt > SnapshotRetention))
            _retained.TryRemove(pair.Key, out _);
        foreach (var pair in _retained.OrderByDescending(x => x.Value.RetainedAt).Skip(MaximumRetainedSnapshots))
            _retained.TryRemove(pair.Key, out _);
    }

    public void Dispose()
    {
        _sharedWatcher?.Dispose();
        _alecaWatcher?.Dispose();
        _gate.Dispose();
    }

    private sealed record RetainedSnapshot(MyFrameSnapshot Snapshot, DateTimeOffset RetainedAt);

    private sealed record SourceFingerprint(string Settings, string Inventory, string Catalog,
        string Prices, string Orders, string MarketItems, string DataDatabase)
    {
        public static SourceFingerprint Capture(MyFrameLocalDataOptions paths, string? alecaDirectory) => new(
            Stamp(paths.SettingsPath),
            Stamp(string.IsNullOrWhiteSpace(alecaDirectory) ? null : Path.Combine(alecaDirectory, "lastData.dat")),
            DirectoryStamp(string.IsNullOrWhiteSpace(alecaDirectory) ? null : Path.Combine(alecaDirectory, "cachedData", "json")),
            Stamp(paths.PriceCachePath), Stamp(paths.MarketStatePath), Stamp(paths.MarketItemIndexPath),
            Stamp(Path.Combine(Path.GetDirectoryName(paths.SettingsPath)!, "data.db")));

        private static string Stamp(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "missing";
            try
            {
                var info = new FileInfo(path);
                return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            }
            catch (IOException) { return "unavailable"; }
            catch (UnauthorizedAccessException) { return "unavailable"; }
        }

        private static string DirectoryStamp(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return "missing";
            try
            {
                var files = Directory.EnumerateFiles(directory, "*.json").Select(path => new FileInfo(path)).ToArray();
                return $"{files.Length}:{files.Select(x => x.LastWriteTimeUtc.Ticks).DefaultIfEmpty().Max()}:{files.Sum(x => x.Length)}";
            }
            catch (IOException) { return "unavailable"; }
            catch (UnauthorizedAccessException) { return "unavailable"; }
        }
    }
}
