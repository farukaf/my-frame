using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyFrame.Core.Sync;

namespace MyFrame.Core.Tests;

public sealed class CollectorCaptureInboxTests
{
    [Fact]
    public async Task ScansInboxImportsValidCaptureAndIsIdempotent()
    {
        using var folder = new TemporaryFolder();
        await WriteCaptureAsync(folder.Path, valid: true);
        var databasePath = Path.Combine(folder.Path, "data.db");
        await using var database = new SyncDatabase(databasePath);
        await database.PublishCatalogAsync(
            new SyncBatch("public-export", "catalog", "[]", 1),
            [new PublicExportRecord("/Lotus/Resource", "Resource", "Resource", null, new Dictionary<string, string>())]);

        var first = await CollectorCaptureInbox.ImportAsync(folder.Path, database, true);
        var second = await CollectorCaptureInbox.ImportAsync(folder.Path, database, true);

        Assert.Equal(1, first.Discovered);
        Assert.Equal(1, first.Imported);
        Assert.Equal(0, first.AlreadyPublished);
        Assert.Equal("imported", Assert.Single(first.Items).State);
        Assert.Equal(1, second.AlreadyPublished);
        Assert.Equal("alreadyPublished", Assert.Single(second.Items).State);
        var synchronized = await new SqliteSynchronizedDataReader(databasePath).ReadAsync();
        Assert.NotNull(synchronized);
        Assert.Equal(4, synchronized!.Inventory.Stackables["/Lotus/Resource"]);
    }

    [Fact]
    public async Task RejectsBadCaptureWithoutStoppingOtherMarkers()
    {
        using var folder = new TemporaryFolder();
        await WriteCaptureAsync(folder.Path, valid: true, eventId: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        await WriteCaptureAsync(folder.Path, valid: false, eventId: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await using var database = new SyncDatabase(Path.Combine(folder.Path, "data.db"));

        var result = await CollectorCaptureInbox.ImportAsync(folder.Path, database, true);

        Assert.Equal(2, result.Discovered);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Rejected);
        Assert.Contains(result.Items, item => item.State == "rejected" && item.ErrorCode == "CAPTURE_INTEGRITY_FAILED");
        var status = await database.GetStatusAsync("overwolf-inventory");
        Assert.Equal("failed", status?.LastRunState);
        Assert.Equal("CAPTURE_INTEGRITY_FAILED", status?.ErrorCode);
    }

    [Fact]
    public async Task RequiresExplicitConsentBeforeScanning()
    {
        using var folder = new TemporaryFolder();
        await using var database = new SyncDatabase(Path.Combine(folder.Path, "data.db"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CollectorCaptureInbox.ImportAsync(folder.Path, database, false));

        Assert.Equal("CAPTURE_CONSENT_REQUIRED", error.Message);
    }

    private static async Task WriteCaptureAsync(string directory, bool valid, Guid? eventId = null)
    {
        var id = eventId ?? Guid.NewGuid();
        var payload = "{\"stackables\":[{\"typeId\":\"/Lotus/Resource\",\"quantity\":4}]}";
        var envelope = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, gameId = 8954, source = "overwolf-native", kind = "inventory",
            sessionId = Guid.NewGuid(), eventId = id, sequence = 1L, receivedAt = DateTimeOffset.UtcNow,
            providerVersion = "native", completeness = "unverified", encoding = "json-object", payload
        });
        var captureName = $"{id:D}.capture.json";
        var capturePath = Path.Combine(directory, captureName);
        await File.WriteAllTextAsync(capturePath, envelope, new UTF8Encoding(false));
        var bytes = await File.ReadAllBytesAsync(capturePath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!valid) hash = new string('0', hash.Length);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{id:D}.ready.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, fileName = captureName, bytes = bytes.Length, sha256 = hash
        }), new UTF8Encoding(false));
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "my-frame-inbox-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
