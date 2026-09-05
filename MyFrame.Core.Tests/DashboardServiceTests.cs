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

    private sealed class Scenario : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();

        public Scenario(int chassisOwned)
        {
            var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
                new Dictionary<string, int> { [ChassisRecipe] = chassisOwned, [RelicUnique] = 3 },
                new HashSet<string>(), new Dictionary<string, long>(), 0, 0, "synthetic");
            Service = new DashboardService(new StaticPath(_directory.Path), new StubInventory(inventory),
                new StubCatalog(Catalog()), Market, new EmptyCache(), Engine);
        }

        public RecordingMarket Market { get; } = new();
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

    private sealed class RecordingMarket : IWarframeMarketClient
    {
        public List<string> Requested { get; } = [];
        public Task<MarketAccount?> GetAccountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MarketAccount?>(null);
        public Task<IReadOnlyList<MarketOrder>> GetMyOrdersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MarketOrder>>([]);
        public Task<MarketQuote?> GetTopOrdersAsync(string slug, CancellationToken cancellationToken = default)
        {
            lock (Requested) Requested.Add(slug);
            return Task.FromResult<MarketQuote?>(new MarketQuote(slug, 12, 9, DateTimeOffset.UtcNow));
        }
    }

    private sealed class EmptyCache : IPriceCache
    {
        public Task<MarketQuote?> GetAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult<MarketQuote?>(null);
        public Task SetAsync(MarketQuote quote, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
