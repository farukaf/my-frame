using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyFrame.Core;

/// <summary>
/// Creates the immutable capture and ready-marker pair consumed by the local collector inbox.
/// The Overwolf WebApp remains responsible only for receiving native events and writing this pair.
/// </summary>
public sealed record CollectorCaptureWrite(string Body, string FileName, string MarkerName, string Marker);

public static class CollectorCaptureWriter
{
    public const int GameId = 8954;

    public static CollectorCaptureWrite Create(string payload, Guid sessionId, long sequence,
        DateTimeOffset receivedAt, Guid? eventId = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));

        var payloadBytes = Encoding.UTF8.GetByteCount(payload);
        if (payloadBytes > CollectorCaptureReader.MaximumPayloadBytes)
            throw new ArgumentException("PAYLOAD_LIMIT", nameof(payload));

        var id = eventId ?? Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            gameId = GameId,
            source = "overwolf-native",
            kind = "inventory",
            sessionId = sessionId.ToString("D"),
            sequence,
            eventId = id.ToString("D"),
            receivedAt = receivedAt.ToUniversalTime().ToString("O"),
            captureMode = "snapshot",
            completeness = "unverified",
            encoding = DetectEncoding(payload),
            payload
        });
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        if (bodyBytes.Length > CollectorCaptureReader.MaximumEnvelopeBytes)
            throw new ArgumentException("ENVELOPE_LIMIT", nameof(payload));

        var fileName = $"{id:D}.capture.json";
        var marker = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            fileName,
            bytes = bodyBytes.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant()
        });
        return new(body, fileName, $"{id:D}.ready.json", marker);
    }

    private static string DetectEncoding(string payload)
    {
        try
        {
            using var parsed = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 64 });
            return "json-string";
        }
        catch (JsonException)
        {
            return "opaque-string";
        }
    }
}
