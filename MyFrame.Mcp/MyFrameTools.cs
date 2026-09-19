using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MyFrame.Mcp;

[McpServerToolType]
public sealed class MyFrameTools(MyFrameQueryService queries, QueryExecutionGate execution)
{
    [McpServerTool(Name = "get_overview", Title = "Get My Frame overview", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Start here. Returns inventory totals, source health, market coverage, active settings and an optional account name. Reuse its snapshotId for one consistent analysis.")]
    public Task<OverviewResponse> GetOverview(
        [Description("Include the Warframe.Market display name and platform. The private account ID is never returned.")] bool includeAccount = false,
        [Description("Optional snapshotId returned by an earlier call.")] string? snapshotId = null,
        CancellationToken cancellationToken = default) => Execute(token =>
            queries.GetOverviewAsync(includeAccount, snapshotId, token), cancellationToken);

    [McpServerTool(Name = "search_inventory", Title = "Search inventory", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Searches the supported aggregated inventory. Equipment presence is known but its quantity is null.")]
    public Task<PageResponse<InventoryItemDto>> SearchInventory(
        [Description("Case-insensitive text matched against name, category, and itemId; maximum 200 characters.")] string? text = null,
        [Description("Optional entity type: equipment, component, relic, item, or stackable.")] string? entityType = null,
        [Description("Optional exact category, case-insensitive.")] string? category = null,
        [Description("Optional minimum known quantity. Unknown equipment quantities are excluded unless includeUnknownQuantity is true.")] int? minimumQuantity = null,
        [Description("State filter: all, built, or stackable.")] string state = "all",
        [Description("Include presence-only equipment when a quantity filter is used.")] bool includeUnknownQuantity = false,
        [Description("Page size from 1 to 200; default 50.")] int limit = 50,
        [Description("Opaque cursor returned by the previous page.")] string? cursor = null,
        [Description("Optional snapshotId. A cursor already carries one.")] string? snapshotId = null,
        CancellationToken cancellationToken = default) => Execute(token => queries.SearchInventoryAsync(
            ValidateText(text), entityType, category, minimumQuantity, state, includeUnknownQuantity,
            limit, cursor, snapshotId, token), cancellationToken);

    [McpServerTool(Name = "get_item", Title = "Get item details", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns possession, components, mastery, prices, relic sources, and structured recommendation evidence for one stable itemId.")]
    public Task<ItemResponse> GetItem(
        [Description("Exact itemId returned by another My Frame tool.")] string itemId,
        [Description("Nested detail section: all, summary, components, relics, or recommendations.")] string section = "all",
        [Description("Section page size from 1 to 200; applies to components, relics, or recommendations.")] int limit = 50,
        [Description("Opaque cursor returned by a previous get_item section page.")] string? cursor = null,
        [Description("Optional snapshotId returned by an earlier call.")] string? snapshotId = null,
        CancellationToken cancellationToken = default) => Execute(token =>
            queries.GetItemAsync(itemId, section, limit, cursor, snapshotId, token), cancellationToken);

    [McpServerTool(Name = "list_collection", Title = "List collection", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists collection and mastery goals with deterministic snapshot-bound pagination.")]
    public Task<PageResponse<CollectionDto>> ListCollection(
        [Description("State: all, inProgress, notOwned, owned, or mastered.")] string state = "all",
        [Description("Optional Prime filter.")] bool? prime = null,
        [Description("Optional vaulted filter.")] bool? vaulted = null,
        [Description("Optional exact category.")] string? category = null,
        [Description("Optional case-insensitive text search.")] string? text = null,
        [Description("Page size from 1 to 200.")] int limit = 50,
        [Description("Opaque cursor returned by the previous page.")] string? cursor = null,
        [Description("Optional snapshotId returned by an earlier call.")] string? snapshotId = null,
        CancellationToken cancellationToken = default) => Execute(token => queries.ListCollectionAsync(
            state, prime, vaulted, category, ValidateText(text), limit, cursor, snapshotId, token), cancellationToken);

    [McpServerTool(Name = "list_farm", Title = "List farm goals", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists farm goals based on actually missing component units and useful owned relics. Partial price coverage never becomes a definitive cost.")]
    public Task<PageResponse<FarmDto>> ListFarm(
        [Description("Optional case-insensitive text search.")] string? text = null,
        [Description("Optional vaulted filter.")] bool? vaulted = null,
        [Description("Optional maximum number of missing component units.")] int? maximumMissingUnits = null,
        [Description("Optional exact collection itemId.")] string? targetItemId = null,
        [Description("Page size from 1 to 200.")] int limit = 50,
        [Description("Opaque cursor returned by the previous page.")] string? cursor = null,
        [Description("Optional snapshotId returned by an earlier call.")] string? snapshotId = null,
        CancellationToken cancellationToken = default) => Execute(token => queries.ListFarmAsync(
            ValidateText(text), vaulted, maximumMissingUnits, targetItemId, limit, cursor, snapshotId, token), cancellationToken);

    [McpServerTool(Name = "list_sales", Title = "List sale decisions", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists keep, platinum, and ducat decisions with reservation evidence. availableToSell can be provisional when orders are unconfirmed.")]
    public Task<PageResponse<SaleDto>> ListSales(
        [Description("Action: all, keep, platinum, or ducats.")] string action = "all",
        [Description("Optional vaulted filter.")] bool? vaulted = null,
        [Description("Optional existing-order filter.")] bool? existingOrder = null,
        [Description("Optional case-insensitive text search.")] string? text = null,
        [Description("Page size from 1 to 200.")] int limit = 50,
        [Description("Opaque cursor returned by the previous page.")] string? cursor = null,
        [Description("Optional snapshotId returned by an earlier call.")] string? snapshotId = null,
        CancellationToken cancellationToken = default) => Execute(token => queries.ListSalesAsync(
            action, vaulted, existingOrder, ValidateText(text), limit, cursor, snapshotId, token), cancellationToken);

    [McpServerTool(Name = "list_relics", Title = "List relic decisions", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists intact solo-opening relic estimates. Missing or stale reward prices produce hold/insufficient-data, never zero-valued rewards.")]
    public Task<PageResponse<RelicDto>> ListRelics(
        [Description("Action: all, open, sellSealed, or hold.")] string action = "all",
        [Description("Optional vaulted filter.")] bool? vaulted = null,
        [Description("Optional minimum owned relic quantity.")] int? minimumOwned = null,
        [Description("Optional collection itemId; only relics containing its missing components are returned.")] string? targetItemId = null,
        [Description("Optional case-insensitive text search.")] string? text = null,
        [Description("Page size from 1 to 200.")] int limit = 50,
        [Description("Opaque cursor returned by the previous page.")] string? cursor = null,
        [Description("Optional snapshotId returned by an earlier call.")] string? snapshotId = null,
        CancellationToken cancellationToken = default) => Execute(token => queries.ListRelicsAsync(
            action, vaulted, minimumOwned, targetItemId, ValidateText(text), limit, cursor, snapshotId, token), cancellationToken);

    [McpServerTool(Name = "list_surplus", Title = "List collection surplus", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists parts no longer needed for collection. surplusForCollection and availableToSell are separate because reservations still apply.")]
    public Task<PageResponse<SurplusDto>> ListSurplus(
        [Description("Reason: all, crafted, mastered, or onlyOneNeeded.")] string reason = "all",
        [Description("Optional filter for a fresh platinum reference price.")] bool? hasPlatinumValue = null,
        [Description("Optional case-insensitive text search.")] string? text = null,
        [Description("Page size from 1 to 200.")] int limit = 50,
        [Description("Opaque cursor returned by the previous page.")] string? cursor = null,
        [Description("Optional snapshotId returned by an earlier call.")] string? snapshotId = null,
        CancellationToken cancellationToken = default) => Execute(token => queries.ListSurplusAsync(
            reason, hasPlatinumValue, ValidateText(text), limit, cursor, snapshotId, token), cancellationToken);

    private static string? ValidateText(string? value)
    {
        if (value?.Length > 200) throw new QueryProblemException("INVALID_ARGUMENT", "Search text is limited to 200 characters.");
        return value;
    }

    private async Task<T> Execute<T>(Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        try { return await execution.RunAsync(action, cancellationToken).ConfigureAwait(false); }
        catch (QueryProblemException error)
        {
            throw new McpException($"{error.Code}: {error.Message} Retryable={error.Retryable.ToString().ToLowerInvariant()}.");
        }
    }
}
