using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.Core.Tests;

public sealed class SynchronizedDataReaderTests
{
    [Fact]
    public async Task SettingsOnlyDatabaseIsTreatedAsNoSynchronizedData()
    {
        using var folder = new TemporaryFolder();
        var database = Path.Combine(folder.Path, "data.db");
        var settings = new SqliteSettingsStore(database);
        Assert.Null(await settings.LoadAsync());

        var reader = new SqliteSynchronizedDataReader(database);
        Assert.Null(await reader.ReadAsync());
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "my-frame-synchronized-reader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
