using System.Text.Json;
using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class McpRuleRegressionTests
{
    [Fact]
    public void PartialRelicPricesNeverProduceAComparativeRecommendation()
    {
        var relic = new CatalogItem("/relic", "Lith T1 Relic", "Relics", "", "",
            false, false, true, false, null, "relic-id", "lith_t1_relic", [],
            [new("Lith T1 Relic", "Common", 25, false, "Alpha Prime Blueprint"),
             new("Lith T1 Relic", "Rare", 10, true, "Beta Prime Blueprint")]);
        var catalog = Catalog([relic], new Dictionary<string, MarketIdentity>
        {
            [ItemNameNormalizer.Normalize("Alpha Prime Blueprint")] = new("alpha", "alpha_prime_blueprint"),
            [ItemNameNormalizer.Normalize("Beta Prime Blueprint")] = new("beta", "beta_prime_blueprint")
        });
        var inventory = Inventory(new Dictionary<string, int> { [relic.UniqueName] = 2 });
        var now = DateTimeOffset.UtcNow;
        var quotes = new Dictionary<string, MarketQuote>
        {
            ["lith_t1_relic"] = new("lith_t1_relic", 4, 3, now),
            ["alpha_prime_blueprint"] = new("alpha_prime_blueprint", 20, 18, now)
        };

        var result = new RecommendationEngine().Evaluate(inventory, catalog, quotes, [], new());

        var recommendation = Assert.Single(result.Relics);
        Assert.Equal("Hold", recommendation.Action);
        Assert.Equal(1, recommendation.RewardPricesKnown);
        Assert.Equal(2, recommendation.RewardPricesRequired);
        Assert.Null(recommendation.CompleteExpectedOpenValueEach);
        Assert.Equal(5, recommendation.ExpectedOpenValueEach);
        Assert.Equal("relic_insufficient_data", recommendation.ReasonCode);
    }

    [Fact]
    public void SetIsNotAllocatedWhenAnyComponentPriceIsMissing()
    {
        var item = new CatalogItem("/set", "Test Prime", "Warframes", "Suits", "",
            true, true, true, false, null, "set-id", "test_prime_set",
            [new("/bp", "Blueprint", 1, 45, true), new("/systems", "Systems", 1, 15, true)], []);
        var catalog = Catalog([item], new Dictionary<string, MarketIdentity>
        {
            [ItemNameNormalizer.Normalize("Test Prime Blueprint")] = new("bp", "test_prime_blueprint"),
            [ItemNameNormalizer.Normalize("Test Prime Systems")] = new("systems", "test_prime_systems")
        });
        var inventory = Inventory(new Dictionary<string, int> { ["/bp"] = 2, ["/systems"] = 2 },
            [item.UniqueName]);
        var now = DateTimeOffset.UtcNow;
        var quotes = new Dictionary<string, MarketQuote>
        {
            ["test_prime_set"] = new("test_prime_set", 100, 90, now),
            ["test_prime_blueprint"] = new("test_prime_blueprint", 10, 9, now)
        };

        var result = new RecommendationEngine().Evaluate(inventory, catalog, quotes, [],
            new RecommendationSettings(10, 0));

        Assert.DoesNotContain(result.Sales, value => value.UniqueName == item.UniqueName);
        Assert.Contains(result.Sales, value => value.UniqueName == "/bp");
        Assert.Contains(result.Sales, value => value.UniqueName == "/systems");
    }

    [Fact]
    public void CompleteSetAllocationPreventsPartsFromBeingSoldTwice()
    {
        var item = new CatalogItem("/set", "Test Prime", "Warframes", "Suits", "",
            true, true, true, false, null, "set-id", "test_prime_set",
            [new("/bp", "Blueprint", 1, 45, true), new("/systems", "Systems", 1, 15, true)], []);
        var catalog = Catalog([item], new Dictionary<string, MarketIdentity>
        {
            [ItemNameNormalizer.Normalize("Test Prime Blueprint")] = new("bp", "test_prime_blueprint"),
            [ItemNameNormalizer.Normalize("Test Prime Systems")] = new("systems", "test_prime_systems")
        });
        var inventory = Inventory(new Dictionary<string, int> { ["/bp"] = 2, ["/systems"] = 2 },
            [item.UniqueName]);
        var now = DateTimeOffset.UtcNow;
        var quotes = new Dictionary<string, MarketQuote>
        {
            ["test_prime_set"] = new("test_prime_set", 100, 90, now),
            ["test_prime_blueprint"] = new("test_prime_blueprint", 10, 9, now),
            ["test_prime_systems"] = new("test_prime_systems", 20, 18, now)
        };

        var result = new RecommendationEngine().Evaluate(inventory, catalog, quotes, [],
            new RecommendationSettings(10, 0));

        var setSale = Assert.Single(result.Sales);
        Assert.Equal(item.UniqueName, setSale.UniqueName);
        Assert.Equal(2, setSale.Excess);
        Assert.Equal(2, setSale.AllocatedComponents!["/bp"]);
        Assert.Equal(2, setSale.AllocatedComponents["/systems"]);
        Assert.Equal(2, result.SetAllocations!["/bp"]);
        Assert.All(result.Surplus, value =>
        {
            Assert.Equal(2, value.AllocatedToSets);
            Assert.Equal(0, value.AvailableToSell);
        });
    }

    [Fact]
    public void FarmCountsMissingUnitsAndOnlyRelicsForMissingRewards()
    {
        var target = new CatalogItem("/target", "Target Prime", "Warframes", "Suits", "",
            true, true, true, false, null, "set", "target_prime_set",
            [new("/bp", "Blueprint", 2, 15, true), new("/systems", "Systems", 2, 15, true)],
            [new("Lith A1 Relic", "Common", 25, false, "Target Prime Blueprint"),
             new("Meso B1 Relic", "Common", 25, false, "Target Prime Systems")]);
        var lith = new CatalogItem("/lith", "Lith A1 Relic", "Relics", "", "",
            false, false, true, false, null, null, null, [], []);
        var catalog = Catalog([target, lith], new Dictionary<string, MarketIdentity>());
        var inventory = Inventory(new Dictionary<string, int>
        {
            ["/bp"] = 1,
            ["/lith"] = 8
        });

        var result = new RecommendationEngine().Evaluate(inventory, catalog,
            new Dictionary<string, MarketQuote>(), [], new());

        var farm = Assert.Single(result.Farm);
        Assert.Equal(2, farm.MissingParts);
        Assert.Equal(3, farm.MissingUnits);
        Assert.Equal(1, farm.OwnedRelics);
        Assert.Null(farm.MissingPartsCostPlatinum);
    }

    [Fact]
    public void SurplusSeparatesCollectionNeedFromOrderReservations()
    {
        var item = new CatalogItem("/target", "Target Prime", "Warframes", "Suits", "",
            true, true, true, false, null, "set", "target_prime_set",
            [new("/bp", "Blueprint", 1, 15, true)], []);
        var catalog = Catalog([item], new Dictionary<string, MarketIdentity>
        {
            [ItemNameNormalizer.Normalize("Target Prime Blueprint")] = new("bp-id", "target_prime_blueprint")
        });
        var inventory = Inventory(new Dictionary<string, int> { ["/bp"] = 5 }, [item.UniqueName]);
        var orders = new[] { new MarketOrder("order", "bp-id", "target_prime_blueprint", "sell", 10, 2, true) };

        var result = new RecommendationEngine().Evaluate(inventory, catalog,
            new Dictionary<string, MarketQuote>(), orders, new RecommendationSettings(10, 0));

        var surplus = Assert.Single(result.Surplus);
        Assert.Equal(5, surplus.Surplus);
        Assert.Equal(2, surplus.Reserved);
        Assert.Equal(3, surplus.AvailableToSell);
    }

    [Fact]
    public async Task ReadOnlyBulkPriceLoadReportsInvalidJson()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "quotes.json");
        await File.WriteAllTextAsync(path, "{ definitely not json");
        var cache = new JsonPriceCache(path);

        await Assert.ThrowsAsync<JsonException>(() => cache.LoadAllAsync());
        Assert.Null(await cache.GetAsync("anything"));
    }

    private static CatalogSnapshot Catalog(CatalogItem[] items,
        IReadOnlyDictionary<string, MarketIdentity> market) => new(items,
        items.ToDictionary(x => x.UniqueName, StringComparer.Ordinal), market);

    private static InventorySnapshot Inventory(Dictionary<string, int> stackables,
        HashSet<string>? equipment = null) => new(DateTimeOffset.UtcNow, stackables,
        equipment ?? new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, long>(),
        0, 0, "synthetic");

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "my-frame-core-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
