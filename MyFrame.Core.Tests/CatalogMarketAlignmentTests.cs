using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class CatalogMarketAlignmentTests
{
    private const string ParallaxUnique = "/Lotus/Types/Items/Ships/ZarimanShip";
    private const string AvionicsUnique = "/Lotus/Types/Recipes/LandingCraftRecipes/ZarimanShip/ZarimanShipAvionicsComponent";

    [Fact]
    public void MarksAPartTradableWhenTheMarketNamesItAndTheCatalogDoesNot()
    {
        // The real case: AlecaFrame calls Parallax Avionics untradable and gives it no market
        // identity, while Warframe.Market lists parallax_avionics_blueprint with live sell orders.
        var catalog = Catalog(tradable: false);
        var index = Index(("Parallax Avionics Blueprint", "parallax_avionics_blueprint"));

        var aligned = CatalogMarketAlignment.AlignToMarket(catalog, index);

        var component = Assert.Single(aligned.Items).Components[0];
        Assert.True(component.Tradable);
        Assert.Equal("parallax_avionics_blueprint",
            aligned.MarketByNormalizedName[ItemNameNormalizer.Normalize("Parallax Avionics")].Slug);
    }

    [Fact]
    public void LeavesAPartAloneWhenTheMarketDoesNotNameIt()
    {
        var aligned = CatalogMarketAlignment.AlignToMarket(Catalog(tradable: false),
            Index(("Something Else", "something_else")));

        Assert.False(Assert.Single(aligned.Items).Components[0].Tradable);
        Assert.Empty(aligned.MarketByNormalizedName.Where(x => x.Value.Slug.Contains("parallax")));
    }

    [Fact]
    public void KeepsTheIdentityTheCatalogAlreadyResolved()
    {
        var catalog = new CatalogSnapshot(Catalog(tradable: true).Items,
            Catalog(tradable: true).ByUniqueName,
            new Dictionary<string, MarketIdentity>(StringComparer.Ordinal)
            {
                [ItemNameNormalizer.Normalize("Parallax Avionics")] = new("catalog-id", "catalog_slug")
            });

        var aligned = CatalogMarketAlignment.AlignToMarket(catalog,
            Index(("Parallax Avionics Blueprint", "market_slug")));

        Assert.Equal("catalog_slug",
            aligned.MarketByNormalizedName[ItemNameNormalizer.Normalize("Parallax Avionics")].Slug);
    }

    [Fact]
    public void ReturnsTheSameCatalogWhenThereIsNoIndex()
    {
        var catalog = Catalog(tradable: false);
        Assert.Same(catalog, CatalogMarketAlignment.AlignToMarket(catalog, null));
    }

    private static CatalogSnapshot Catalog(bool tradable)
    {
        var item = new CatalogItem(ParallaxUnique, "Parallax", "Misc", "", "", false, false, false,
            false, null, null, null, [new(AvionicsUnique, "Avionics", 1, 0, tradable)], [], "Orbiter");
        return new CatalogSnapshot([item],
            new Dictionary<string, CatalogItem> { [ParallaxUnique] = item },
            new Dictionary<string, MarketIdentity>(StringComparer.Ordinal));
    }

    private static MarketItemIndex Index(params (string Name, string Slug)[] entries) =>
        new(entries.ToDictionary(x => ItemNameNormalizer.Normalize(x.Name),
            x => new MarketIdentity(x.Slug + "-id", x.Slug), StringComparer.Ordinal),
            DateTimeOffset.UtcNow);
}
