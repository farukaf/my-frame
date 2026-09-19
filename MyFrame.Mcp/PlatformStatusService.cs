using System.Text.Json;
using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.Mcp;

public sealed record CapabilityDto(string Name, string State, string Detail);
public sealed record CapabilitiesResponse(DateTimeOffset ServedAt, IReadOnlyList<CapabilityDto> Capabilities);
public sealed record SyncSourceStatusDto(string SourceId, string State, string? LastRunState,
    DateTimeOffset? LastRunAt, string? ErrorCode, string? ActiveRevisionId, string? ParserVersion,
    long AcceptedRecords, long RejectedRecords);
public sealed record SyncStatusResponse(DateTimeOffset ServedAt, IReadOnlyList<SyncSourceStatusDto> Sources);
public sealed record CaptureInboxStatusResponse(DateTimeOffset ServedAt, string State,
    int PendingMarkers, DateTimeOffset? NewestMarkerAt, string? LastErrorCode,
    string? ActiveCaptureMode = null, string? ActiveCompleteness = null, long? ActiveSequence = null);
public sealed record SyncRunDto(string RunId, string SourceId, string State,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, long RecordsReceived,
    long RecordsAccepted, long RecordsRejected, string? ErrorCode);
public sealed record InventoryCoverageDto(string FieldPath, string State);
public sealed record SourceCoverageDto(string SourceId, string FieldPath, string State);
public sealed record PublicExportItemDto(string UniqueName, string? Name, string? Category,
    string? Description, IReadOnlyDictionary<string, string> Aliases);
public sealed record PublicExportSearchResponse(DateTimeOffset ServedAt, string State,
    string? ActiveRevisionId, string? ParserVersion, IReadOnlyDictionary<string, string> Coverage,
    IReadOnlyList<PublicExportItemDto> Items);
public sealed record ReferenceSearchHitDto(string Kind, string Title, string SectionId,
    string? SectionTitle, string Snippet, double Score, string Url, string Revision,
    string? License, string? Author, bool TrustedForFacts);
public sealed record ReferenceSearchResponse(DateTimeOffset ServedAt, string State,
    int Documents, int RejectedDocuments, IReadOnlyList<ReferenceSearchHitDto> Hits);
public sealed record InventoryEquipmentDto(string InstanceId, string? TypeId, int? Rank,
    string? ConfigJson, string RankState, string ConfigState);
public sealed record InventoryUpgradeDto(string? OwnerInstanceId, string SourceField,
    string? UpgradeId, int? Rank);
public sealed record LoadoutDto(string InstanceId, string? TypeId, int? Rank, string? ConfigJson,
    string RankState, string ConfigState, IReadOnlyList<InventoryUpgradeDto> Upgrades);
public sealed record WorldStateRewardDto(string Item, decimal? Chance, int? Count, string? Rarity);
public sealed record WorldStateJobDto(string Id, string? Type, string? UniqueName,
    int? MinimumMasteryRank, IReadOnlyList<int> StandingStages, IReadOnlyList<WorldStateRewardDto> Rewards);
public sealed record WorldStateBountyDto(string Id, string? Syndicate, DateTimeOffset? Activation,
    DateTimeOffset? Expiry, IReadOnlyList<WorldStateJobDto> Jobs);
public sealed record WorldStateBountiesResponse(DateTimeOffset ServedAt, string State,
    DateTimeOffset? LastAttemptAt, string? ErrorCode, IReadOnlyList<WorldStateBountyDto> Bounties);
public sealed record WorldStateCycleDto(string Name, string? State, DateTimeOffset? Activation, DateTimeOffset? Expiry);
public sealed record WorldStateResponse(DateTimeOffset ServedAt, string State, DateTimeOffset? LastAttemptAt,
    string? ErrorCode, IReadOnlyList<WorldStateBountyDto> Bounties, IReadOnlyList<WorldStateCycleDto> Cycles,
    IReadOnlyDictionary<string, string> Coverage, string? ActiveRevisionId);

public sealed class PlatformStatusService
{
    private static readonly string[] SourceIds = ["overwolf-inventory", "public-export", "worldstate-pc", "warframe-market", "references"];
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
        if (!Directory.Exists(directory))
            return new(DateTimeOffset.UtcNow, "not_initialized", 0, null, null);

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
        return new(DateTimeOffset.UtcNow, markers.Length == 0 ? "ready" : "pending",
            markers.Length, newest, status?.ErrorCode, revision?.CaptureMode,
            revision?.Completeness, revision?.Sequence);
    }

    public async Task<IReadOnlyList<SyncRunDto>> GetSyncHistoryAsync(
        int limit = 20, string? sourceId = null, CancellationToken cancellationToken = default)
    {
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var runs = await database.GetRecentRunsAsync(sourceId, Math.Clamp(limit, 1, 100), cancellationToken);
        return runs.Select(run => new SyncRunDto(run.RunId, run.SourceId, run.State,
            run.StartedAt, run.FinishedAt, run.RecordsReceived, run.RecordsAccepted,
            run.RecordsRejected, run.ErrorCode)).ToArray();
    }

    public async Task<IReadOnlyList<InventoryCoverageDto>> GetInventoryCoverageAsync(
        CancellationToken cancellationToken = default)
    {
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var coverage = await database.GetInventoryCoverageAsync(cancellationToken);
        return coverage.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new InventoryCoverageDto(pair.Key, pair.Value.ToString()))
            .ToArray();
    }

    public async Task<IReadOnlyList<SourceCoverageDto>> GetSourceCoverageAsync(
        string sourceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 100)
            throw new ArgumentException("sourceId must contain 1 to 100 characters.", nameof(sourceId));
        var allowed = new[] { "overwolf-inventory", "public-export", "worldstate-pc" };
        if (!allowed.Contains(sourceId, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("sourceId is not a coverage-enabled source.", nameof(sourceId));
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var coverage = await database.GetSourceCoverageAsync(sourceId, cancellationToken);
        return coverage.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SourceCoverageDto(sourceId, pair.Key, pair.Value.ToString()))
            .ToArray();
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
            .Where(record => normalizedCategory is null || string.Equals(record.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
            .Where(record => normalizedText is null || Contains(record.UniqueName, normalizedText) ||
                Contains(record.Name, normalizedText) || Contains(record.Category, normalizedText) ||
                record.Aliases.Values.Any(value => Contains(value, normalizedText)))
            .OrderBy(record => record.Name ?? record.UniqueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.UniqueName, StringComparer.Ordinal)
            .Take(limit)
            .Select(record => new PublicExportItemDto(record.UniqueName, record.Name, record.Category,
                record.Description, record.Aliases))
            .ToArray();
        var status = await database.GetStatusAsync("public-export", cancellationToken);
        var coverage = await database.GetSourceCoverageAsync("public-export", cancellationToken);
        return new(DateTimeOffset.UtcNow, status is null ? "not_initialized" : status.LastRunState ?? "unknown",
            status?.ActiveRevisionId, status?.ParserVersion,
            coverage.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal), items);
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

    private static bool Contains(string? value, string text) =>
        value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;

    public async Task<IReadOnlyList<InventoryEquipmentDto>> GetInventoryEquipmentAsync(
        string? typeId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 200.");

        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var equipment = await database.GetInventoryEquipmentAsync(cancellationToken);
        return equipment
            .Where(item => string.IsNullOrWhiteSpace(typeId) ||
                string.Equals(item.TypeId, typeId, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(item => new InventoryEquipmentDto(item.InstanceId, item.TypeId, item.Rank,
                item.ConfigJson, item.RankState.ToString(), item.ConfigState.ToString()))
            .ToArray();
    }

    public async Task<IReadOnlyList<InventoryUpgradeDto>> GetInventoryUpgradesAsync(
        string? ownerInstanceId = null, string? sourceField = null, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 200.");

        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var upgrades = await database.GetInventoryUpgradesAsync(cancellationToken);
        return upgrades
            .Where(item => string.IsNullOrWhiteSpace(ownerInstanceId) ||
                string.Equals(item.OwnerInstanceId, ownerInstanceId, StringComparison.Ordinal))
            .Where(item => string.IsNullOrWhiteSpace(sourceField) ||
                string.Equals(item.SourceField, sourceField, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(item => new InventoryUpgradeDto(item.OwnerInstanceId, item.SourceField,
                item.UpgradeId, item.Rank))
            .ToArray();
    }

    public async Task<IReadOnlyList<LoadoutDto>> GetLoadoutsAsync(
        string? typeId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 200.");

        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        var equipment = await database.GetInventoryEquipmentAsync(cancellationToken);
        var upgrades = await database.GetInventoryUpgradesAsync(cancellationToken);
        return equipment
            .Where(item => string.IsNullOrWhiteSpace(typeId) ||
                string.Equals(item.TypeId, typeId, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(item => new LoadoutDto(item.InstanceId, item.TypeId, item.Rank, item.ConfigJson,
                item.RankState.ToString(), item.ConfigState.ToString(), upgrades
                    .Where(upgrade => string.Equals(upgrade.OwnerInstanceId, item.InstanceId, StringComparison.Ordinal))
                    .Select(upgrade => new InventoryUpgradeDto(upgrade.OwnerInstanceId, upgrade.SourceField,
                        upgrade.UpgradeId, upgrade.Rank))
                    .ToArray()))
            .ToArray();
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
                job.Rewards.Any(value => value.Item.Contains(normalizedReward, StringComparison.OrdinalIgnoreCase))))
            .Take(limit).Select(bounty => new WorldStateBountyDto(bounty.Id, bounty.Syndicate,
            bounty.Activation, bounty.Expiry, bounty.Jobs.Select(job => new WorldStateJobDto(job.Id, job.Type,
                job.UniqueName, job.MinimumMasteryRank, job.StandingStages, job.Rewards.Select(reward =>
            new WorldStateRewardDto(reward.Item, reward.Chance, reward.Count, reward.Rarity)).ToArray())).ToArray())).ToArray());
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
                    job.Rewards.Any(value => value.Item.Contains(normalizedReward, StringComparison.OrdinalIgnoreCase))))
                .Take(limit).Select(bounty => new WorldStateBountyDto(bounty.Id, bounty.Syndicate,
                bounty.Activation, bounty.Expiry, bounty.Jobs.Select(job => new WorldStateJobDto(job.Id, job.Type,
                    job.UniqueName, job.MinimumMasteryRank, job.StandingStages, job.Rewards.Select(reward =>
                        new WorldStateRewardDto(reward.Item, reward.Chance, reward.Count, reward.Rarity)).ToArray())).ToArray())).ToArray(),
            cycles.Select(cycle => new WorldStateCycleDto(cycle.Name, cycle.State, cycle.Activation, cycle.Expiry)).ToArray(),
            coverage.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal),
            status?.ActiveRevisionId);
    }

    public Task<WorldStateResponse> GetActivityAsync(int limit = 100, string? syndicate = null,
        string? reward = null,
        CancellationToken cancellationToken = default) => GetWorldStateAsync(limit, syndicate, reward, cancellationToken);
}
