using System.Text.Json;
using System.Diagnostics;

namespace MyFrame.Core;

public sealed record CollectorCaptureStatus(
    string State,
    bool DirectoryExists,
    bool OverwolfRunning,
    bool WarframeRunning,
    string? HeartbeatState,
    DateTimeOffset? HeartbeatTimestampUtc,
    bool HeartbeatFresh,
    int ReadyMarkers,
    int ValidMarkers,
    int InvalidMarkers,
    IReadOnlyDictionary<string, int> InvalidByCode,
    string? CollectorState = null,
    IReadOnlyList<string>? SupportedFeatures = null,
    IReadOnlyDictionary<string, int>? EventCounts = null,
    string? LastEventFeature = null,
    DateTimeOffset? LastEventAt = null);

/// <summary>Read-only diagnostics for the local Overwolf capture inbox.</summary>
public static class CollectorCaptureStatusProbe
{
    public static async Task<CollectorCaptureStatus> ReadAsync(string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var absoluteDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(absoluteDirectory))
            return new("missing", false, IsProcessRunning("Overwolf"), IsProcessRunning("Warframe.x64"), null, null, false, 0, 0, 0,
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

        var state = heartbeat.Fresh && valid > 0 ? "ready" :
            heartbeat.Fresh ? "heartbeat-only" :
            markers.Length > 0 ? "captures-only" : "idle";
        return new(state, true, IsProcessRunning("Overwolf"), IsProcessRunning("Warframe.x64"), heartbeat.State, heartbeat.TimestampUtc, heartbeat.Fresh, markers.Length, valid,
            markers.Length - valid, invalidByCode, heartbeat.CollectorState, heartbeat.SupportedFeatures,
            heartbeat.EventCounts, heartbeat.LastEventFeature, heartbeat.LastEventAt);
    }

    private static bool IsProcessRunning(string name)
    {
        try { return Process.GetProcessesByName(name).Length > 0; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static (string? State, DateTimeOffset? TimestampUtc, bool Fresh, string? CollectorState,
        IReadOnlyList<string> SupportedFeatures, IReadOnlyDictionary<string, int> EventCounts,
        string? LastEventFeature, DateTimeOffset? LastEventAt) ReadHeartbeat(string path)
    {
        if (!File.Exists(path)) return (null, null, false, null, [], new Dictionary<string, int>(), null, null);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path), new() { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("kind").GetString() != "my-frame-collector") return ("invalid", null, false, null, [], new Dictionary<string, int>(), null, null);
            var state = root.GetProperty("state").GetString();
            var parsed = DateTimeOffset.TryParse(root.GetProperty("timestampUtc").GetString(), out var timestamp);
            var collectorState = root.TryGetProperty("collectorState", out var collector) && collector.ValueKind == JsonValueKind.String ? collector.GetString() : null;
            var features = root.TryGetProperty("supportedFeatures", out var featureValue) && featureValue.ValueKind == JsonValueKind.Array
                ? featureValue.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).Take(16).ToArray()
                : [];
            var eventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (root.TryGetProperty("eventCounts", out var counts) && counts.ValueKind == JsonValueKind.Object)
                foreach (var property in counts.EnumerateObject())
                    if (property.Value.TryGetInt32(out var count) && count >= 0 && count <= 1000000) eventCounts[property.Name] = count;
            var lastFeature = root.TryGetProperty("lastEventFeature", out var last) && last.ValueKind == JsonValueKind.String ? last.GetString() : null;
            var lastEventAt = root.TryGetProperty("lastEventAt", out var lastAt) && lastAt.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(lastAt.GetString(), out var parsedLast) ? parsedLast : (DateTimeOffset?)null;
            return (state, parsed ? timestamp : null,
                state is "started" && parsed && DateTimeOffset.UtcNow - timestamp < TimeSpan.FromHours(1),
                collectorState, features, eventCounts, lastFeature, lastEventAt);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or
                                       KeyNotFoundException or UnauthorizedAccessException)
        {
            return ("invalid", null, false, null, [], new Dictionary<string, int>(), null, null);
        }
    }
}
