using System.Security.Cryptography;
using System.Text.Json;

namespace MyFrame.Core;

/// <summary>F1 transport probe only. No publication, player normalization or raw output.</summary>
public sealed record CollectorCaptureProbe(int SchemaVersion, int GameId, string Source,
    Guid SessionId, Guid EventId, long Sequence, DateTimeOffset ReceivedAt, int Bytes,
    string Completeness, bool Publishable, bool PayloadRootObject);

public static class CollectorCaptureReader
{
    public const int MaximumEnvelopeBytes = 16 * 1024 * 1024;
    public const int MaximumPayloadBytes = 8 * 1024 * 1024;

    public static async Task<CollectorCaptureProbe> ReadAsync(string markerPath,
        CancellationToken cancellationToken = default)
    {
        var absolute = Path.GetFullPath(markerPath);
        var markerBytes = await ReadBoundedAsync(absolute, 4096, cancellationToken);
        try
        {
            using var marker = JsonDocument.Parse(markerBytes, new() { MaxDepth = 8 });
            var m = marker.RootElement;
            if (m.GetProperty("schemaVersion").GetInt32() != 1) throw Invalid();
            var fileName = m.GetProperty("fileName").GetString() ?? "";
            const string suffix = ".capture.json";
            if (!fileName.EndsWith(suffix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(fileName[..^suffix.Length], "D", out var eventId) ||
                Path.GetFileName(fileName) != fileName) throw Invalid();
            if (Path.GetFileName(absolute) != $"{eventId:D}.ready.json") throw Invalid();
            var expectedBytes = m.GetProperty("bytes").GetInt32();
            if (expectedBytes is <= 0 or > MaximumEnvelopeBytes) throw Invalid();
            var expectedHash = m.GetProperty("sha256").GetString();
            var capturePath = Path.Combine(Path.GetDirectoryName(absolute)!, fileName);
            var body = await ReadBoundedAsync(capturePath, MaximumEnvelopeBytes, cancellationToken);
            if (body.Length != expectedBytes || !string.Equals(Convert.ToHexString(SHA256.HashData(body)),
                    expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CAPTURE_INTEGRITY_FAILED");
            using var envelope = JsonDocument.Parse(body, new() { MaxDepth = 8 });
            var e = envelope.RootElement;
            if (e.GetProperty("schemaVersion").GetInt32() != 1 || e.GetProperty("gameId").GetInt32() != 8954 ||
                e.GetProperty("source").GetString() != "overwolf-native" ||
                e.GetProperty("kind").GetString() != "inventory" ||
                e.GetProperty("completeness").GetString() != "unverified" ||
                !Guid.TryParseExact(e.GetProperty("sessionId").GetString(), "D", out var sessionId) ||
                !Guid.TryParseExact(e.GetProperty("eventId").GetString(), "D", out var bodyEventId) ||
                eventId != bodyEventId || e.GetProperty("sequence").GetInt64() <= 0) throw Invalid();
            var encoding = e.GetProperty("encoding").GetString();
            if (encoding is not ("json-object" or "json-string" or "opaque-string")) throw Invalid();
            var payload = e.GetProperty("payload").GetString() ?? throw Invalid();
            if (System.Text.Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes) throw Invalid();
            var rootObject = false;
            if (encoding != "opaque-string")
            {
                using var parsed = JsonDocument.Parse(payload, new() { MaxDepth = 64 });
                rootObject = parsed.RootElement.ValueKind == JsonValueKind.Object;
            }
            return new(1, 8954, "overwolf-native", sessionId, eventId,
                e.GetProperty("sequence").GetInt64(), e.GetProperty("receivedAt").GetDateTimeOffset(),
                body.Length, "unverified", false, rootObject);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or
                                       InvalidOperationException or FormatException or OverflowException)
        {
            throw Invalid(); // Do not return source content or parser excerpts to logs/MCP.
        }
    }

    private static InvalidDataException Invalid() => new("CAPTURE_FORMAT_INVALID");

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximum,
        CancellationToken cancellationToken)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("CAPTURE_LINK_REJECTED");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length is <= 0 || file.Length > maximum) throw Invalid();
        var bytes = new byte[(int)file.Length];
        await file.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }
}
