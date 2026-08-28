using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class AlecaCatalogReaderTests
{
    [Fact]
    public async Task LoadsComponentsMarketIdentityAndRelicSources()
    {
        using var directory = new TemporaryDirectory();
        var catalogDirectory = System.IO.Path.Combine(directory.Path, "cachedData", "json");
        Directory.CreateDirectory(catalogDirectory);
        await File.WriteAllTextAsync(System.IO.Path.Combine(catalogDirectory, "Items.json"), """
            [
              {"uniqueName":"/Items/TestPrime","name":"Test Prime","category":"Warframes","productCategory":"Suits",
               "masterable":true,"isPrime":true,"tradable":true,
               "marketInfo":{"id":"set-id","urlName":"test_prime_set"},
               "components":[{"uniqueName":"/Parts/TestBlueprint","name":"Blueprint","itemCount":1,"ducats":45,"tradable":true}]},
              {"uniqueName":"/Relics/LithT1","name":"Lith T1 Relic","category":"Relics","rewards":[
                {"rarity":"Rare","chance":10,"item":{"name":"Test Prime Blueprint","warframeMarket":{"id":"part-id","urlName":"test_prime_blueprint"}}}
              ]}
            ]
            """);

        var result = await new AlecaCatalogReader().LoadAsync(directory.Path);

        var item = result.ByUniqueName["/Items/TestPrime"];
        Assert.Equal("test_prime_set", item.MarketSlug);
        Assert.Equal(45, Assert.Single(item.Components).Ducats);
        Assert.Equal("Lith T1 Relic", Assert.Single(item.Relics).RelicName);
        Assert.Equal("test_prime_blueprint", result.MarketByNormalizedName["testprime"].Slug);
    }
}
