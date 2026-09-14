using System.Text.Json;

namespace MyFrame.Core;

public sealed record CollectorCaptureStatus(
    string State,
    bool DirectoryExists,
    string? HeartbeatState,
    DateTimeOffset? HeartbeatTimestampUtc,
    int ReadyMarkers,
    int ValidMarkers,
    int InvalidMarkers,
    IReadOnlyDictionary<string, int> InvalidByCode);

/// <summary>Read-only diagnostics for the local Overwolf capture inbox.</summary>
public static class CollectorCaptureStatusProbe
{
    public static async Task<CollectorCaptureStatus> ReadAsync(string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var absoluteDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(absoluteDirectory))
            return new("missing", false, null, null, 0, 0, 0,
                new Dictionary<string, int>(StringComparer.Ordinal));

        var heartbeat = ReadHeartbeat(Path.Combine(absoluteDirectory, "collector-status.json"));
        var markers = Directory.EnumerateFiles(absoluteDirectory, "*.ready.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var invalidByCode = new Dictionary<string, int>(StringComparer.Ordinal);
        var valid = 0;
        foreach (var marker in markers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await CollectorCaptureReader.ReadAsync(marker, cancellationToken).ConfigureAwait(false);
                valid++;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                var code = error.Message is { Length: > 0 } message && message.Length <= 64
                    ? message : "CAPTURE_INVALID";
                invalidByCode[code] = invalidByCode.GetValueOrDefault(code) + 1;
            }
        }

        var state = heartbeat.State is "started" && valid > 0 ? "ready" :
            heartbeat.State is "started" ? "heartbeat-only" :
            markers.Length > 0 ? "captures-only" : "idle";
        return new(state, true, heartbeat.State, heartbeat.TimestampUtc, markers.Length, valid,
            markers.Length - valid, invalidByCode);
    }

    private static (string? State, DateTimeOffset? TimestampUtc) ReadHeartbeat(string path)
    {
        if (!File.Exists(path)) return (null, null);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path), new() { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("kind").GetString() != "my-frame-collector") return ("invalid", null);
            var state = root.GetProperty("state").GetString();
            return (state, DateTimeOffset.TryParse(root.GetProperty("timestampUtc").GetString(), out var timestamp)
                ? timestamp : null);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or
                                       KeyNotFoundException or UnauthorizedAccessException)
        {
            return ("invalid", null);
        }
    }
}
