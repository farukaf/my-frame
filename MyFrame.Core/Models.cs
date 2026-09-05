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
    IReadOnlyList<RelicSource> Relics)
{
    public string ImageUrl => string.IsNullOrWhiteSpace(ImageName) ? "" :
        $"https://cdn.warframestat.us/img/{Uri.EscapeDataString(ImageName)}";
}

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
    string Status,
    bool Prime,
    bool Vaulted,
    string ImageUrl)
{
    public string PrimeStatus => !Prime ? "" : Vaulted ? "Prime · Vaulted" : "Prime · Unvaulted";
    public string CardMetadata => $"{OwnedComponents:N0}/{RequiredComponents:N0} parts";
}

public sealed record FarmRecommendation(
    string ItemName,
    string Category,
    int MissingParts,
    int RequiredParts,
    int OwnedRelics,
    bool Vaulted,
    int? EstimatedPlatinum,
    IReadOnlyList<string> MissingComponentNames,
    string Reason,
    string ImageUrl)
{
    public string VaultStatus => Vaulted ? "Vaulted" : "Unvaulted";
    public string CardMetadata => $"{MissingParts:N0} missing · {OwnedRelics:N0} useful relics";
    public string CardAction => EstimatedPlatinum is null ? "Farm missing components" : $"Farm or buy for ~{EstimatedPlatinum:N0}p";
}

public sealed record RelicRecommendation(
    string RelicName,
    string UniqueName,
    int Owned,
    bool Vaulted,
    int? SellPriceEach,
    double ExpectedOpenValueEach,
    string Action,
    string Reason,
    string ImageUrl)
{
    public int? TotalSellPrice => SellPriceEach * Owned;
    public double TotalExpectedOpenValue => ExpectedOpenValueEach * Owned;
    public string VaultStatus => Vaulted ? "Vaulted" : "Unvaulted";
    public string CardMetadata => $"Owned {Owned:N0} · sealed {(SellPriceEach is null ? "—" : $"{SellPriceEach:N0}p")} · open EV {ExpectedOpenValueEach:0.0}p";
}

public sealed record SaleRecommendation(
    string ItemName,
    string UniqueName,
    string? MarketSlug,
    int Owned,
    int Reserved,
    int ReservedForCraft,
    int ReservedForFutureSale,
    int ReservedForOrders,
    int Excess,
    int DucatsEach,
    int? LowestSell,
    int? HighestBuy,
    RecommendationAction Action,
    bool Vaulted,
    bool CurrentlyOwned,
    bool Mastered,
    bool ExistingOrder,
    string Reason,
    string ImageUrl)
{
    public int TotalDucats => Excess * DucatsEach;
    public int? TotalPlatinum => LowestSell is null ? null : LowestSell * Excess;
    public int KeepQuantity => Action == RecommendationAction.Keep ? Reserved + Excess : Reserved;
    public string VaultStatus => Vaulted ? "Vaulted" : "Unvaulted";
    public string CardMetadata => $"Owned {Owned:N0} · extra {Excess:N0} · {(TotalPlatinum is null ? "price unavailable" : $"~{TotalPlatinum:N0}p")}";
    public string ItemDetails
    {
        get
        {
            var ownership = CurrentlyOwned ? "Built item: currently in inventory" : Mastered
                ? "Built item: mastered previously, no longer in inventory"
                : "Built item: not found and not mastered";
            var reservations = new List<string>();
            if (ReservedForCraft > 0) reservations.Add($"{ReservedForCraft:N0} reserved for crafting");
            if (ReservedForFutureSale > 0) reservations.Add($"{ReservedForFutureSale:N0} reserved until vault");
            if (ReservedForOrders > 0) reservations.Add($"{ReservedForOrders:N0} reserved for active orders");
            var reservationText = reservations.Count == 0 ? "No copies reserved" : string.Join(" · ", reservations);
            return $"{ownership} · Mastery: {(Mastered ? "completed" : "pending")} · Vault: {(Vaulted ? "vaulted" : "unvaulted")} · {reservationText}";
        }
    }
    public string KeepLabel
    {
        get
        {
            var reasons = new List<string>(3);
            if (ReservedForCraft > 0) reasons.Add($"keep {ReservedForCraft:N0} to craft");
            if (ReservedForFutureSale > 0) reasons.Add($"keep {ReservedForFutureSale:N0} to sell after vault");
            if (ReservedForOrders > 0) reasons.Add($"keep {ReservedForOrders:N0} for active orders");
            return reasons.Count > 0 ? string.Join(" · ", reasons) : $"keep {KeepQuantity:N0}";
        }
    }
    public string ActionLabel => Action switch
    {
        RecommendationAction.ExchangeForDucats =>
            $"Exchange {Excess:N0} · {KeepLabel} · {TotalDucats:N0} ducats",
        RecommendationAction.SellForPlatinum =>
            $"Sell {Excess:N0} · {KeepLabel} · {(TotalPlatinum is null ? "price unavailable" : $"~{TotalPlatinum:N0}p")}",
        RecommendationAction.Keep => $"{KeepLabel} · do not sell or exchange now",
        _ => Action.ToString()
    };
}

public sealed record RecommendationResult(
    IReadOnlyList<CollectionGoal> Collection,
    IReadOnlyList<FarmRecommendation> Farm,
    IReadOnlyList<SaleRecommendation> Sales,
    IReadOnlyList<RelicRecommendation> Relics,
    int TotalDucats,
    int EstimatedPlatinum,
    DateTimeOffset GeneratedAt,
    RecommendationSettings Settings);

public sealed record RecommendationSettings(
    int DucatsPerPlatinum = 10,
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
