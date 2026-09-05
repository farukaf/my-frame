using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class DashboardServiceTests
{
    private const string ChassisRecipe = "/Recipes/OberonPrimeChassisBlueprint";
    private const string ChassisComponent = "/Recipes/OberonPrimeChassisComponent";
    private const string RelicUnique = "/Projections/LithA1";

    [Fact]
    public async Task RecipeKeyedStackReachesTheEngineUnderItsCatalogComponentName()
    {
        using var scenario = new Scenario(chassisOwned: 2);

        await scenario.Service.RefreshAsync();

        var stackables = scenario.Engine.Inventory!.Stackables;
        Assert.Equal(2, stackables[ChassisComponent]);
        Assert.False(stackables.ContainsKey(ChassisRecipe));
    }

    [Fact]
    public async Task SpareCopiesArePricedBeforeSealedRelicsAndRewardTables()
    {
        using var scenario = new Scenario(chassisOwned: 2);

        await scenario.Service.RefreshAsync();

        Assert.Equal(
            ["oberon_prime_set", "oberon_prime_chassis_blueprint", "lith_a1_relic", "braton_prime_barrel"],
            scenario.Market.Requested);
    }

    [Fact]
    public async Task PieceWithoutASpareCopyYieldsPriorityToTheSealedRelics()
    {
        using var scenario = new Scenario(chassisOwned: 1);

        await scenario.Service.RefreshAsync();

        Assert.Equal("lith_a1_relic", scenario.Market.Requested[0]);
        Assert.Contains("oberon_prime_chassis_blueprint", scenario.Market.Requested);
        Assert.True(scenario.Market.Requested.IndexOf("lith_a1_relic") <
                    scenario.Market.Requested.IndexOf("oberon_prime_chassis_blueprint"));
    }

    [Fact]
    public async Task InventoryIsPublishedBeforeTheFirstMarketRequest()
    {
        using var scenario = new Scenario(chassisOwned: 2);

        await scenario.Service.RefreshAsync();

        Assert.Equal("publish", scenario.Timeline[0]);
        Assert.Equal("account", scenario.Timeline[1]);
        Assert.Equal("publish", scenario.Timeline[^1]);
        var first = scenario.Published[0];
        Assert.True(first.Status.IsLoading);
        Assert.NotNull(first.Recommendations);
    }

    [Fact]
    public async Task QuotesInsideTheFreshnessWindowAreNotRequestedAgain()
    {
        using var scenario = new Scenario(chassisOwned: 2,
            ("oberon_prime_set", TimeSpan.FromMinutes(2)),
            ("lith_a1_relic", TimeSpan.FromMinutes(2)));

        await scenario.Service.RefreshAsync();

        Assert.Equal(["oberon_prime_chassis_blueprint", "braton_prime_barrel"], scenario.Market.Requested);
    }

    [Fact]
    public async Task QuotesOlderThanTheFreshnessWindowAreRefreshed()
    {
        using var scenario = new Scenario(chassisOwned: 2,
            ("oberon_prime_set", TimeSpan.FromMinutes(20)),
            ("lith_a1_relic", TimeSpan.FromMinutes(2)));

        await scenario.Service.RefreshAsync();

        Assert.Contains("oberon_prime_set", scenario.Market.Requested);
        Assert.DoesNotContain("lith_a1_relic", scenario.Market.Requested);
    }

    [Fact]
    public async Task AFullyFreshCacheCostsNoRequestsAndReportsNoStalePrices()
    {
        using var scenario = new Scenario(chassisOwned: 2,
            ("oberon_prime_set", TimeSpan.FromMinutes(1)),
            ("oberon_prime_chassis_blueprint", TimeSpan.FromMinutes(1)),
            ("lith_a1_relic", TimeSpan.FromMinutes(1)),
            ("braton_prime_barrel", TimeSpan.FromMinutes(1)));

        var snapshot = await scenario.Service.RefreshAsync();

        Assert.Empty(scenario.Market.Requested);
        Assert.Equal(0, snapshot.Status.PricesStale);
        Assert.Equal("", snapshot.Status.StaleWarning);
        Assert.Equal(1d, snapshot.Status.PriceProgress);
    }

    [Fact]
    public async Task ProgressRunsFromTheCachedCountUpToEveryTrackedPrice()
    {
        using var scenario = new Scenario(chassisOwned: 2, ("oberon_prime_set", TimeSpan.FromMinutes(1)));

        var snapshot = await scenario.Service.RefreshAsync();

        Assert.Equal(1, scenario.Published[0].Status.PricesLoaded);
        Assert.Equal(4, scenario.Published[0].Status.PricesTracked);
        Assert.NotEmpty(scenario.Progress);
        Assert.Equal(4, scenario.Progress[^1].PricesLoaded);
        Assert.All(scenario.Progress, x => Assert.True(x.IsLoading));
        Assert.False(snapshot.Status.IsLoading);
        Assert.Equal(1d, snapshot.Status.PriceProgress);
    }

    [Fact]
    public async Task SlugsTheMarketCannotAnswerAreReportedAsStale()
    {
        using var scenario = new Scenario(chassisOwned: 2);
        scenario.Market.Unanswered.Add("braton_prime_barrel");

        var snapshot = await scenario.Service.RefreshAsync();

        Assert.Equal(1, snapshot.Status.PricesStale);
        Assert.Equal("1 of 4 prices are out of date.", snapshot.Status.StaleWarning);
    }

    [Fact]
    public async Task PricesAreLeftUntouchedWhenTheRefreshSkipsThem()
    {
        using var scenario = new Scenario(chassisOwned: 2);

        var snapshot = await scenario.Service.RefreshAsync(refreshPrices: false);

        Assert.Empty(scenario.Market.Requested);
        Assert.Equal(4, snapshot.Status.PricesStale);
        Assert.Equal("Prices are out of date. Showing inventory only.", snapshot.Status.StaleWarning);
    }

    private sealed class Scenario : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();

        public Scenario(int chassisOwned, params (string Slug, TimeSpan Age)[] cached)
        {
            var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
                new Dictionary<string, int> { [ChassisRecipe] = chassisOwned, [RelicUnique] = 3 },
                new HashSet<string>(), new Dictionary<string, long>(), 0, 0, "synthetic");
            Market = new RecordingMarket(Timeline);
            Cache = new SeededCache(cached);
            Service = new DashboardService(new StaticPath(_directory.Path), new StubInventory(inventory),
                new StubCatalog(Catalog()), Market, Cache, Engine);
            Service.SnapshotUpdated += (_, snapshot) =>
            {
                Timeline.Add("publish");
                Published.Add(snapshot);
            };
            Service.SyncProgressChanged += (_, status) => Progress.Add(status);
        }

        public List<string> Timeline { get; } = [];
        public List<DashboardSnapshot> Published { get; } = [];
        public List<SyncStatus> Progress { get; } = [];
        public RecordingMarket Market { get; }
        public SeededCache Cache { get; }
        public RecordingEngine Engine { get; } = new();
        public DashboardService Service { get; }

        public void Dispose()
        {
            Service.Dispose();
            _directory.Dispose();
        }

        private static CatalogSnapshot Catalog()
        {
            var warframe = new CatalogItem("/Items/OberonPrime", "Oberon Prime", "Warframes", "Suits", "",
                true, true, false, true, null, "set-id", "oberon_prime_set",
                [
                    new CatalogComponent("/Recipes/OberonPrimeBlueprint", "Blueprint", 1, 45, true),
                    new CatalogComponent(ChassisComponent, "Chassis", 1, 15, true)
                ], []);
            var relic = new CatalogItem(RelicUnique, "Lith A1 Relic", "Relics", "Projections", "",
                false, false, true, false, null, "relic-id", "lith_a1_relic", [],
                [new RelicSource("Lith A1 Relic", "common", 25.33, false, "Braton Prime Barrel")]);
            var market = new Dictionary<string, MarketIdentity>
            {
                [ItemNameNormalizer.Normalize("Oberon Prime Chassis")] = new("chassis-id", "oberon_prime_chassis_blueprint"),
                [ItemNameNormalizer.Normalize("Oberon Prime Blueprint")] = new("bp-id", "oberon_prime_blueprint"),
                [ItemNameNormalizer.Normalize("Braton Prime Barrel")] = new("barrel-id", "braton_prime_barrel")
            };
            var items = new[] { warframe, relic };
            return new CatalogSnapshot(items, items.ToDictionary(x => x.UniqueName, StringComparer.Ordinal), market);
        }
    }

    private sealed class StaticPath(string directory) : IAlecaFramePath
    {
        public string DirectoryPath { get; private set; } = directory;
        public event EventHandler<string>? Changed;
        public void SetDirectory(string directoryPath)
        {
            DirectoryPath = directoryPath;
            Changed?.Invoke(this, directoryPath);
        }
    }

    private sealed class StubInventory(InventorySnapshot snapshot) : IAlecaFrameReader
    {
        public Task<InventorySnapshot> ReadAsync(string alecaDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class StubCatalog(CatalogSnapshot snapshot) : IAlecaCatalogReader
    {
        public Task<CatalogSnapshot> LoadAsync(string alecaDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class RecordingMarket(List<string> timeline) : IWarframeMarketClient
    {
        public List<string> Requested { get; } = [];
        public HashSet<string> Unanswered { get; } = new(StringComparer.Ordinal);

        public Task<MarketAccount?> GetAccountAsync(CancellationToken cancellationToken = default)
        {
            timeline.Add("account");
            return Task.FromResult<MarketAccount?>(null);
        }

        public Task<IReadOnlyList<MarketOrder>> GetMyOrdersAsync(CancellationToken cancellationToken = default)
        {
            timeline.Add("orders");
            return Task.FromResult<IReadOnlyList<MarketOrder>>([]);
        }

        public Task<MarketQuote?> GetTopOrdersAsync(string slug, CancellationToken cancellationToken = default)
        {
            lock (Requested)
            {
                Requested.Add(slug);
                timeline.Add($"quote:{slug}");
            }
            return Task.FromResult(Unanswered.Contains(slug)
                ? null : new MarketQuote(slug, 12, 9, DateTimeOffset.UtcNow));
        }
    }

    private sealed class SeededCache : IPriceCache
    {
        private readonly Dictionary<string, MarketQuote> _quotes = new(StringComparer.Ordinal);

        public SeededCache(params (string Slug, TimeSpan Age)[] cached)
        {
            foreach (var (slug, age) in cached)
                _quotes[slug] = new MarketQuote(slug, 5, 4, DateTimeOffset.UtcNow - age);
        }

        public List<string> Written { get; } = [];

        public Task<MarketQuote?> GetAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(_quotes.GetValueOrDefault(slug));

        public Task SetAsync(MarketQuote quote, CancellationToken cancellationToken = default)
        {
            _quotes[quote.Slug] = quote;
            Written.Add(quote.Slug);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEngine : IRecommendationEngine
    {
        public InventorySnapshot? Inventory { get; private set; }
        public RecommendationResult Evaluate(InventorySnapshot inventory, CatalogSnapshot catalog,
            IReadOnlyDictionary<string, MarketQuote> quotes, IReadOnlyList<MarketOrder> myOrders,
            RecommendationSettings settings)
        {
            Inventory = inventory;
            return new RecommendationResult([], [], [], [], 0, 0, DateTimeOffset.UtcNow, settings);
        }
    }
}
