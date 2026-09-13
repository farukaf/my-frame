using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MyFrame.Mcp;

[McpServerToolType]
public sealed class MyFrameTools(MyFrameQueryService queries, QueryExecutionGate execution, PlatformStatusService platform)
{
    [McpServerTool(Name = "get_capabilities", Title = "Get platform capabilities", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns available, partial and externally pending data capabilities. Check this before asking for rich inventory, activities or references.")]
    public CapabilitiesResponse GetCapabilities() => platform.GetCapabilities();

    [McpServerTool(Name = "get_sync_status", Title = "Get synchronization status", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns read-only status for local sources and their last error. It never starts network synchronization or changes the database.")]
    public Task<SyncStatusResponse> GetSyncStatus(CancellationToken cancellationToken = default) => platform.GetSyncStatusAsync(cancellationToken);

    [McpServerTool(Name = "get_capture_inbox_status", Title = "Get capture inbox status", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns read-only metadata about local Overwolf capture markers. It never reads payload contents, imports files, starts synchronization, or exposes the inbox path.")]
    public Task<CaptureInboxStatusResponse> GetCaptureInboxStatus(CancellationToken cancellationToken = default) =>
        platform.GetCaptureInboxStatusAsync(cancellationToken);

    [McpServerTool(Name = "get_sync_history", Title = "Get synchronization history", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns recent sanitized synchronization attempts from SQLite. It never reads payloads or starts synchronization.")]
    public Task<IReadOnlyList<SyncRunDto>> GetSyncHistory(
        [Description("Number of attempts from 1 to 100; default 20.")] int limit = 20,
        [Description("Optional exact source id, such as overwolf-inventory or public-export.")] string? sourceId = null,
        CancellationToken cancellationToken = default) => platform.GetSyncHistoryAsync(limit, sourceId, cancellationToken);

    [McpServerTool(Name = "get_inventory_coverage", Title = "Get inventory coverage", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns field-level inventory coverage states from the synchronized SQLite projection. It never returns raw payloads or treats unobserved fields as absent.")]
    public Task<IReadOnlyList<InventoryCoverageDto>> GetInventoryCoverage(CancellationToken cancellationToken = default) =>
        platform.GetInventoryCoverageAsync(cancellationToken);

    [McpServerTool(Name = "get_equipment", Title = "Get equipment instances", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns observed equipment instances from the synchronized SQLite projection, including opaque instance identity, observed rank/configuration and explicit coverage states. Raw capture payloads are never returned.")]
    public Task<IReadOnlyList<InventoryEquipmentDto>> GetEquipment(
        [Description("Optional exact typeId/uniqueName filter.")] string? typeId = null,
        [Description("Maximum number of instances from 1 to 200; default 100.")] int limit = 100,
        CancellationToken cancellationToken = default) => platform.GetInventoryEquipmentAsync(typeId, limit, cancellationToken);

    [McpServerTool(Name = "get_mods", Title = "Get observed mods and upgrades", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns observed mod/upgrade metadata attributed to an equipment instance when the capture supplied that relation. Missing attribution remains null; no build capacity or polarity is inferred.")]
    public Task<IReadOnlyList<InventoryUpgradeDto>> GetMods(
        [Description("Optional exact equipment instanceId filter.")] string? ownerInstanceId = null,
        [Description("Optional source array filter, such as mods, upgrades, or RawUpgrades.")] string? sourceField = null,
        [Description("Maximum number of entries from 1 to 200; default 100.")] int limit = 100,
        CancellationToken cancellationToken = default) => platform.GetInventoryUpgradesAsync(
        ownerInstanceId, sourceField, limit, cancellationToken);

    [McpServerTool(Name = "get_loadout", Title = "Get equipment loadouts", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns each observed equipment instance together with its observed rank/configuration and explicitly attributed mods/upgrades. Missing relations remain empty or unknown; no build compatibility is inferred.")]
    public Task<IReadOnlyList<LoadoutDto>> GetLoadout(
        [Description("Optional exact typeId/uniqueName filter.")] string? typeId = null,
        [Description("Maximum number of loadouts from 1 to 200; default 100.")] int limit = 100,
        CancellationToken cancellationToken = default) => platform.GetLoadoutsAsync(typeId, limit, cancellationToken);

    [McpServerTool(Name = "get_bounties", Title = "Get active bounties", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns currently active World State bounties with jobs, standing stages and attributed rewards. It never invents missing rewards or treats an unavailable World State as an empty game state.")]
    public Task<WorldStateBountiesResponse> GetBounties(
        [Description("Maximum number of active bounties from 1 to 200; default 100.")] int limit = 100,
        [Description("Optional case-insensitive exact syndicate filter, such as Entrati or Ostrons.")] string? syndicate = null,
        CancellationToken cancellationToken = default) => platform.GetBountiesAsync(limit, syndicate, cancellationToken);

    [McpServerTool(Name = "get_world_state", Title = "Get current World State", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns the current World State source status, active bounties with rewards, and supported planetary cycles. It is read-only and never fetches the network.")]
    public Task<WorldStateResponse> GetWorldState(
        [Description("Maximum number of active bounties from 1 to 200; default 100.")] int limit = 100,
        [Description("Optional case-insensitive exact syndicate filter, such as Entrati or Ostrons.")] string? syndicate = null,
        CancellationToken cancellationToken = default) => platform.GetWorldStateAsync(limit, syndicate, cancellationToken);

    [McpServerTool(Name = "get_activity", Title = "Get current activities", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns current bounty activities and planetary cycles using the same World State revision, coverage and validity rules as get_world_state.")]
    public Task<WorldStateResponse> GetActivity(
        [Description("Maximum number of active activities from 1 to 200; default 100.")] int limit = 100,
        [Description("Optional case-insensitive exact syndicate filter, such as Entrati or Ostrons.")] string? syndicate = null,
        CancellationToken cancellationToken = default) => platform.GetActivityAsync(limit, syndicate, cancellationToken);

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
