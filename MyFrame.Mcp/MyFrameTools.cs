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

    [McpServerTool(Name = "get_market_credential_status", Title = "Get market credential status", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns only the state and expiry of the independent Warframe Market credential. Never returns the token or starts authentication.")]
    public Task<MarketCredentialStatusResponse> GetMarketCredentialStatus(CancellationToken cancellationToken = default) =>
        platform.GetMarketCredentialStatusAsync(cancellationToken);

    [McpServerTool(Name = "get_sync_status", Title = "Get synchronization status", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns read-only status for local sources and their last error. It never starts network synchronization or changes the database.")]
    public Task<SyncStatusResponse> GetSyncStatus(CancellationToken cancellationToken = default) => platform.GetSyncStatusAsync(cancellationToken);

    [McpServerTool(Name = "get_capture_inbox_status", Title = "Get capture inbox status", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns read-only metadata about local Overwolf capture markers, heartbeat freshness and sanitized collector callback diagnostics. It never reads payload contents, imports files, starts synchronization, or exposes the inbox path.")]
    public Task<CaptureInboxStatusResponse> GetCaptureInboxStatus(CancellationToken cancellationToken = default) =>
        platform.GetCaptureInboxStatusAsync(cancellationToken);

    [McpServerTool(Name = "get_sync_history", Title = "Get synchronization history", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns recent sanitized synchronization attempts from SQLite. It never reads payloads or starts synchronization.")]
    public Task<SyncHistoryResponse> GetSyncHistory(
        [Description("Number of attempts from 1 to 100; default 20.")] int limit = 20,
        [Description("Optional exact source id, such as overwolf-inventory or public-export.")] string? sourceId = null,
        CancellationToken cancellationToken = default) => platform.GetSyncHistoryAsync(limit, sourceId, cancellationToken);

    [McpServerTool(Name = "get_inventory_coverage", Title = "Get inventory coverage", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns field-level inventory coverage states from the synchronized SQLite projection. It never returns raw payloads or treats unobserved fields as absent.")]
    public Task<InventoryCoverageResponse> GetInventoryCoverage(CancellationToken cancellationToken = default) =>
        platform.GetInventoryCoverageAsync(cancellationToken);

    [McpServerTool(Name = "get_inventory_history", Title = "Get inventory revision history", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns sanitized inventory revision metadata from SQLite, including sequence, completeness and snapshot/delta mode. It never returns raw payloads or claims that a delta is a complete inventory.")]
    public Task<InventoryHistoryResponse> GetInventoryHistory(
        [Description("Maximum revisions from 1 to 100; default 20.")] int limit = 20,
        CancellationToken cancellationToken = default) => platform.GetInventoryHistoryAsync(limit, cancellationToken);

    [McpServerTool(Name = "get_inventory_changes", Title = "Compare inventory revisions", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compares two complete inventory snapshot revisions without exposing raw payloads. Delta captures return partial and produce no inferred additions/removals.")]
    public Task<InventoryChangesResponse> GetInventoryChanges(
        [Description("Optional previous revision id from get_inventory_history.")] string? fromRevisionId = null,
        [Description("Optional target revision id from get_inventory_history.")] string? toRevisionId = null,
        [Description("Maximum changes from 1 to 500; default 200.")] int limit = 200,
        CancellationToken cancellationToken = default) => platform.GetInventoryChangesAsync(fromRevisionId, toRevisionId, limit, cancellationToken);

    [McpServerTool(Name = "get_source_coverage", Title = "Get source field coverage", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns source state, active revision/parser and field-level coverage for a synchronized source such as public-export, worldstate-pc, or overwolf-inventory. Public Export coverage includes components, relics, marketIdentity, imageName, productCategory, localizedNames and technicalMetadata. It never returns raw payloads and preserves NotObserved instead of guessing.")]
    public Task<SourceCoverageResponse> GetSourceCoverage(
        [Description("Coverage-enabled source id: public-export, worldstate-pc, or overwolf-inventory.")] string sourceId,
        CancellationToken cancellationToken = default) => platform.GetSourceCoverageAsync(sourceId, cancellationToken);

    [McpServerTool(Name = "search_public_export", Title = "Search Warframe Public Export", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Searches the synchronized official Warframe Public Export catalog by uniqueName, name, alias, or category. Results include normalized technical metadata, market identity, recipe components and relic sources when observed. It never fetches the network or returns raw JSON.")]
    public Task<PublicExportSearchResponse> SearchPublicExport(
        [Description("Optional case-insensitive text filter for uniqueName, name, or alias; maximum 200 characters.")] string? text = null,
        [Description("Optional exact case-insensitive category filter; maximum 100 characters.")] string? category = null,
        [Description("Maximum results from 1 to 200; default 50.")] int limit = 50,
        CancellationToken cancellationToken = default) => platform.SearchPublicExportAsync(text, category, limit, cancellationToken);

    [McpServerTool(Name = "get_public_export_item", Title = "Get Public Export item", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns one normalized official Public Export item by uniqueName, name, or localized alias without requiring an inventory snapshot. Includes observed technical metadata, recipe components, relic sources and market identity; never fetches the network or returns raw JSON.")]
    public Task<PublicExportItemResponse> GetPublicExportItem(
        [Description("UniqueName, name, or localized alias; maximum 512 characters.")] string itemId,
        CancellationToken cancellationToken = default) => platform.GetPublicExportItemAsync(itemId, cancellationToken);

    [McpServerTool(Name = "search_references", Title = "Search imported Wiki and Overframe references", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Searches explicitly imported Wiki/Overframe JSON references under the local data root. Results retain URL, revision, author/license and are untrusted for game facts; no network access occurs.")]
    public Task<ReferenceSearchResponse> SearchReferences(
        [Description("Text query from 1 to 200 characters.")] string query,
        [Description("Maximum hits from 1 to 100; default 20.")] int limit = 20,
        CancellationToken cancellationToken = default) => platform.SearchReferencesAsync(query, limit, cancellationToken);

    [McpServerTool(Name = "get_reference_section", Title = "Get imported reference section", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns one bounded section from an explicitly imported Wiki/Overframe reference, preserving URL, revision, author/license and the untrusted-for-facts flag. It never fetches the network or reads arbitrary paths.")]
    public Task<ReferenceSectionResponse> GetReferenceSection(
        [Description("Exact source URL returned by search_references; maximum 2048 characters.")] string url,
        [Description("Exact section id returned by search_references; maximum 200 characters.")] string sectionId,
        [Description("Optional exact revision to disambiguate the source; maximum 200 characters.")] string? revision = null,
        CancellationToken cancellationToken = default) => platform.GetReferenceSectionAsync(url, sectionId, revision, cancellationToken);

    [McpServerTool(Name = "get_equipment", Title = "Get equipment instances", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns observed equipment instances from the synchronized SQLite projection, including opaque instance identity, observed rank/configuration and explicit coverage states. Raw capture payloads are never returned.")]
    public Task<InventoryEquipmentResponse> GetEquipment(
        [Description("Optional exact typeId/uniqueName filter.")] string? typeId = null,
        [Description("Maximum number of instances from 1 to 200; default 100.")] int limit = 100,
        CancellationToken cancellationToken = default) => platform.GetInventoryEquipmentAsync(typeId, limit, cancellationToken);

    [McpServerTool(Name = "get_mods", Title = "Get observed mods and upgrades", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns observed mod/upgrade metadata attributed to an equipment instance when the capture supplied that relation. Missing attribution remains null; no build capacity or polarity is inferred.")]
    public Task<InventoryUpgradesResponse> GetMods(
        [Description("Optional exact equipment instanceId filter.")] string? ownerInstanceId = null,
        [Description("Optional source array filter, such as mods, upgrades, or RawUpgrades.")] string? sourceField = null,
        [Description("Maximum number of entries from 1 to 200; default 100.")] int limit = 100,
        CancellationToken cancellationToken = default) => platform.GetInventoryUpgradesAsync(
        ownerInstanceId, sourceField, limit, cancellationToken);

    [McpServerTool(Name = "get_loadout", Title = "Get equipment loadouts", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns each observed equipment instance together with its observed rank/configuration and explicitly attributed mods/upgrades. Missing relations remain empty or unknown; no build compatibility is inferred.")]
    public Task<LoadoutResponse> GetLoadout(
        [Description("Optional exact typeId/uniqueName filter.")] string? typeId = null,
        [Description("Maximum number of loadouts from 1 to 200; default 100.")] int limit = 100,
        CancellationToken cancellationToken = default) => platform.GetLoadoutsAsync(typeId, limit, cancellationToken);

    [McpServerTool(Name = "get_acquisition", Title = "Get item acquisition sources", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Combines normalized catalog components and relic sources with currently active World State bounties for one stable itemId. It includes catalog/World State revisions and parser provenance, is read-only, and does not invent sources when a dataset is unavailable.")]
    public Task<AcquisitionResponse> GetAcquisition(
        [Description("Exact catalog itemId/uniqueName returned by another My Frame tool.")] string itemId,
        [Description("Maximum entries per acquisition source from 1 to 200; default 100.")] int limit = 100,
        CancellationToken cancellationToken = default) => platform.GetAcquisitionAsync(itemId, limit, cancellationToken);

    [McpServerTool(Name = "get_bounties", Title = "Get active bounties", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns currently active World State bounties with jobs, standing stages, attributed rewards and source revision/parser provenance. It never invents missing rewards or treats an unavailable World State as an empty game state.")]
    public Task<WorldStateBountiesResponse> GetBounties(
        [Description("Maximum number of active bounties from 1 to 200; default 100.")] int limit = 100,
        [Description("Optional case-insensitive exact syndicate filter, such as Entrati or Ostrons.")] string? syndicate = null,
        [Description("Optional case-insensitive text filter matched against reward item names, such as Mother Token.")] string? reward = null,
        CancellationToken cancellationToken = default) => platform.GetBountiesAsync(limit, syndicate, reward, cancellationToken);

    [McpServerTool(Name = "get_world_state", Title = "Get current World State", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns the current World State source status, active bounties with rewards, supported planetary cycles and source revision/parser provenance. It is read-only and never fetches the network.")]
    public Task<WorldStateResponse> GetWorldState(
        [Description("Maximum number of active bounties from 1 to 200; default 100.")] int limit = 100,
        [Description("Optional case-insensitive exact syndicate filter, such as Entrati or Ostrons.")] string? syndicate = null,
        [Description("Optional case-insensitive text filter matched against reward item names, such as Mother Token.")] string? reward = null,
        CancellationToken cancellationToken = default) => platform.GetWorldStateAsync(limit, syndicate, reward, cancellationToken);

    [McpServerTool(Name = "get_activity", Title = "Get current activities", UseStructuredContent = true,
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns current bounty activities and planetary cycles using the same World State revision, coverage and validity rules as get_world_state. An optional reward filter narrows bounties without inventing missing rewards.")]
    public Task<WorldStateResponse> GetActivity(
        [Description("Maximum number of active activities from 1 to 200; default 100.")] int limit = 100,
        [Description("Optional case-insensitive exact syndicate filter, such as Entrati or Ostrons.")] string? syndicate = null,
        [Description("Optional case-insensitive text filter matched against reward item names, such as Mother Token.")] string? reward = null,
        CancellationToken cancellationToken = default) => platform.GetActivityAsync(limit, syndicate, reward, cancellationToken);

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
    [Description("Returns possession, catalog description, components, mastery, prices, relic sources, and structured recommendation evidence for one stable itemId. Description is source text and does not replace observed mechanics.")]
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
