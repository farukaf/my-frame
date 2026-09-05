using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class RecommendationEngineTests
{
    [Fact]
    public void ReservesRequiredSetBeforeRecommendingExcess()
    {
        var (inventory, catalog) = Scenario(ownedParts: 3, ownedEquipment: false);
        var quote = new MarketQuote("test_prime_blueprint", 8, 6, DateTimeOffset.UtcNow);

        var result = new RecommendationEngine().Evaluate(inventory, catalog,
            new Dictionary<string, MarketQuote> { [quote.Slug] = quote }, [],
            new RecommendationSettings(10, false));

        var sale = Assert.Single(result.Sales);
        Assert.Equal(1, sale.Reserved);
        Assert.Equal(2, sale.Excess);
        Assert.Equal(RecommendationAction.SellForPlatinum, sale.Action);
    }

    [Fact]
    public void NeverRecommendsRequiredPieceAndChoosesDucatsAtConfiguredRatio()
    {
        var (inventory, catalog) = Scenario(ownedParts: 2, ownedEquipment: false);
        var quote = new MarketQuote("test_prime_blueprint", 2, 1, DateTimeOffset.UtcNow);

        var result = new RecommendationEngine().Evaluate(inventory, catalog,
            new Dictionary<string, MarketQuote> { [quote.Slug] = quote }, [],
            new RecommendationSettings(10, false));

        var sale = Assert.Single(result.Sales);
        Assert.Equal(1, sale.Excess);
        Assert.Equal(RecommendationAction.ExchangeForDucats, sale.Action);
        Assert.Equal(45, result.TotalDucats);
        Assert.Equal("Exchange 1 · keep 1 to craft · 45 ducats", sale.ActionLabel);
    }

    [Fact]
    public void FullyReservedPieceIsShownAsKeepRecommendation()
    {
        var (inventory, catalog) = Scenario(ownedParts: 1, ownedEquipment: false);

        var result = new RecommendationEngine().Evaluate(inventory, catalog,
            new Dictionary<string, MarketQuote>(), [], new RecommendationSettings(10, false));

        var recommendation = Assert.Single(result.Sales);
        Assert.Equal(RecommendationAction.Keep, recommendation.Action);
        Assert.Equal(1, recommendation.Reserved);
        Assert.Equal(0, recommendation.Excess);
        Assert.Equal("keep 1 to craft · do not sell or exchange now", recommendation.ActionLabel);
    }

    [Fact]
    public void VaultedUncraftedItemKeepsOneForCraftAndOneForFutureSale()
    {
        var (inventory, baseCatalog) = Scenario(ownedParts: 3, ownedEquipment: false);
        var vaulted = baseCatalog.Items.Single() with { Vaulted = true };
        var catalog = baseCatalog with
        {
            Items = [vaulted],
            ByUniqueName = new Dictionary<string, CatalogItem> { [vaulted.UniqueName] = vaulted }
        };

        var result = new RecommendationEngine().Evaluate(inventory, catalog,
            new Dictionary<string, MarketQuote>(), [], new RecommendationSettings(10, false));

        var recommendation = Assert.Single(result.Sales);
        Assert.Equal(2, recommendation.Reserved);
        Assert.Equal(1, recommendation.ReservedForCraft);
        Assert.Equal(1, recommendation.ReservedForFutureSale);
        Assert.Equal(1, recommendation.Excess);
        Assert.Contains("keep 1 to craft · keep 1 to sell after vault", recommendation.ActionLabel);
    }

    [Fact]
    public void DoesNotMarkItemReadyWhenNonTradableComponentsAreMissing()
    {
        const string itemUnique = "/Items/AeolakLike";
        const string resourceUnique = "/Resources/TradableResource";
        var item = new CatalogItem(itemUnique, "Aeolak-like", "Primary", "LongGuns", "",
            true, false, false, false, null, null, null,
            [
                new(resourceUnique, "Resource", 50, 0, true),
                new("/Parts/Barrel", "Barrel", 1, 0, false),
                new("/Parts/Blueprint", "Blueprint", 1, 0, false),
                new("/Parts/Receiver", "Receiver", 1, 0, false),
                new("/Parts/Stock", "Stock", 1, 0, false)
            ], []);
        var catalog = new CatalogSnapshot([item],
            new Dictionary<string, CatalogItem> { [itemUnique] = item },
            new Dictionary<string, MarketIdentity>());
        var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
            new Dictionary<string, int> { [resourceUnique] = 50 }, new HashSet<string>(),
            new Dictionary<string, long>(), 0, 0, "synthetic");

        var result = new RecommendationEngine().Evaluate(inventory, catalog,
            new Dictionary<string, MarketQuote>(), [], new());

        var goal = Assert.Single(result.Collection);
        Assert.Equal("In progress", goal.Status);
        Assert.Equal(4, Assert.Single(result.Farm).MissingParts);
    }

    [Fact]
    public void ExistingSellOrderAddsToReservation()
    {
        var (inventory, catalog) = Scenario(ownedParts: 4, ownedEquipment: true);
        var orders = new[] { new MarketOrder("order", "part-id", "test_prime_blueprint", "sell", 10, 2, true) };

        var result = new RecommendationEngine().Evaluate(inventory, catalog, new Dictionary<string, MarketQuote>(),
            orders, new RecommendationSettings(10, false));

        var sale = Assert.Single(result.Sales);
        Assert.Equal(2, sale.Reserved);
        Assert.Equal(2, sale.Excess);
    }

    [Fact]
    public void DuplicateRelicNamesDoNotBreakFarmRecommendations()
    {
        var (inventory, baseCatalog) = Scenario(ownedParts: 0, ownedEquipment: false);
        var relicA = new CatalogItem("/Relics/A", "Lith G12 Exceptional", "Relics", "", "",
            false, false, false, false, null, null, null, [], []);
        var relicB = relicA with { UniqueName = "/Relics/B" };
        var items = baseCatalog.Items.Concat([relicA, relicB]).ToArray();
        var catalog = baseCatalog with
        {
            Items = items,
            ByUniqueName = items.ToDictionary(item => item.UniqueName, StringComparer.Ordinal)
        };

        var result = new RecommendationEngine().Evaluate(inventory, catalog,
            new Dictionary<string, MarketQuote>(), [], new RecommendationSettings(10, false));

        Assert.Single(result.Farm);
    }

    [Fact]
    public void RecommendsOpeningRelicWhenExpectedRewardsBeatSealedPrice()
    {
        const string relicUnique = "/Relics/LithT1";
        var relic = new CatalogItem(relicUnique, "Lith T1 Relic", "Relics", "", "lith.png",
            false, false, true, true, null, "relic-id", "lith_t1_relic", [],
            [new RelicSource("Lith T1 Relic", "Rare", 10, true, "Valuable Prime Blueprint")]);
        var catalog = new CatalogSnapshot([relic],
            new Dictionary<string, CatalogItem> { [relicUnique] = relic },
            new Dictionary<string, MarketIdentity>
            {
                [ItemNameNormalizer.Normalize("Valuable Prime Blueprint")] = new("reward-id", "valuable_prime_blueprint")
            });
        var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
            new Dictionary<string, int> { [relicUnique] = 3 }, new HashSet<string>(),
            new Dictionary<string, long>(), 0, 0, "synthetic");
        var quotes = new Dictionary<string, MarketQuote>
        {
            ["lith_t1_relic"] = new("lith_t1_relic", 4, 3, DateTimeOffset.UtcNow),
            ["valuable_prime_blueprint"] = new("valuable_prime_blueprint", 100, 90, DateTimeOffset.UtcNow)
        };

        var result = new RecommendationEngine().Evaluate(inventory, catalog, quotes, [], new());

        var recommendation = Assert.Single(result.Relics);
        Assert.Equal("Open", recommendation.Action);
        Assert.Equal(10, recommendation.ExpectedOpenValueEach);
        Assert.Equal(30, recommendation.TotalExpectedOpenValue);
    }

    private static (InventorySnapshot Inventory, CatalogSnapshot Catalog) Scenario(int ownedParts, bool ownedEquipment)
    {
        const string partUnique = "/Parts/TestBlueprint";
        const string itemUnique = "/Items/TestPrime";
        var component = new CatalogComponent(partUnique, "Blueprint", 1, 45, true);
        var item = new CatalogItem(itemUnique, "Test Prime", "Warframes", "Suits", "", true, true,
            true, false, null, "set-id", "test_prime_set", [component], []);
        var market = new Dictionary<string, MarketIdentity>
        {
            [ItemNameNormalizer.Normalize("Test Prime Blueprint")] = new("part-id", "test_prime_blueprint")
        };
        var catalog = new CatalogSnapshot([item], new Dictionary<string, CatalogItem> { [itemUnique] = item }, market);
        var equipment = ownedEquipment ? new HashSet<string> { itemUnique } : new HashSet<string>();
        var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
            new Dictionary<string, int> { [partUnique] = ownedParts }, equipment,
            new Dictionary<string, long>(), 0, 0, "synthetic");
        return (inventory, catalog);
    }
}
