using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.Mcp;

public sealed record CapabilityDto(string Name, string State, string Detail);
public sealed record CapabilitiesResponse(DateTimeOffset ServedAt, IReadOnlyList<CapabilityDto> Capabilities);
public sealed record SyncSourceStatusDto(string SourceId, string State, string? LastRunState, DateTimeOffset? LastRunAt, string? ErrorCode);
public sealed record SyncStatusResponse(DateTimeOffset ServedAt, IReadOnlyList<SyncSourceStatusDto> Sources);
public sealed record CaptureInboxStatusResponse(DateTimeOffset ServedAt, string State,
    int PendingMarkers, DateTimeOffset? NewestMarkerAt, string? LastErrorCode);

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
}
