using System.Text.Json;
using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.Mcp;

public sealed record CapabilityDto(string Name, string State, string Detail);
public sealed record CapabilitiesResponse(DateTimeOffset ServedAt, IReadOnlyList<CapabilityDto> Capabilities);
public sealed record MarketCredentialStatusResponse(DateTimeOffset ServedAt, string State,
    DateTimeOffset? ExpiresAt, bool IsConfigured);
public sealed record SyncSourceStatusDto(string SourceId, string State, string? LastRunState,
    DateTimeOffset? LastRunAt, string? ErrorCode, string? ActiveRevisionId, string? ParserVersion,
    long AcceptedRecords, long RejectedRecords);
public sealed record SyncStatusResponse(DateTimeOffset ServedAt, IReadOnlyList<SyncSourceStatusDto> Sources);
public sealed record CaptureInboxStatusResponse(DateTimeOffset ServedAt, string State,
    int PendingMarkers, DateTimeOffset? NewestMarkerAt, string? LastErrorCode,
    string? ActiveCaptureMode = null, string? ActiveCompleteness = null, long? ActiveSequence = null,
    bool OverwolfRunning = false, bool WarframeRunning = false, string? HeartbeatState = null,
    bool HeartbeatFresh = false, int ValidMarkers = 0, int InvalidMarkers = 0,
    string? CollectorState = null, IReadOnlyList<string>? SupportedFeatures = null,
    IReadOnlyDictionary<string, int>? EventCounts = null, string? LastEventFeature = null,
    DateTimeOffset? LastEventAt = null);
public sealed record SyncRunDto(string RunId, string SourceId, string State,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, long RecordsReceived,
    long RecordsAccepted, long RecordsRejected, string? ErrorCode);
public sealed record SyncHistoryResponse(IReadOnlyList<SyncRunDto> Items);
public sealed record InventoryCoverageDto(string FieldPath, string State);
public sealed record InventoryCoverageResponse(IReadOnlyList<InventoryCoverageDto> Items);
public sealed record InventoryRevisionDto(string RevisionId, string ContentHash, long Sequence,
    string Completeness, string CaptureMode, DateTimeOffset RetrievedAt);
public sealed record InventoryHistoryResponse(DateTimeOffset ServedAt, string State,
    IReadOnlyList<InventoryRevisionDto> Items);
public sealed record InventoryChangeDto(string Kind, string Key, string Change,
    string? BeforeTypeId, string? AfterTypeId, int? BeforeRank, int? AfterRank,
    int? BeforeQuantity, int? AfterQuantity, string BeforeState, string AfterState);
public sealed record InventoryChangesResponse(DateTimeOffset ServedAt, string State,
    string? FromRevisionId, string? ToRevisionId, IReadOnlyList<InventoryChangeDto> Items);
public sealed record SourceCoverageDto(string SourceId, string FieldPath, string State);
public sealed record SourceCoverageResponse(IReadOnlyList<SourceCoverageDto> Items,
    DateTimeOffset? ServedAt = null, string? State = null, string? ActiveRevisionId = null,
    string? ParserVersion = null);
public sealed record PublicExportItemDto(string UniqueName, string? Name, string? Category,
    string? Description, IReadOnlyDictionary<string, string> Aliases,
    string ProductCategory, string ImageName, bool Masterable, bool Prime, bool Tradable,
    bool Vaulted, string? MarketId, string? MarketSlug, string ItemType,
    IReadOnlyList<CatalogComponent> Components, IReadOnlyList<RelicSource> Relics);
public sealed record PublicExportSearchResponse(DateTimeOffset ServedAt, string State,
    string? ActiveRevisionId, string? ParserVersion, IReadOnlyDictionary<string, string> Coverage,
    IReadOnlyList<PublicExportItemDto> Items);
public sealed record PublicExportItemResponse(DateTimeOffset ServedAt, string State,
    string? ActiveRevisionId, string? ParserVersion, IReadOnlyDictionary<string, string> Coverage,
    PublicExportItemDto? Item);
public sealed record ReferenceSearchHitDto(string Kind, string Title, string SectionId,
    string? SectionTitle, string Snippet, double Score, string Url, string Revision,
    string? License, string? Author, bool TrustedForFacts);
public sealed record ReferenceSearchResponse(DateTimeOffset ServedAt, string State,
    int Documents, int RejectedDocuments, IReadOnlyList<ReferenceSearchHitDto> Hits);
public sealed record ReferenceSectionResponse(DateTimeOffset ServedAt, string State,
    int Documents, int RejectedDocuments, string? Kind, string? Title, string? Url,
    string? Revision, string? License, string? Author, bool TrustedForFacts,
    string? SectionId, string? SectionTitle, string? Content, bool ContentTruncated);
public sealed record InventoryEquipmentDto(string InstanceId, string? TypeId, int? Rank,
    string? ConfigJson, string RankState, string ConfigState);
public sealed record InventoryEquipmentResponse(IReadOnlyList<InventoryEquipmentDto> Items);
public sealed record InventoryUpgradeDto(string? OwnerInstanceId, string SourceField,
    string? UpgradeId, int? Rank);
public sealed record InventoryUpgradesResponse(IReadOnlyList<InventoryUpgradeDto> Items);
public sealed record LoadoutDto(string InstanceId, string? TypeId, int? Rank, string? ConfigJson,
    string RankState, string ConfigState, IReadOnlyList<InventoryUpgradeDto> Upgrades);
public sealed record LoadoutResponse(IReadOnlyList<LoadoutDto> Items);
public sealed record WorldStateRewardDto(string Item, decimal? Chance, int? Count, string? Rarity);
public sealed record WorldStateJobDto(string Id, string? Type, string? UniqueName,
    int? MinimumMasteryRank, IReadOnlyList<int> StandingStages, IReadOnlyList<WorldStateRewardDto> Rewards);
public sealed record WorldStateBountyDto(string Id, string? Syndicate, DateTimeOffset? Activation,
    DateTimeOffset? Expiry, IReadOnlyList<WorldStateJobDto> Jobs);
public sealed record WorldStateBountiesResponse(DateTimeOffset ServedAt, string State,
    DateTimeOffset? LastAttemptAt, string? ErrorCode, IReadOnlyList<WorldStateBountyDto> Bounties,
    string? ActiveRevisionId = null, string? ParserVersion = null);
public sealed record WorldStateCycleDto(string Name, string? State, DateTimeOffset? Activation, DateTimeOffset? Expiry);
public sealed record WorldStateResponse(DateTimeOffset ServedAt, string State, DateTimeOffset? LastAttemptAt,
    string? ErrorCode, IReadOnlyList<WorldStateBountyDto> Bounties, IReadOnlyList<WorldStateCycleDto> Cycles,
    IReadOnlyDictionary<string, string> Coverage, string? ActiveRevisionId, string? ParserVersion = null);

public sealed class PlatformStatusService
{
    private readonly IMarketTokenStore _marketTokenStore;
    private static readonly string[] SourceIds = ["overwolf-inventory", "public-export", "worldstate-pc", "warframe-market", "references"];
    public PlatformStatusService(IMarketTokenStore? marketTokenStore = null) =>
        _marketTokenStore = marketTokenStore ?? new FileMarketTokenStore(MyFrameStoragePaths.MarketTokenPath);

    public CapabilitiesResponse GetCapabilities() => new(DateTimeOffset.UtcNow,
    [
        new("inventory.overwolf", "pending_external_validation", "Native GEP contract is implemented; real capture and Arsenal comparison are still required."),
        new("catalog.public_export", "partial", "Index/parser and SQLite publication are available; document host access may be unavailable."),
        new("worldstate", "available", "World State adapter supports bounties, cycles, validity and attributed rewards."),
        new("market.public", "available", "Public market queries do not require a credential."),
        new("market.private", "optional", "Account/orders require an independent market credential."),
        new("references.wiki_overframe", "import_only", "References require an explicitly permitted, attributed import and remain untrusted for facts."),
        new("mcp.domain", "partial", "Current MCP tools remain available; rich inventory/catalog/activity tools are being added incrementally.")
    ]);

    public async Task<MarketCredentialStatusResponse> GetMarketCredentialStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var status = MarketCredentialService.Classify(await _marketTokenStore.ReadAsync(cancellationToken));
        return new(DateTimeOffset.UtcNow, status.State.ToString().ToLowerInvariant(), status.ExpiresAt, status.IsConfigured);
    }

    public async Task<SyncStatusResponse> GetSyncStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var values = new List<SyncSourceStatusDto>();
        foreach (var sourceId in SourceIds)
        {
            var status = await database.GetStatusAsync(sourceId, cancellationToken);
            if (sourceId == "references" && status is null)
            {
                var directory = Path.Combine(MyFrameStoragePaths.RootDirectory, "references");
                long count = 0, rejected = 0;
                if (Directory.Exists(directory))
                    foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        try { _ = ReferenceDocumentParser.Parse(File.ReadAllText(path), File.GetLastWriteTimeUtc(path)); count++; }
                        catch (InvalidDataException) { rejected++; }
                        catch (JsonException) { rejected++; }
                    }
                values.Add(new(sourceId, count == 0 ? "not_initialized" : rejected == 0 ? "available" : "partial", null, null, null,
                    null, count == 0 ? null : "reference-file-1", count, rejected));
                continue;
            }
            values.Add(status is null
                ? new(sourceId, "not_initialized", null, null, null, null, null, 0, 0)
                : new(sourceId, status.LastRunState ?? "unknown", status.LastRunState, status.LastRunAt,
                    status.ErrorCode, status.ActiveRevisionId, status.ParserVersion,
                    status.AcceptedRecords, status.RejectedRecords));
        }
        return new(DateTimeOffset.UtcNow, values);
    }

    public async Task<CaptureInboxStatusResponse> GetCaptureInboxStatusAsync(CancellationToken cancellationToken = default)
    {
        var directory = MyFrameStoragePaths.CollectorCaptureDirectory;
        var probe = await CollectorCaptureStatusProbe.ReadAsync(directory, cancellationToken);
        if (!probe.DirectoryExists)
            return new(DateTimeOffset.UtcNow, "not_initialized", 0, null, null,
                OverwolfRunning: probe.OverwolfRunning, WarframeRunning: probe.WarframeRunning,
                HeartbeatState: probe.HeartbeatState, HeartbeatFresh: probe.HeartbeatFresh,
                CollectorState: probe.CollectorState, SupportedFeatures: probe.SupportedFeatures,
                EventCounts: probe.EventCounts, LastEventFeature: probe.LastEventFeature,
                LastEventAt: probe.LastEventAt);

        var markers = Directory.EnumerateFiles(directory, "*.ready.json", SearchOption.TopDirectoryOnly).ToArray();
        DateTimeOffset? newest = null;
        foreach (var marker in markers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observed = File.GetLastWriteTimeUtc(marker);
            var value = new DateTimeOffset(DateTime.SpecifyKind(observed, DateTimeKind.Utc));
            if (newest is null || value > newest) newest = value;
        }

        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var status = await database.GetStatusAsync("overwolf-inventory", cancellationToken);
        var revision = await database.GetActiveInventoryRevisionStatusAsync(cancellationToken);
        return new(DateTimeOffset.UtcNow, probe.State,
            markers.Length, newest, status?.ErrorCode, revision?.CaptureMode,
            revision?.Completeness, revision?.Sequence, probe.OverwolfRunning,
            probe.WarframeRunning, probe.HeartbeatState, probe.HeartbeatFresh,
            probe.ValidMarkers, probe.InvalidMarkers, probe.CollectorState,
            probe.SupportedFeatures, probe.EventCounts, probe.LastEventFeature, probe.LastEventAt);
    }

    public async Task<SyncHistoryResponse> GetSyncHistoryAsync(
        int limit = 20, string? sourceId = null, CancellationToken cancellationToken = default)
    {
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var runs = await database.GetRecentRunsAsync(sourceId, Math.Clamp(limit, 1, 100), cancellationToken);
        return new(runs.Select(run => new SyncRunDto(run.RunId, run.SourceId, run.State,
            run.StartedAt, run.FinishedAt, run.RecordsReceived, run.RecordsAccepted,
            run.RecordsRejected, run.ErrorCode)).ToArray());
    }

    public async Task<InventoryCoverageResponse> GetInventoryCoverageAsync(
        CancellationToken cancellationToken = default)
    {
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var coverage = await database.GetInventoryCoverageAsync(cancellationToken);
        return new(coverage.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new InventoryCoverageDto(pair.Key, pair.Value.ToString()))
            .ToArray());
    }

    public async Task<InventoryHistoryResponse> GetInventoryHistoryAsync(
        int limit = 20, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var revisions = await database.GetInventoryRevisionSummariesAsync(limit, cancellationToken);
        return new(DateTimeOffset.UtcNow, revisions.Count == 0 ? "not_initialized" : "available",
            revisions.Select(value => new InventoryRevisionDto(value.RevisionId, value.ContentHash,
                value.Sequence, value.Completeness, value.CaptureMode, value.RetrievedAt)).ToArray());
    }

    public async Task<InventoryChangesResponse> GetInventoryChangesAsync(
        string? fromRevisionId = null, string? toRevisionId = null, int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 500.");
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var summaries = await database.GetInventoryRevisionSummariesAsync(100, cancellationToken);
        if (summaries.Count == 0)
            return new(DateTimeOffset.UtcNow, "not_initialized", null, null, []);
        var to = toRevisionId is null ? summaries[0] : summaries.FirstOrDefault(value => value.RevisionId == toRevisionId);
        if (to is null) return new(DateTimeOffset.UtcNow, "not_found", fromRevisionId, toRevisionId, []);
        var from = fromRevisionId is null
            ? summaries.FirstOrDefault(value => value.RevisionId != to.RevisionId)
            : summaries.FirstOrDefault(value => value.RevisionId == fromRevisionId);
        if (from is null) return new(DateTimeOffset.UtcNow, "insufficient_history", null, to.RevisionId, []);
        var before = await database.GetInventoryRevisionDataAsync(from.RevisionId, cancellationToken);
        var after = await database.GetInventoryRevisionDataAsync(to.RevisionId, cancellationToken);
        if (before is null || after is null) return new(DateTimeOffset.UtcNow, "not_found", from.RevisionId, to.RevisionId, []);
        if (before.Summary.CaptureMode == "delta" || after.Summary.CaptureMode == "delta")
            return new(DateTimeOffset.UtcNow, "partial", from.RevisionId, to.RevisionId, []);

        var changes = new List<InventoryChangeDto>();
        var equipmentBefore = before.Equipment.ToDictionary(value => value.InstanceId, StringComparer.Ordinal);
        var equipmentAfter = after.Equipment.ToDictionary(value => value.InstanceId, StringComparer.Ordinal);
        foreach (var key in equipmentBefore.Keys.Union(equipmentAfter.Keys, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            equipmentBefore.TryGetValue(key, out var oldValue);
            equipmentAfter.TryGetValue(key, out var newValue);
            var changed = oldValue is null || newValue is null || oldValue.TypeId != newValue.TypeId ||
                oldValue.Rank != newValue.Rank || oldValue.RankState != newValue.RankState || oldValue.ConfigState != newValue.ConfigState;
            if (changed)
                changes.Add(new("equipment", key, oldValue is null ? "added" : newValue is null ? "removed" : "changed",
                    oldValue?.TypeId, newValue?.TypeId, oldValue?.Rank, newValue?.Rank, null, null,
                    oldValue?.RankState.ToString() ?? "NotObserved", newValue?.RankState.ToString() ?? "NotObserved"));
        }

        static Dictionary<string, InventoryStackableRecord> Aggregate(IReadOnlyList<InventoryStackableRecord> values) =>
            values.Where(value => value.TypeId is not null).GroupBy(value => value.TypeId!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => new InventoryStackableRecord(group.Key,
                    group.All(value => value.Quantity is not null) ? group.Sum(value => value.Quantity!.Value) : null,
                    group.All(value => value.QuantityState == InventoryFieldState.Known) ? InventoryFieldState.Known : InventoryFieldState.NotObserved, "{}"), StringComparer.Ordinal);
        var stackBefore = Aggregate(before.Stackables);
        var stackAfter = Aggregate(after.Stackables);
        foreach (var key in stackBefore.Keys.Union(stackAfter.Keys, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            stackBefore.TryGetValue(key, out var oldValue);
            stackAfter.TryGetValue(key, out var newValue);
            if (oldValue?.Quantity == newValue?.Quantity && oldValue?.QuantityState == newValue?.QuantityState) continue;
            changes.Add(new("stackable", key, oldValue is null ? "added" : newValue is null ? "removed" : "changed",
                null, null, null, null, oldValue?.Quantity, newValue?.Quantity,
                oldValue?.QuantityState.ToString() ?? "NotObserved", newValue?.QuantityState.ToString() ?? "NotObserved"));
        }
        return new(DateTimeOffset.UtcNow, "available", from.RevisionId, to.RevisionId, changes.Take(limit).ToArray());
    }

    public async Task<SourceCoverageResponse> GetSourceCoverageAsync(
        string sourceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 100)
            throw new ArgumentException("sourceId must contain 1 to 100 characters.", nameof(sourceId));
        var allowed = new[] { "overwolf-inventory", "public-export", "worldstate-pc", "references" };
        if (!allowed.Contains(sourceId, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("sourceId is not a coverage-enabled source.", nameof(sourceId));
        if (string.Equals(sourceId, "references", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.Combine(MyFrameStoragePaths.RootDirectory, "references");
            var accepted = 0;
            var rejected = 0;
            if (Directory.Exists(directory))
                foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { _ = ReferenceDocumentParser.Parse(File.ReadAllText(path), File.GetLastWriteTimeUtc(path)); accepted++; }
                    catch (InvalidDataException) { rejected++; }
                    catch (JsonException) { rejected++; }
                }
            var state = accepted > 0 ? InventoryFieldState.Known : InventoryFieldState.NotObserved;
            var attribution = accepted > 0 ? InventoryFieldState.Known : InventoryFieldState.NotObserved;
            var sections = accepted > 0 ? InventoryFieldState.Known : InventoryFieldState.NotObserved;
            var rejectedState = rejected > 0 ? InventoryFieldState.Invalid : InventoryFieldState.NotObserved;
            var values = new Dictionary<string, InventoryFieldState>(StringComparer.Ordinal)
            {
                ["documents"] = state,
                ["attribution"] = attribution,
                ["sections"] = sections,
                ["rejectedDocuments"] = rejectedState
            };
            return new(values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new SourceCoverageDto(sourceId, pair.Key, pair.Value.ToString())).ToArray(),
                DateTimeOffset.UtcNow, rejected > 0 ? "partial" : accepted > 0 ? "available" : "not_initialized");
        }
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var coverage = await database.GetSourceCoverageAsync(sourceId, cancellationToken);
        var status = await database.GetStatusAsync(sourceId, cancellationToken);
        return new(coverage.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SourceCoverageDto(sourceId, pair.Key, pair.Value.ToString()))
            .ToArray(), DateTimeOffset.UtcNow,
            status is null ? "not_initialized" : status.LastRunState ?? "unknown",
            status?.ActiveRevisionId, status?.ParserVersion);
    }

    public async Task<PublicExportSearchResponse> SearchPublicExportAsync(
        string? text = null, string? category = null, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 200.");
        if (text?.Length > 200 || category?.Length > 100)
            throw new ArgumentException("Search filters exceed their length limit.");
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var normalizedText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        var records = await database.GetPublicExportItemsAsync("public-export", cancellationToken);
        var items = records
            .Where(record => normalizedCategory is null ||
                string.Equals(record.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(PublicExportCatalogMapper.Map(record).ProductCategory, normalizedCategory, StringComparison.OrdinalIgnoreCase))
            .Where(record => normalizedText is null || Contains(record.UniqueName, normalizedText) ||
                Contains(record.Name, normalizedText) || Contains(record.Category, normalizedText) ||
                record.Aliases.Values.Any(value => Contains(value, normalizedText)))
            .OrderBy(record => record.Name ?? record.UniqueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.UniqueName, StringComparer.Ordinal)
            .Take(limit)
            .Select(ToPublicExportItem)
            .ToArray();
        var status = await database.GetStatusAsync("public-export", cancellationToken);
        var coverage = await database.GetSourceCoverageAsync("public-export", cancellationToken);
        return new(DateTimeOffset.UtcNow, status is null ? "not_initialized" : status.LastRunState ?? "unknown",
            status?.ActiveRevisionId, status?.ParserVersion,
            coverage.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal), items);
    }

    public async Task<PublicExportItemResponse> GetPublicExportItemAsync(
        string itemId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemId) || itemId.Length > 512)
            throw new ArgumentException("itemId is required and limited to 512 characters.", nameof(itemId));
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var status = await database.GetStatusAsync("public-export", cancellationToken);
        var coverage = await database.GetSourceCoverageAsync("public-export", cancellationToken);
        var records = await database.GetPublicExportItemsAsync("public-export", cancellationToken);
        var normalized = itemId.Trim();
        var match = records.FirstOrDefault(record =>
            PublicExportIdentity.Equivalent(record.UniqueName, normalized) ||
            (!string.IsNullOrWhiteSpace(record.Name) && PublicExportIdentity.Equivalent(record.Name, normalized)) ||
            record.Aliases.Values.Any(alias => PublicExportIdentity.Equivalent(alias, normalized)));
        return new(DateTimeOffset.UtcNow, status is null ? "not_initialized" :
            status.LastRunState ?? "unknown", status?.ActiveRevisionId, status?.ParserVersion,
            coverage.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal),
            match is null ? null : ToPublicExportItem(match));
    }

    private static PublicExportItemDto ToPublicExportItem(PublicExportRecord record)
    {
        var item = PublicExportCatalogMapper.Map(record);
        return new(record.UniqueName, item.Name, item.Category, item.Description, record.Aliases,
            item.ProductCategory, item.ImageName, item.Masterable, item.Prime, item.Tradable,
            item.Vaulted, item.MarketId, item.MarketSlug, item.ItemType, item.Components, item.Relics);
    }

    public Task<ReferenceSearchResponse> SearchReferencesAsync(
        string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("query is required.", nameof(query));
        if (query.Length > 200) throw new ArgumentException("query is limited to 200 characters.", nameof(query));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(MyFrameStoragePaths.RootDirectory, "references");
        if (!Directory.Exists(directory))
            return Task.FromResult(new ReferenceSearchResponse(DateTimeOffset.UtcNow, "not_initialized", 0, 0, []));

        var documents = new List<ReferenceDocument>();
        var rejected = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { documents.Add(ReferenceDocumentParser.Parse(File.ReadAllText(path), File.GetLastWriteTimeUtc(path))); }
            catch (InvalidDataException) { rejected++; }
            catch (JsonException) { rejected++; }
        }
        var hits = ReferenceSearch.Search(documents, query.Trim(), limit);
        var result = hits.Select(hit =>
        {
            var document = documents.First(value => value.Url == hit.SourceUrl && value.Revision == hit.Revision);
            return new ReferenceSearchHitDto(document.Kind.ToString(), document.Title, hit.SectionId,
                hit.Title, hit.Snippet, hit.Score, hit.SourceUrl.ToString(), hit.Revision,
                document.License, document.Author, document.IsTrustedForFacts);
        }).ToArray();
        var state = documents.Count == 0 ? "empty" : rejected == 0 ? "available" : "partial";
        return Task.FromResult(new ReferenceSearchResponse(DateTimeOffset.UtcNow, state, documents.Count, rejected, result));
    }

    public Task<ReferenceSectionResponse> GetReferenceSectionAsync(
        string url, string sectionId, string? revision = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048)
            throw new ArgumentException("url is required and limited to 2048 characters.", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var sourceUri))
            throw new ArgumentException("url must be an absolute URL.", nameof(url));
        if (string.IsNullOrWhiteSpace(sectionId) || sectionId.Length > 200)
            throw new ArgumentException("sectionId is required and limited to 200 characters.", nameof(sectionId));
        if (revision?.Length > 200) throw new ArgumentException("revision is limited to 200 characters.", nameof(revision));
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.Combine(MyFrameStoragePaths.RootDirectory, "references");
        if (!Directory.Exists(directory))
            return Task.FromResult(new ReferenceSectionResponse(DateTimeOffset.UtcNow, "not_initialized", 0, 0,
                null, null, null, null, null, null, false, null, null, null, false));

        var documents = new List<ReferenceDocument>();
        var rejected = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { documents.Add(ReferenceDocumentParser.Parse(File.ReadAllText(path), File.GetLastWriteTimeUtc(path))); }
            catch (InvalidDataException) { rejected++; }
            catch (JsonException) { rejected++; }
        }

        var document = documents.FirstOrDefault(value =>
            string.Equals(value.Url.AbsoluteUri, sourceUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase) &&
            (revision is null || string.Equals(value.Revision, revision, StringComparison.Ordinal)));
        var section = document?.Sections.FirstOrDefault(value => string.Equals(value.Id, sectionId, StringComparison.Ordinal));
        if (document is null || section is null)
            return Task.FromResult(new ReferenceSectionResponse(DateTimeOffset.UtcNow, "not_found", documents.Count, rejected,
                null, null, null, null, null, null, false, null, null, null, false));

        const int maxContent = 20_000;
        var truncated = section.Content.Length > maxContent;
        var content = truncated ? section.Content[..maxContent] : section.Content;
        return Task.FromResult(new ReferenceSectionResponse(DateTimeOffset.UtcNow, "available", documents.Count, rejected,
            document.Kind.ToString(), document.Title, document.Url.ToString(), document.Revision,
            document.License, document.Author, document.IsTrustedForFacts, section.Id, section.Title, content, truncated));
    }

    private static bool Contains(string? value, string text) =>
        value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;

    public async Task<InventoryEquipmentResponse> GetInventoryEquipmentAsync(
        string? typeId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 200.");

        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var equipment = await database.GetInventoryEquipmentAsync(cancellationToken);
        return new(equipment
            .Where(item => string.IsNullOrWhiteSpace(typeId) ||
                string.Equals(item.TypeId, typeId, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(item => new InventoryEquipmentDto(item.InstanceId, item.TypeId, item.Rank,
                item.ConfigJson, item.RankState.ToString(), item.ConfigState.ToString()))
            .ToArray());
    }

    public async Task<InventoryUpgradesResponse> GetInventoryUpgradesAsync(
        string? ownerInstanceId = null, string? sourceField = null, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 200.");

        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var upgrades = await database.GetInventoryUpgradesAsync(cancellationToken);
        return new(upgrades
            .Where(item => string.IsNullOrWhiteSpace(ownerInstanceId) ||
                string.Equals(item.OwnerInstanceId, ownerInstanceId, StringComparison.Ordinal))
            .Where(item => string.IsNullOrWhiteSpace(sourceField) ||
                string.Equals(item.SourceField, sourceField, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(item => new InventoryUpgradeDto(item.OwnerInstanceId, item.SourceField,
                item.UpgradeId, item.Rank))
            .ToArray());
    }

    public async Task<LoadoutResponse> GetLoadoutsAsync(
        string? typeId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 200.");

        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var equipment = await database.GetInventoryEquipmentAsync(cancellationToken);
        var upgrades = await database.GetInventoryUpgradesAsync(cancellationToken);
        return new(equipment
            .Where(item => string.IsNullOrWhiteSpace(typeId) ||
                string.Equals(item.TypeId, typeId, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(item => new LoadoutDto(item.InstanceId, item.TypeId, item.Rank, item.ConfigJson,
                item.RankState.ToString(), item.ConfigState.ToString(), upgrades
                    .Where(upgrade => string.Equals(upgrade.OwnerInstanceId, item.InstanceId, StringComparison.Ordinal))
                    .Select(upgrade => new InventoryUpgradeDto(upgrade.OwnerInstanceId, upgrade.SourceField,
                        upgrade.UpgradeId, upgrade.Rank))
                    .ToArray()))
            .ToArray());
    }

    public async Task<AcquisitionResponse> GetAcquisitionAsync(string itemId, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemId) || itemId.Length > 512)
            throw new ArgumentException("itemId is required and limited to 512 characters.", nameof(itemId));
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 200.");

        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var catalogStatus = await database.GetStatusAsync("public-export", cancellationToken);
        var worldStatus = await database.GetStatusAsync("worldstate-pc", cancellationToken);
        var catalogCoverage = await database.GetSourceCoverageAsync("public-export", cancellationToken);
        var worldCoverage = await database.GetSourceCoverageAsync("worldstate-pc", cancellationToken);
        var coverage = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["catalog.components"] = catalogCoverage.GetValueOrDefault("components", InventoryFieldState.NotObserved).ToString(),
            ["catalog.relics"] = catalogCoverage.GetValueOrDefault("relics", InventoryFieldState.NotObserved).ToString(),
            ["worldstate.bountyRewards"] = worldCoverage.GetValueOrDefault("bountyRewards", InventoryFieldState.NotObserved).ToString(),
            ["worldstate.motherTokens"] = worldCoverage.GetValueOrDefault("motherTokens", InventoryFieldState.NotObserved).ToString()
        };
        var items = await database.GetPublicExportItemsAsync("public-export", cancellationToken);
        var normalizedItemId = itemId.Trim();
        var item = items.FirstOrDefault(value =>
            PublicExportIdentity.Equivalent(value.UniqueName, normalizedItemId) ||
            (!string.IsNullOrWhiteSpace(value.Name) && PublicExportIdentity.Equivalent(value.Name, normalizedItemId)) ||
            value.Aliases.Values.Any(alias => PublicExportIdentity.Equivalent(alias, normalizedItemId)));
        if (item is null)
        {
            var catalogState = catalogStatus is null ? "not_initialized" : catalogStatus.LastRunState ?? "unknown";
            var resultState = catalogState == "not_initialized" ? "not_initialized" :
                catalogState == "published" ? "item_not_found" : "catalog_unavailable";
            return new(DateTimeOffset.UtcNow, resultState,
                catalogStatus?.ErrorCode, catalogStatus?.ActiveRevisionId, worldStatus?.ActiveRevisionId,
                itemId, null, [], [], [], coverage);
        }

        var components = (await database.GetPublicExportComponentsAsync("public-export", cancellationToken))
            .Where(value => string.Equals(value.ParentUniqueName, item.UniqueName, StringComparison.Ordinal))
            .Take(limit)
            .Select(value => new AcquisitionComponentDto(value.UniqueName, value.Name, value.RequiredCount,
                value.Ducats, value.Tradable, value.ImageName)).ToArray();
        var relics = (await database.GetPublicExportRelicsAsync("public-export", cancellationToken))
            .Where(value => string.Equals(value.RewardUniqueName, item.UniqueName, StringComparison.Ordinal))
            .Take(limit)
            .Select(value => new AcquisitionRelicDto(value.RelicName, value.Rarity, value.Chance,
                value.Vaulted, value.RewardName)).ToArray();
        var bounties = await database.GetCurrentWorldStateBountiesAsync(DateTimeOffset.UtcNow, cancellationToken);
        var rewardNames = new[] { item.Name, item.UniqueName }.Concat(item.Aliases.Values)
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        var matchedBounties = bounties.Where(bounty => bounty.Jobs.Any(job => job.Rewards.Any(reward =>
                RewardMatches(reward.Item, rewardNames))))
            .Take(limit)
            .Select(ToBountyDto).ToArray();
        var catalogAvailable = catalogStatus?.LastRunState == "published";
        var worldAvailable = worldStatus?.LastRunState == "published";
        var state = catalogAvailable && worldAvailable ? "available" : catalogAvailable ? "partial" : "catalog_unavailable";
        return new(DateTimeOffset.UtcNow, state, worldStatus?.ErrorCode ?? catalogStatus?.ErrorCode,
            catalogStatus?.ActiveRevisionId, worldStatus?.ActiveRevisionId, item.UniqueName, item.Name,
            components, relics, matchedBounties, coverage);
    }

    private static WorldStateBountyDto ToBountyDto(WorldStateBounty bounty) =>
        new(bounty.Id, bounty.Syndicate, bounty.Activation, bounty.Expiry,
            bounty.Jobs.Select(job => new WorldStateJobDto(job.Id, job.Type, job.UniqueName,
                job.MinimumMasteryRank, job.StandingStages, job.Rewards.Select(reward =>
            new WorldStateRewardDto(reward.Item, reward.Chance, reward.Count, reward.Rarity)).ToArray())).ToArray());

    private static bool RewardMatches(string reward, IReadOnlyList<string> names)
    {
        var normalizedReward = PublicExportIdentity.Canonicalize(reward);
        return names.Any(name =>
        {
            var normalizedName = PublicExportIdentity.Canonicalize(name);
            return normalizedName.Length > 0 &&
                (string.Equals(normalizedReward, normalizedName, StringComparison.Ordinal) ||
                 normalizedReward.Contains(normalizedName, StringComparison.Ordinal));
        });
    }

    public async Task<WorldStateBountiesResponse> GetBountiesAsync(
        int limit = 100, string? syndicate = null, string? reward = null,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 200.");
        if (reward?.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(reward), "reward is limited to 200 characters.");

        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var bounties = await database.GetCurrentWorldStateBountiesAsync(DateTimeOffset.UtcNow, cancellationToken);
        var status = await database.GetStatusAsync("worldstate-pc", cancellationToken);
        var state = status is null ? "not_initialized" : status.LastRunState == "published" ? "available" : status.LastRunState ?? "unknown";
        var normalizedReward = string.IsNullOrWhiteSpace(reward) ? null : reward.Trim();
        return new(DateTimeOffset.UtcNow, state, status?.LastRunAt, status?.ErrorCode, bounties
            .Where(bounty => string.IsNullOrWhiteSpace(syndicate) ||
                string.Equals(bounty.Syndicate, syndicate, StringComparison.OrdinalIgnoreCase))
            .Where(bounty => normalizedReward is null || bounty.Jobs.Any(job =>
                job.Rewards.Any(value => RewardMatches(value.Item, [normalizedReward!]))))
            .Take(limit).Select(bounty => new WorldStateBountyDto(bounty.Id, bounty.Syndicate,
            bounty.Activation, bounty.Expiry, bounty.Jobs.Select(job => new WorldStateJobDto(job.Id, job.Type,
                job.UniqueName, job.MinimumMasteryRank, job.StandingStages, job.Rewards.Select(reward =>
            new WorldStateRewardDto(reward.Item, reward.Chance, reward.Count, reward.Rarity)).ToArray())).ToArray())).ToArray(),
            status?.ActiveRevisionId, status?.ParserVersion);
    }

    public async Task<WorldStateResponse> GetWorldStateAsync(int limit = 100, string? syndicate = null,
        string? reward = null,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 200.");
        if (reward?.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(reward), "reward is limited to 200 characters.");
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var status = await database.GetStatusAsync("worldstate-pc", cancellationToken);
        var bounties = await database.GetCurrentWorldStateBountiesAsync(DateTimeOffset.UtcNow, cancellationToken);
        var cycles = await database.GetCurrentWorldStateCyclesAsync(cancellationToken);
        var coverage = await database.GetSourceCoverageAsync("worldstate-pc", cancellationToken);
        var state = status is null ? "not_initialized" : status.LastRunState == "published" ? "available" : status.LastRunState ?? "unknown";
        var normalizedReward = string.IsNullOrWhiteSpace(reward) ? null : reward.Trim();
        return new(DateTimeOffset.UtcNow, state, status?.LastRunAt, status?.ErrorCode,
            bounties.Where(bounty => string.IsNullOrWhiteSpace(syndicate) ||
                string.Equals(bounty.Syndicate, syndicate, StringComparison.OrdinalIgnoreCase))
                .Where(bounty => normalizedReward is null || bounty.Jobs.Any(job =>
                    job.Rewards.Any(value => RewardMatches(value.Item, [normalizedReward!]))))
                .Take(limit).Select(bounty => new WorldStateBountyDto(bounty.Id, bounty.Syndicate,
                bounty.Activation, bounty.Expiry, bounty.Jobs.Select(job => new WorldStateJobDto(job.Id, job.Type,
                    job.UniqueName, job.MinimumMasteryRank, job.StandingStages, job.Rewards.Select(reward =>
                        new WorldStateRewardDto(reward.Item, reward.Chance, reward.Count, reward.Rarity)).ToArray())).ToArray())).ToArray(),
            cycles.Select(cycle => new WorldStateCycleDto(cycle.Name, cycle.State, cycle.Activation, cycle.Expiry)).ToArray(),
            coverage.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal),
            status?.ActiveRevisionId, status?.ParserVersion);
    }

    public Task<WorldStateResponse> GetActivityAsync(int limit = 100, string? syndicate = null,
        string? reward = null,
        CancellationToken cancellationToken = default) => GetWorldStateAsync(limit, syndicate, reward, cancellationToken);
}
