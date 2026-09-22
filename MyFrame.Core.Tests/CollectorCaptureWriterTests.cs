using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class CollectorCaptureWriterTests
{
    [Fact]
    public void CreateWritesAnIntegrityProtectedUnverifiedSnapshot()
    {
        var eventId = Guid.Parse("d2719d99-f0f4-4225-a7d3-5c1cf68b269e");
        var sessionId = Guid.Parse("1d6fb5d0-522c-4938-a207-e1b5c6e1df50");
        var capture = CollectorCaptureWriter.Create("{\"Suits\":[{\"Name\":\"private-synthetic\"}]}",
            sessionId, 1, new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero), eventId);

        using var body = JsonDocument.Parse(capture.Body);
        using var marker = JsonDocument.Parse(capture.Marker);
        Assert.Equal("json-string", body.RootElement.GetProperty("encoding").GetString());
        Assert.Equal("unverified", body.RootElement.GetProperty("completeness").GetString());
        Assert.Equal($"{eventId:D}.capture.json", capture.FileName);
        Assert.Equal($"{eventId:D}.ready.json", capture.MarkerName);
        Assert.Equal(Encoding.UTF8.GetByteCount(capture.Body), marker.RootElement.GetProperty("bytes").GetInt32());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(capture.Body))).ToLowerInvariant(),
            marker.RootElement.GetProperty("sha256").GetString());
    }

    [Fact]
    public void CreatePreservesOpaquePayloadsWithoutParsingThem()
    {
        var capture = CollectorCaptureWriter.Create("private-synthetic-opaque", Guid.NewGuid(), 1,
            DateTimeOffset.UtcNow);

        using var body = JsonDocument.Parse(capture.Body);
        Assert.Equal("opaque-string", body.RootElement.GetProperty("encoding").GetString());
        Assert.Equal("private-synthetic-opaque", body.RootElement.GetProperty("payload").GetString());
    }

    [Fact]
    public void CreateRejectsOversizedPayloadsAndInvalidSequences()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CollectorCaptureWriter.Create("{}", Guid.NewGuid(), 0,
            DateTimeOffset.UtcNow));
        var oversized = new string('x', CollectorCaptureReader.MaximumPayloadBytes + 1);
        var error = Assert.Throws<ArgumentException>(() => CollectorCaptureWriter.Create(oversized, Guid.NewGuid(), 1,
            DateTimeOffset.UtcNow));
        Assert.StartsWith("PAYLOAD_LIMIT", error.Message, StringComparison.Ordinal);
    }
}
