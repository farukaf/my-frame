using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class InventoryCatalogAlignmentTests
{
    private const string ChassisComponent = "/Lotus/Types/Recipes/WarframeRecipes/OberonPrimeChassisComponent";
    private const string ChassisRecipe = "/Lotus/Types/Recipes/WarframeRecipes/OberonPrimeChassisBlueprint";
    private const string StockComponent = "/Lotus/Types/Recipes/Weapons/WeaponParts/ThanotechRifleStock";
    private const string StockRecipe = "/Lotus/Types/Recipes/Weapons/WeaponParts/ThanotechRifleStockBlueprint";
    private const string SetBlueprint = "/Lotus/Types/Recipes/WarframeRecipes/OberonPrimeBlueprint";

    [Fact]
    public void WarframePartRecipeIsRekeyedOntoItsCatalogComponent()
    {
        var aligned = Align(new Dictionary<string, int> { [ChassisRecipe] = 2 });

        Assert.Equal(2, aligned.Stackables[ChassisComponent]);
        Assert.False(aligned.Stackables.ContainsKey(ChassisRecipe));
    }

    [Fact]
    public void WeaponPartRecipeIsRekeyedOntoTheBareComponentName()
    {
        var aligned = Align(new Dictionary<string, int> { [StockRecipe] = 3 });

        Assert.Equal(3, aligned.Stackables[StockComponent]);
        Assert.False(aligned.Stackables.ContainsKey(StockRecipe));
    }

    [Fact]
    public void ComponentSuffixWinsOverTheBareNameWhenBothExist()
    {
        var ambiguous = new CatalogComponent(ChassisRecipe[..^"Blueprint".Length], "Chassis", 1, 15, true);
        var aligned = Align(new Dictionary<string, int> { [ChassisRecipe] = 1 }, ambiguous);

        Assert.Equal(1, aligned.Stackables[ChassisComponent]);
        Assert.False(aligned.Stackables.ContainsKey(ambiguous.UniqueName));
    }

    [Fact]
    public void KeysAlreadyMatchingTheCatalogAreLeftUntouched()
    {
        var stackables = new Dictionary<string, int> { [SetBlueprint] = 1, [ChassisComponent] = 4 };

        var aligned = Align(stackables);

        Assert.Equal(1, aligned.Stackables[SetBlueprint]);
        Assert.Equal(4, aligned.Stackables[ChassisComponent]);
        Assert.Equal(2, aligned.Stackables.Count);
    }

    [Fact]
    public void RecipeAndComponentSpellingsOfTheSamePieceAreSummed()
    {
        var aligned = Align(new Dictionary<string, int> { [ChassisRecipe] = 2, [ChassisComponent] = 1 });

        Assert.Equal(3, aligned.Stackables[ChassisComponent]);
        Assert.False(aligned.Stackables.ContainsKey(ChassisRecipe));
    }

    [Fact]
    public void RecipeWithoutAnyCatalogCounterpartKeepsItsOwnKey()
    {
        const string unrelated = "/Lotus/Types/Recipes/AbilityOverrides/RhinoRoarBlueprint";

        var aligned = Align(new Dictionary<string, int> { [unrelated] = 1 });

        Assert.Equal(1, aligned.Stackables[unrelated]);
        Assert.Single(aligned.Stackables);
    }

    [Fact]
    public void OberonPrimeChassisBecomesASaleRecommendationOnceAligned()
    {
        var catalog = Catalog();
        var inventory = Inventory(new Dictionary<string, int> { [ChassisRecipe] = 2 });
        var quotes = new Dictionary<string, MarketQuote>
        {
            ["oberon_prime_chassis"] = new("oberon_prime_chassis", 12, 9, DateTimeOffset.UtcNow)
        };
        var settings = new RecommendationSettings(10, 0);

        Assert.Empty(new RecommendationEngine().Evaluate(inventory, catalog, quotes, [], settings).Sales);

        var aligned = InventoryCatalogAlignment.AlignToCatalog(inventory, catalog);
        var sale = Assert.Single(new RecommendationEngine().Evaluate(aligned, catalog, quotes, [], settings).Sales);

        Assert.Equal(ChassisComponent, sale.UniqueName);
        Assert.Equal(2, sale.Owned);
        Assert.Equal(1, sale.ReservedForCraft);
        Assert.Equal(1, sale.Excess);
        Assert.Equal(RecommendationAction.SellForPlatinum, sale.Action);
    }

    [Fact]
    public void APartRowShowsThePartsOwnIconRatherThanTheBuiltItem()
    {
        var sale = Assert.Single(SalesFor(Catalog()));

        Assert.Equal("https://cdn.warframestat.us/img/GenericWarframePrimeChassis.png", sale.ImageUrl);
    }

    [Fact]
    public void APartWithoutAnIconFallsBackToTheParentArt()
    {
        var catalog = Catalog();
        var chassis = catalog.Items[0].Components.Single(x => x.UniqueName == ChassisComponent) with { ImageName = "" };
        var warframe = catalog.Items[0] with
        {
            Components = [catalog.Items[0].Components[0], chassis]
        };
        var items = new[] { warframe, catalog.Items[1] };

        var sale = Assert.Single(SalesFor(catalog with
        {
            Items = items,
            ByUniqueName = items.ToDictionary(x => x.UniqueName, StringComparer.Ordinal)
        }));

        Assert.Equal("https://cdn.warframestat.us/img/PaladinPrime.png", sale.ImageUrl);
    }

    [Fact]
    public void TheMainBlueprintKeepsTheParentArtBecauseEveryItemSharesOneBlueprintIcon()
    {
        var catalog = Catalog();
        var inventory = Inventory(new Dictionary<string, int> { [SetBlueprint] = 2, [ChassisComponent] = 2 });
        var sales = new RecommendationEngine().Evaluate(
            InventoryCatalogAlignment.AlignToCatalog(inventory, catalog), catalog,
            new Dictionary<string, MarketQuote>(), [], new RecommendationSettings(10, 0)).Sales;

        var blueprint = sales.Single(x => x.UniqueName == SetBlueprint);
        var chassis = sales.Single(x => x.UniqueName == ChassisComponent);

        Assert.Equal("https://cdn.warframestat.us/img/PaladinPrime.png", blueprint.ImageUrl);
        Assert.Equal("https://cdn.warframestat.us/img/GenericWarframePrimeChassis.png", chassis.ImageUrl);
    }

    private static IReadOnlyList<SaleRecommendation> SalesFor(CatalogSnapshot catalog)
    {
        var aligned = InventoryCatalogAlignment.AlignToCatalog(
            Inventory(new Dictionary<string, int> { [ChassisRecipe] = 2 }), catalog);
        return new RecommendationEngine().Evaluate(aligned, catalog,
            new Dictionary<string, MarketQuote>(), [], new RecommendationSettings(10, 0)).Sales;
    }

    private static InventorySnapshot Align(IDictionary<string, int> stackables, params CatalogComponent[] extra) =>
        InventoryCatalogAlignment.AlignToCatalog(Inventory(stackables), Catalog(extra));

    private static InventorySnapshot Inventory(IDictionary<string, int> stackables) =>
        new(DateTimeOffset.UtcNow, new Dictionary<string, int>(stackables, StringComparer.Ordinal),
            new HashSet<string>(), new Dictionary<string, long>(), 0, 0, "synthetic");

    private static CatalogSnapshot Catalog(params CatalogComponent[] extra)
    {
        var warframe = new CatalogItem("/Lotus/Powersuits/Paladin/PaladinPrime", "Oberon Prime", "Warframes",
            "Suits", "PaladinPrime.png", true, true, false, true, null, "set-id", "oberon_prime_set",
            [
                new CatalogComponent(SetBlueprint, "Blueprint", 1, 45, true, "blueprint.png"),
                new CatalogComponent(ChassisComponent, "Chassis", 1, 15, true, "GenericWarframePrimeChassis.png"),
                .. extra
            ], []);
        var weapon = new CatalogItem("/Lotus/Weapons/Tenno/LongGuns/Trumna", "Trumna", "Primary", "LongGuns",
            "", true, false, false, false, null, null, null,
            [new CatalogComponent(StockComponent, "Stock", 1, 0, false)], []);
        var market = new Dictionary<string, MarketIdentity>
        {
            [ItemNameNormalizer.Normalize("Oberon Prime Chassis")] = new("chassis-id", "oberon_prime_chassis")
        };
        var items = new[] { warframe, weapon };
        return new CatalogSnapshot(items, items.ToDictionary(x => x.UniqueName, StringComparer.Ordinal), market);
    }
}
