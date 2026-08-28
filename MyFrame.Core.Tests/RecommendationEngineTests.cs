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

    private static (InventorySnapshot Inventory, CatalogSnapshot Catalog) Scenario(int ownedParts, bool ownedEquipment)
    {
        const string partUnique = "/Parts/TestBlueprint";
        const string itemUnique = "/Items/TestPrime";
        var component = new CatalogComponent(partUnique, "Blueprint", 1, 45, true);
        var item = new CatalogItem(itemUnique, "Test Prime", "Warframes", "Suits", "", true, true,
            true, true, null, "set-id", "test_prime_set", [component], []);
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
