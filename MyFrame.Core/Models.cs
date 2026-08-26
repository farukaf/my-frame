namespace MyFrame.Core;

public sealed record InventorySnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyDictionary<string, int> Stackables,
    IReadOnlySet<string> OwnedEquipment,
    IReadOnlyDictionary<string, long> Experience,
    int PlayerLevel,
    int TradesRemaining,
    string SourcePath);

public sealed record CatalogComponent(
    string UniqueName,
    string Name,
    int Required,
    int Ducats,
    bool Tradable);

public sealed record RelicSource(
    string RelicName,
    string Rarity,
    double Chance,
    bool Vaulted,
    string RewardName);

public sealed record CatalogItem(
    string UniqueName,
    string Name,
    string Category,
    string ProductCategory,
    string ImageName,
    bool Masterable,
    bool Prime,
    bool Tradable,
    bool Vaulted,
    string? EstimatedVaultDate,
    string? MarketId,
    string? MarketSlug,
    IReadOnlyList<CatalogComponent> Components,
    IReadOnlyList<RelicSource> Relics);

public sealed record CatalogSnapshot(
    IReadOnlyList<CatalogItem> Items,
    IReadOnlyDictionary<string, CatalogItem> ByUniqueName,
    IReadOnlyDictionary<string, MarketIdentity> MarketByNormalizedName);

public sealed record MarketIdentity(string Id, string Slug);

public sealed record MarketQuote(
    string Slug,
    int? LowestSell,
    int? HighestBuy,
    DateTimeOffset RetrievedAt,
    bool IsStale = false);

public sealed record MarketOrder(
    string Id,
    string ItemId,
    string? ItemSlug,
    string Type,
    int Platinum,
    int Quantity,
    bool Visible);

public sealed record MarketAccount(string Id, string IngameName, string Platform);

public enum RecommendationAction
{
    Keep,
    Farm,
    SellForPlatinum,
    ExchangeForDucats
}

public sealed record CollectionGoal(
    string ItemName,
    string Category,
    bool Owned,
    bool Mastered,
    int OwnedComponents,
    int RequiredComponents,
    double Completion,
    string Status);

public sealed record FarmRecommendation(
    string ItemName,
    string Category,
    int MissingParts,
    int RequiredParts,
    int OwnedRelics,
    bool Vaulted,
    int? EstimatedPlatinum,
    IReadOnlyList<string> MissingComponentNames,
    string Reason);

public sealed record SaleRecommendation(
    string ItemName,
    string UniqueName,
    string? MarketSlug,
    int Owned,
    int Reserved,
    int Excess,
    int DucatsEach,
    int? LowestSell,
    int? HighestBuy,
    RecommendationAction Action,
    bool Vaulted,
    bool ExistingOrder,
    string Reason)
{
    public int TotalDucats => Excess * DucatsEach;
    public int? TotalPlatinum => LowestSell is null ? null : LowestSell * Excess;
}

public sealed record RecommendationResult(
    IReadOnlyList<CollectionGoal> Collection,
    IReadOnlyList<FarmRecommendation> Farm,
    IReadOnlyList<SaleRecommendation> Sales,
    int TotalDucats,
    int EstimatedPlatinum,
    DateTimeOffset GeneratedAt);

public sealed record RecommendationSettings(
    double DucatsPerPlatinum = 10,
    bool ReserveUnvaultedPrimeWarframeSet = true);

public sealed record SyncStatus(
    bool IsLoading,
    string Message,
    DateTimeOffset? LastSuccessfulSync,
    bool HasInventory,
    bool HasMarket,
    string? Error = null);

public sealed record DashboardSnapshot(
    InventorySnapshot Inventory,
    CatalogSnapshot Catalog,
    RecommendationResult Recommendations,
    MarketAccount? Account,
    IReadOnlyList<MarketOrder> Orders,
    IReadOnlyDictionary<string, MarketQuote> Quotes,
    SyncStatus Status);

public static class ItemNameNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
            }
        }

        var normalized = new string(buffer[..length]);
        return normalized.EndsWith("blueprint", StringComparison.Ordinal)
            ? normalized[..^"blueprint".Length]
            : normalized;
    }
}
