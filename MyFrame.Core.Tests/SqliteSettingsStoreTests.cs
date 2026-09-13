using System.Text.Json;
using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class SqliteSettingsStoreTests
{
    [Fact]
    public async Task ImportsLegacySettingsAndPersistsUpdatesInSqlite()
    {
        using var folder = new TemporaryFolder();
        var database = Path.Combine(folder.Path, "data.db");
        var legacy = Path.Combine(folder.Path, "settings.v1.json");
        var original = new MyFrameSettingsDocument(1, 3, "C:\\Aleca", 0, 99, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(legacy, JsonSerializer.Serialize(original,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var store = new SqliteSettingsStore(database, legacy);
        var imported = await store.LoadAsync();
        Assert.NotNull(imported);
        Assert.Equal(1, imported!.DucatsPerPlatinum);
        Assert.Equal(10, imported.UnvaultedPrimeSetsToReserve);

        var updated = imported with { Revision = 4, DucatsPerPlatinum = 7 };
        await store.SaveAsync(updated);
        Assert.True(File.Exists(legacy));

        var reopened = new SqliteSettingsStore(database);
        var result = await reopened.LoadAsync();
        Assert.NotNull(result);
        Assert.Equal(4, result!.Revision);
        Assert.Equal(7, result.DucatsPerPlatinum);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "my-frame-settings-sqlite-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
