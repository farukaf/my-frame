using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.Mcp;

public sealed record CapabilityDto(string Name, string State, string Detail);
public sealed record CapabilitiesResponse(DateTimeOffset ServedAt, IReadOnlyList<CapabilityDto> Capabilities);
public sealed record SyncSourceStatusDto(string SourceId, string State, string? LastRunState, DateTimeOffset? LastRunAt, string? ErrorCode);
public sealed record SyncStatusResponse(DateTimeOffset ServedAt, IReadOnlyList<SyncSourceStatusDto> Sources);
public sealed record CaptureInboxStatusResponse(DateTimeOffset ServedAt, string State,
    int PendingMarkers, DateTimeOffset? NewestMarkerAt, string? LastErrorCode);
public sealed record SyncRunDto(string RunId, string SourceId, string State,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, long RecordsReceived,
    long RecordsAccepted, long RecordsRejected, string? ErrorCode);
public sealed record InventoryCoverageDto(string FieldPath, string State);
public sealed record InventoryEquipmentDto(string InstanceId, string? TypeId, int? Rank,
    string? ConfigJson, string RankState, string ConfigState);

public sealed class PlatformStatusService
{
    private static readonly string[] SourceIds = ["overwolf-inventory", "public-export", "worldstate-pc", "market-public", "references"];
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
            values.Add(status is null
                ? new(sourceId, "not_initialized", null, null, null)
                : new(sourceId, status.LastRunState ?? "unknown", status.LastRunState, status.LastRunAt, status.ErrorCode));
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
        return new(DateTimeOffset.UtcNow, markers.Length == 0 ? "ready" : "pending",
            markers.Length, newest, status?.ErrorCode);
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
}
