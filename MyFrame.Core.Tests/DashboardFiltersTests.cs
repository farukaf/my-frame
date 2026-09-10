using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class DashboardFiltersTests
{
    [Fact]
    public void CollectionInProgressUsesPartialUnownedItemsAndClosestFirst()
    {
        var source = new[]
        {
            Collection("Complete", owned: true, mastered: false, parts: 2, completion: 1),
            Collection("Started", owned: false, mastered: false, parts: 1, completion: .5),
            Collection("Empty", owned: false, mastered: false, parts: 0, completion: 0),
            Collection("Mastered", owned: true, mastered: true, parts: 2, completion: 1)
        };

        var result = DashboardFilters.FilterCollection(source, "In progress", "Closest to completion", null);

        Assert.Equal(["Started"], result.Select(x => x.ItemName));
    }

    [Fact]
    public void CollectionSortAndSearchAreCaseInsensitiveAndUseNameAsTieBreaker()
    {
        var source = new[]
        {
            Collection("Beta Prime", false, false, 1, .5, category: "Warframes", prime: true),
            Collection("Alpha Prime", false, false, 1, .5, category: "Warframes", prime: true),
            Collection("Unrelated", false, false, 1, .5, category: "Weapons", prime: false)
        };

        var result = DashboardFilters.FilterCollection(source, "Prime only", "Closest to completion", " PRIME ");

        Assert.Equal(["Alpha Prime", "Beta Prime"], result.Select(x => x.ItemName));
    }

    [Fact]
    public void SalesFilterExcludesVaultedPartsAndReportsDucatsForListedRows()
    {
        var source = new[]
        {
            Sale("Zulu", RecommendationAction.ExchangeForDucats, ducats: 15, excess: 2),
            Sale("Alpha", RecommendationAction.Keep, ducats: 100, excess: 3),
            Sale("Vaulted", RecommendationAction.ExchangeForDucats, ducats: 25, excess: 1, vaulted: true)
        };

        var result = DashboardFilters.FilterSales(source, "All recommendations", "Name", null, false);

        Assert.Equal(["Alpha", "Zulu"], result.Items.Select(x => x.ItemName));
        Assert.Equal("30", result.DucatsEstimate);
    }

    [Fact]
    public void SalesSearchMatchesReasonAndHighestValueUsesNameAsTieBreaker()
    {
        var source = new[]
        {
            Sale("Beta", RecommendationAction.SellForPlatinum, platinum: 20, reason: "Market order"),
            Sale("Alpha", RecommendationAction.SellForPlatinum, platinum: 20, reason: "Market order"),
            Sale("Other", RecommendationAction.SellForPlatinum, platinum: 80, reason: "Different")
        };

        var result = DashboardFilters.FilterSales(source, "All recommendations", "Highest value", "market", true);

        Assert.Equal(["Alpha", "Beta"], result.Items.Select(x => x.ItemName));
    }

    [Fact]
    public void FarmAndRelicListsKeepTheirDisplayCaps()
    {
        var farm = Enumerable.Range(0, 105).Select(i => new FarmRecommendation(
            $"Farm {i}", "Weapons", 1, 2, 0, false, null, ["part"], "reason", "", null));
        var relics = Enumerable.Range(0, 205).Select(i => new RelicRecommendation(
            $"Relic {i}", $"/Relics/{i}", 1, false, null, 0, "Open", "reason", "", null));

        Assert.Equal(100, DashboardFilters.FilterFarm(farm, null).Count);
        Assert.Equal(200, DashboardFilters.FilterRelics(relics, null).Count);
    }

    private static CollectionGoal Collection(string name, bool owned, bool mastered, int parts,
        double completion, string category = "Warframes", bool prime = false) =>
        new(name, category, owned, mastered, parts, 2, completion, "status", prime, false, "", null, []);

    private static SaleRecommendation Sale(string name, RecommendationAction action,
        int ducats = 0, int excess = 1, int? platinum = null, bool vaulted = false,
        string reason = "reason") =>
        new(name, $"/Items/{name}", $"{name.ToLowerInvariant()}_slug", excess, 0, 0, 0, 0,
            excess, ducats, platinum, null, action, vaulted, true, false, false, reason, "");
}
