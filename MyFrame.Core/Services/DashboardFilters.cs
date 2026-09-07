namespace MyFrame.Core;

/// <summary>
/// Pure presentation filtering used by the dashboard. Keeping these transformations independent
/// of MAUI makes their current behavior executable in tests while the page is being decomposed.
/// </summary>
public static class DashboardFilters
{
    public static IReadOnlyList<CollectionGoal> FilterCollection(
        IEnumerable<CollectionGoal> source, string filter, string sort, string? query)
    {
        IEnumerable<CollectionGoal> values = filter switch
        {
            "In progress" => source.Where(x => !x.Owned && !x.Mastered && x.OwnedComponents > 0),
            "Not owned" => source.Where(x => !x.Owned),
            "Owned" => source.Where(x => x.Owned),
            "Mastered" => source.Where(x => x.Mastered),
            "Prime only" => source.Where(x => x.Prime),
            _ => source
        };
        values = sort switch
        {
            "Name" => values.OrderBy(x => x.ItemName),
            "Category" => values.OrderBy(x => x.Category).ThenBy(x => x.ItemName),
            "Least progress" => values.OrderBy(x => x.Completion).ThenBy(x => x.ItemName),
            _ => values.OrderByDescending(x => x.Completion).ThenBy(x => x.ItemName)
        };
        if (!string.IsNullOrWhiteSpace(query))
            values = values.Where(x => Matches(query, x.ItemName, x.Category, x.Status, x.PrimeStatus));
        return values.ToArray();
    }

    public static IReadOnlyList<FarmRecommendation> FilterFarm(
        IEnumerable<FarmRecommendation> source, string? query) =>
        (string.IsNullOrWhiteSpace(query) ? source : source.Where(x =>
            Matches(query, x.ItemName, x.Category, x.Reason, string.Join(' ', x.MissingComponentNames))))
        .Take(100).ToArray();

    public static IReadOnlyList<RelicRecommendation> FilterRelics(
        IEnumerable<RelicRecommendation> source, string? query) =>
        (string.IsNullOrWhiteSpace(query) ? source : source.Where(x =>
            Matches(query, x.RelicName, x.Reason, x.Action, x.VaultStatus)))
        .Take(200).ToArray();

    public static SalesView FilterSales(
        IEnumerable<SaleRecommendation> source, string filter, string sort, string? query,
        bool includeVaultedParts)
    {
        IEnumerable<SaleRecommendation> values = filter switch
        {
            "Keep" => source.Where(x => x.Action == RecommendationAction.Keep),
            "Platinum" => source.Where(x => x.Action == RecommendationAction.SellForPlatinum),
            "Ducats" => source.Where(x => x.Action == RecommendationAction.ExchangeForDucats),
            "Existing orders" => source.Where(x => x.ExistingOrder),
            "Vaulted items" => source.Where(x => x.Vaulted),
            _ => source
        };
        if (!includeVaultedParts) values = values.Where(x => !x.Vaulted);
        if (!string.IsNullOrWhiteSpace(query))
            values = values.Where(x => Matches(query, x.ItemName, x.Reason, x.ActionLabel, x.VaultStatus));
        values = sort switch
        {
            "Action" => values.OrderBy(x => x.ActionLabel).ThenBy(x => x.ItemName),
            "Highest value" => values.OrderByDescending(x => x.TotalPlatinum).ThenBy(x => x.ItemName),
            _ => values.OrderBy(x => x.ItemName)
        };
        var listed = values.ToArray();
        var ducats = listed.Where(x => x.Action == RecommendationAction.ExchangeForDucats)
            .Sum(x => (long)x.TotalDucats);
        return new SalesView(listed.Take(200).ToArray(), $"{ducats:N0}");
    }

    private static bool Matches(string? query, params string?[] values) => values.Any(value =>
        value?.Contains(query?.Trim() ?? "", StringComparison.OrdinalIgnoreCase) == true);
}

public sealed record SalesView(IReadOnlyList<SaleRecommendation> Items, string DucatsEstimate);
