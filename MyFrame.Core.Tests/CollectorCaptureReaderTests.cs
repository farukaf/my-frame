using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class CollectorCaptureReaderTests
{
    [Fact]
    public async Task ValidCaptureIsInspectedWithoutPublishingOrReturningPrivatePayload()
    {
        using var directory = new TemporaryDirectory();
        var marker = await WriteAsync(directory.Path);
        var result = await CollectorCaptureReader.ReadAsync(marker);
        Assert.True(result.PayloadRootObject);
        Assert.False(result.Publishable);
        Assert.Equal("unverified", result.Completeness);
        Assert.DoesNotContain("private-synthetic", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("gameId", 5426)]
    [InlineData("schemaVersion", 2)]
    [InlineData("sequence", 0)]
    [InlineData("sequence", -1)]
    [InlineData("source", "alecaframe")]
    [InlineData("kind", "chat")]
    [InlineData("completeness", "complete")]
    [InlineData("sessionId", "not-a-session")]
    [InlineData("eventId", "00000000-0000-0000-0000-000000000001")]
    [InlineData("receivedAt", "invalid")]
    [InlineData("encoding", "unknown")]
    public async Task UnsupportedOrInvalidEnvelopeIsRejected(string key, object value)
    {
        using var directory = new TemporaryDirectory();
        var marker = await WriteAsync(directory.Path, values => values[key] = value);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => CollectorCaptureReader.ReadAsync(marker));
        Assert.Equal("CAPTURE_FORMAT_INVALID", error.Message);
    }

    [Theory]
    [InlineData("fileName", "../outside.capture.json")]
    [InlineData("fileName", "C:\\outside.capture.json")]
    [InlineData("bytes", 0)]
    [InlineData("bytes", 16777217)]
    [InlineData("schemaVersion", 3)]
    public async Task UnsafeOrInvalidMarkerIsRejected(string key, object value)
    {
        using var directory = new TemporaryDirectory();
        var marker = await WriteAsync(directory.Path, markerChange: values => values[key] = value);
        await Assert.ThrowsAsync<InvalidDataException>(() => CollectorCaptureReader.ReadAsync(marker));
    }

    [Fact]
    public async Task TamperedHashOrIncompleteBodyCannotPassReadinessCheck()
    {
        using var directory = new TemporaryDirectory();
        var marker = await WriteAsync(directory.Path, markerChange: values => values["sha256"] = new string('0', 64));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => CollectorCaptureReader.ReadAsync(marker));
        Assert.Equal("CAPTURE_INTEGRITY_FAILED", error.Message);
        marker = await WriteAsync(directory.Path, markerChange: values => values["bytes"] = 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => CollectorCaptureReader.ReadAsync(marker));
    }

    [Fact]
    public async Task MarkerWithoutBodyAndMalformedJsonFailWithoutLeakingPayload()
    {
        using var directory = new TemporaryDirectory();
        var marker = await WriteAsync(directory.Path);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(marker));
        File.Delete(Path.Combine(directory.Path, json.RootElement.GetProperty("fileName").GetString()!));
        await Assert.ThrowsAsync<FileNotFoundException>(() => CollectorCaptureReader.ReadAsync(marker));
        await File.WriteAllTextAsync(marker, "{private-synthetic");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => CollectorCaptureReader.ReadAsync(marker));
        Assert.DoesNotContain("private-synthetic", error.Message);
    }

    [Fact]
    public async Task OpaquePayloadIsNotDecodedOrClaimedAsInventoryObject()
    {
        using var directory = new TemporaryDirectory();
        var marker = await WriteAsync(directory.Path, values => {
            values["encoding"] = "opaque-string"; values["payload"] = "private-synthetic-encoded";
        });
        var result = await CollectorCaptureReader.ReadAsync(marker);
        Assert.False(result.PayloadRootObject);
        Assert.False(result.Publishable);
    }

    private static async Task<string> WriteAsync(string directory,
        Action<Dictionary<string, object>>? envelopeChange = null,
        Action<Dictionary<string, object>>? markerChange = null)
    {
        var id = Guid.NewGuid();
        var envelope = new Dictionary<string, object> {
            ["schemaVersion"] = 1, ["gameId"] = 8954, ["source"] = "overwolf-native",
            ["kind"] = "inventory", ["sessionId"] = Guid.NewGuid().ToString("D"),
            ["eventId"] = id.ToString("D"), ["sequence"] = 1,
            ["receivedAt"] = "2026-09-12T12:00:00Z", ["completeness"] = "unverified",
            ["encoding"] = "json-object", ["payload"] = "{\"Suits\":[{\"Name\":\"private-synthetic-value\"}]}"
        };
        envelopeChange?.Invoke(envelope);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        var fileName = $"{id:D}.capture.json";
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
        var marker = new Dictionary<string, object> { ["schemaVersion"] = 1,
            ["fileName"] = fileName, ["bytes"] = bytes.Length,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
        markerChange?.Invoke(marker);
        var path = Path.Combine(directory, $"{id:D}.ready.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(marker));
        return path;
    }
}
