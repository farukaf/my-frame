using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyFrame.Core.Sync;

namespace MyFrame.Core.Tests;

public sealed class CollectorCaptureImporterTests
{
    [Fact]
    public async Task RequiresConsentAndPublishesValidatedEnvelopeIdempotently()
    {
        using var folder = new TemporaryFolder();
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var payload = "{\"equipment\":[{\"instanceId\":\"instance\",\"typeId\":\"/Lotus/Weapon\",\"rank\":30}],\"stackables\":[{\"typeId\":\"/Lotus/Resource\",\"quantity\":4}]}";
        var envelope = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            gameId = 8954,
            source = "overwolf-native",
            kind = "inventory",
            sessionId,
            eventId,
            sequence = 1L,
            receivedAt = DateTimeOffset.UtcNow,
            providerVersion = "native",
            completeness = "unverified",
            encoding = "json-object",
            payload
        });
        var captureName = $"{eventId:D}.capture.json";
        var capturePath = Path.Combine(folder.Path, captureName);
        await File.WriteAllTextAsync(capturePath, envelope, Encoding.UTF8);
        var body = await File.ReadAllBytesAsync(capturePath);
        var markerPath = Path.Combine(folder.Path, $"{eventId:D}.ready.json");
        await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            fileName = captureName,
            bytes = body.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(body))
        }), new UTF8Encoding(false));
        var databasePath = Path.Combine(folder.Path, "data.db");
        await using var database = new SyncDatabase(databasePath);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CollectorCaptureImporter.ImportAsync(markerPath, database, false));
        var first = await CollectorCaptureImporter.ImportAsync(markerPath, database, true);
        var second = await CollectorCaptureImporter.ImportAsync(markerPath, database, true);

        Assert.Equal(1, first.EquipmentRecords);
        Assert.Equal(1, first.StackableRecords);
        Assert.False(first.Publication.AlreadyPublished);
        Assert.True(second.Publication.AlreadyPublished);
        Assert.Single(await database.GetInventoryEquipmentAsync());
        Assert.Single(await database.GetInventoryStackablesAsync());
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "my-frame-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
