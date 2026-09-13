using MyFrame.Core.Sync;

namespace MyFrame.Core.Tests;

public sealed class SyncDatabaseTests
{
    [Fact]
    public async Task InitializesIdempotentlyAndPublishesStatus()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await db.InitializeAsync();
        await db.InitializeAsync();
        var result = await db.PublishAsync(new SyncBatch("warframe", "hash-1", "{\"items\":[]}", 0));
        var status = await db.GetStatusAsync("warframe");
        Assert.False(result.AlreadyPublished);
        Assert.Equal(result.RevisionId, status!.ActiveRevisionId);
        Assert.Equal("published", status.LastRunState);
    }

    [Fact]
    public async Task SameContentHashIsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var batch = new SyncBatch("wiki", "same", "{}", 1);
        var first = await db.PublishAsync(batch);
        var second = await db.PublishAsync(batch);
        Assert.False(first.AlreadyPublished);
        Assert.True(second.AlreadyPublished);
        Assert.Equal(first.RevisionId, second.RevisionId);
    }

    [Fact]
    public async Task InvalidBatchDoesNotCreateSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await Assert.ThrowsAsync<ArgumentException>(() => db.PublishAsync(new SyncBatch("", "hash", "{}", 1)));
        Assert.Null(await db.GetStatusAsync("missing"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ConcurrentWritersKeepOneActiveRevision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => db.PublishAsync(new SyncBatch("source", $"hash-{i}", "{}", i))));
        var status = await db.GetStatusAsync("source");
        Assert.Equal(4, results.Count(r => !r.AlreadyPublished));
        Assert.NotNull(status!.ActiveRevisionId);
    }

    [Fact]
    public async Task BackupCanBeOpenedReadOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "sync.db");
        var backup = Path.Combine(root, "backup", "sync.db");
        await using (var db = new SyncDatabase(path))
        {
            await db.PublishAsync(new SyncBatch("warframe", "hash", "{}", 2));
            await db.BackupAsync(backup);
        }
        await using var restored = new SyncDatabase(backup);
        var status = await restored.GetStatusAsync("warframe");
        Assert.Equal("hash", status!.ActiveContentHash);
    }

    [Fact]
    public async Task HostRecordsFailureWithoutReplacingActiveRevision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await db.PublishAsync(new SyncBatch("source", "good", "{}", 1));
        await using var host = new SyncHost(db);
        var result = await host.RunOnceAsync("source", _ => throw new InvalidDataException("SCHEMA_INVALID"));
        var status = await db.GetStatusAsync("source");
        Assert.Null(result);
        Assert.Equal("good", status!.ActiveContentHash);
        Assert.Equal("SCHEMA_INVALID", status.ErrorCode);
        Assert.Equal("failed", status.LastRunState);
    }

    [Fact]
    public async Task CatalogPublicationStoresNormalizedItemsAtomically()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var records = new[] { new PublicExportRecord("/Lotus/Test", "Lâmina", "Melee", null, new Dictionary<string, string>()) };
        var result = await db.PublishCatalogAsync(new SyncBatch("public-export", "hash", "[]", 1), records);
        var stored = await db.GetPublicExportItemsAsync("public-export");
        Assert.False(result.AlreadyPublished);
        Assert.Single(stored);
        Assert.Equal("lamina", PublicExportIdentity.Canonicalize(stored[0].Name!));
    }

    [Fact]
    public async Task HostPreservesHttpFailureCodeForStatusPage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await using var host = new SyncHost(db);
        await host.RunOnceAsync("public-export", _ => throw new HttpRequestException("PUBLIC_EXPORT_HTTP_403"));
        var status = await db.GetStatusAsync("public-export");
        Assert.Equal("PUBLIC_EXPORT_HTTP_403", status!.ErrorCode);
    }

    [Fact]
    public async Task InventoryPublicationPreservesInstanceAndCoverage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var envelope = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(), 2, DateTimeOffset.UtcNow, "native", "unverified", "{\"equipment\":[]}", "inventory-hash");
        var projection = new InventoryProjection([new InventoryEquipmentRecord("instance-1", "/Lotus/Test", 30, null, InventoryFieldState.Known, InventoryFieldState.NotObserved, "{\"instanceId\":\"instance-1\"}")], [], [new InventoryUnknownRecord("futureField", "true", "FIELD_NOT_MAPPED")], new Dictionary<string, InventoryFieldState> { ["equipment"] = InventoryFieldState.Known });
        await db.PublishInventoryAsync(envelope, projection);
        var stored = await db.GetInventoryEquipmentAsync();
        Assert.Single(stored);
        Assert.Equal("instance-1", stored[0].InstanceId);
        Assert.Equal(InventoryFieldState.NotObserved, stored[0].ConfigState);
    }

    [Fact]
    public async Task WorldStatePublicationKeepsExpiryAndSourceRevision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var snapshot = new WorldStateSnapshot(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "fixture", [new WorldStateBounty("bounty", "Entrati", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10), [new WorldStateJob("job", "Sample", null, 0, [100], [new WorldStateReward("Endo", 50, 100, "Common")])])], [], "world-hash", true, new Dictionary<string, InventoryFieldState>());
        await db.PublishWorldStateAsync(snapshot, new SyncBatch("worldstate-pc", "world-hash", "{}", 1));
        var current = await db.GetCurrentWorldStateBountiesAsync(DateTimeOffset.UtcNow);
        Assert.Single(current);
        Assert.Equal("Entrati", current[0].Syndicate);
    }
}
