using MyFrame.Core;

namespace MyFrame.Mcp;

public sealed record SourceDto(string State, DateTimeOffset? RetrievedAt, bool Fallback, string? DetailCode);
public sealed record WarningDto(string Code, string Message);

public sealed record ResponseMeta(
    string SchemaVersion,
    string RulesVersion,
    string SnapshotId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset ServedAt,
    DateTimeOffset? InventoryCapturedAt,
    IReadOnlyDictionary<string, SourceDto> Sources,
    IReadOnlyList<WarningDto> Warnings,
    bool Complete,
    bool AvailabilityConfirmed);

public sealed record PageTotals(int ItemCount, long? Platinum = null, long? Ducats = null,
    int? Quantity = null, bool Complete = true);

public sealed record PageResponse<T>(
    ResponseMeta Meta,
    int Count,
    int TotalCount,
    PageTotals Totals,
    string? NextCursor,
    IReadOnlyList<T> Items);

public sealed record OverviewResponse(ResponseMeta Meta, OverviewDto Overview);

public sealed record OverviewDto(
    bool SetupRequired,
    int StackTypes,
    int EquipmentTypes,
    int PlayerLevel,
    int TradesRemaining,
    int CollectionTotal,
    int Mastered,
    int FarmGoals,
    int SaleRecommendations,
    int Relics,
    int SurplusParts,
    int EstimatedPlatinum,
    int TotalDucats,
    int FreshPrices,
    int StalePrices,
    string InventoryCoverage,
    SettingsDto? Settings,
    AccountDto? Account);

public sealed record SettingsDto(int DucatsPerPlatinum, int UnvaultedPrimeSetsToReserve, long Revision);
public sealed record AccountDto(string Name, string Platform, DateTimeOffset? RetrievedAt);

public sealed record InventoryItemDto(
    string ItemId,
    string EntityType,
    string Name,
    string Category,
    int? Quantity,
    bool QuantityKnown,
    bool Owned,
    bool CatalogMatched,
    bool Built,
    bool Stackable);

public sealed record ComponentDto(string ItemId, string Name, int Owned, int Required,
    int StillNeeded, int Reserved, int AllocatedToSets, int AvailableToSell, int DucatsEach, bool Tradable,
    string? MarketSlug, PriceDto? Price);

public sealed record PriceDto(int? LowestSellPlatinum, int? HighestBuyPlatinum,
    DateTimeOffset RetrievedAt, string Status, string Basis);

public sealed record ItemResponse(ResponseMeta Meta, ItemDto? Item, ProblemDto? Problem,
    string Section, int Count, int TotalCount, string? NextCursor);
public sealed record ProblemDto(string Code, string Message, bool Retryable);
public sealed record ItemDto(
    string ItemId,
    string EntityType,
    string Name,
    string Category,
    bool CatalogMatched,
    bool Owned,
    int? Quantity,
    bool QuantityKnown,
    bool Mastered,
    bool Prime,
    bool Vaulted,
    string? MarketSlug,
    PriceDto? Price,
    IReadOnlyList<ComponentDto> Components,
    IReadOnlyList<string> Relics,
    IReadOnlyList<EvidenceDto> Recommendations,
    string? Description = null);

public sealed record EvidenceDto(string ReasonCode, string Explanation,
    IReadOnlyDictionary<string, string> Evidence);

public sealed record CollectionDto(string ItemId, string Name, string Category, string Status,
    bool Owned, bool Mastered, bool Prime, bool Vaulted, double Completion,
    int OwnedComponents, int RequiredComponents);

public sealed record FarmDto(string ItemId, string Name, string Category, bool Vaulted,
    int MissingComponentTypes, int MissingUnits, IReadOnlyList<string> MissingComponents,
    int OwnedUsefulRelicTypes, int? MissingPartsCostPlatinum, int? SetPurchasePricePlatinum,
    int PriceCountKnown, int PriceCountRequired, string ReasonCode, string Explanation);

public sealed record SaleDto(string ItemId, string Name, string Action, int Owned, int Reserved,
    int ReservedForCraft, int ReservedForFutureSale, int ReservedForOrders, int AvailableToSell,
    int? PlatinumEach, int? TotalPlatinum, int DucatsEach, int TotalDucats, bool ExistingOrder,
    bool Vaulted, string ReasonCode, string Explanation,
    IReadOnlyDictionary<string, int>? AllocatedComponents);

public sealed record RelicDto(string ItemId, string Name, int Owned, bool Vaulted, string Action,
    int? SealedPricePlatinum, double KnownExpectedOpenValuePlatinum,
    double? CompleteExpectedOpenValuePlatinum, int RewardPricesKnown, int RewardPricesRequired,
    string Refinement, string OpeningAssumption, string ReasonCode, string Explanation);

public sealed record SurplusDto(string ItemId, string Name, string ParentName, string Category,
    int Owned, int StillNeededForCollection, int SurplusForCollection, int Reserved,
    int AllocatedToSets, int AvailableToSell, int? PlatinumEach, int? TotalPlatinum, int DucatsEach, int TotalDucats,
    bool Tradable, string Reason, string ReasonCode, string Explanation);
