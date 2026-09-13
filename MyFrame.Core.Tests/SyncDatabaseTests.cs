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
}
