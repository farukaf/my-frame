using MyFrame.Core.Sync;

namespace MyFrame.Core;

public sealed record CollectorCaptureInboxItem(
    string MarkerFileName,
    string State,
    string? ErrorCode,
    int EquipmentRecords,
    int StackableRecords,
    int UnknownRecords);

public sealed record CollectorCaptureInboxResult(
    int Discovered,
    int Imported,
    int AlreadyPublished,
    int Rejected,
    IReadOnlyList<CollectorCaptureInboxItem> Items);

/// <summary>Imports validated Overwolf captures from a local inbox without deleting or moving files.</summary>
public static class CollectorCaptureInbox
{
    public static async Task<CollectorCaptureInboxResult> ImportAsync(
        string directory,
        SyncDatabase database,
        bool allowRawPayload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(database);
        if (!allowRawPayload) throw new InvalidDataException("CAPTURE_CONSENT_REQUIRED");

        var absoluteDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(absoluteDirectory))
            return new(0, 0, 0, 0, []);

        var markers = Directory.EnumerateFiles(absoluteDirectory, "*.ready.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var items = new List<CollectorCaptureInboxItem>(markers.Length);
        var imported = 0;
        var alreadyPublished = 0;
        var rejected = 0;
        foreach (var markerFileName in markers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var markerPath = Path.Combine(absoluteDirectory, markerFileName);
            try
            {
                var result = await CollectorCaptureImporter.ImportAsync(markerPath, database, true, cancellationToken)
                    .ConfigureAwait(false);
                if (result.Publication.AlreadyPublished)
                {
                    alreadyPublished++;
                    items.Add(new(markerFileName, "alreadyPublished", null,
                        result.EquipmentRecords, result.StackableRecords, result.UnknownRecords));
                }
                else
                {
                    imported++;
                    items.Add(new(markerFileName, "imported", null,
                        result.EquipmentRecords, result.StackableRecords, result.UnknownRecords));
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                                          UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                rejected++;
                var errorCode = SafeErrorCode(error);
                try
                {
                    await database.RecordFailureAsync("overwolf-inventory", errorCode, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception persistenceError) when (persistenceError is IOException or InvalidOperationException or
                                                         UnauthorizedAccessException)
                {
                    // The capture remains rejected even if status persistence is temporarily unavailable.
                }
                items.Add(new(markerFileName, "rejected", errorCode, 0, 0, 0));
            }
        }

        return new(markers.Length, imported, alreadyPublished, rejected, items);
    }

    private static string SafeErrorCode(Exception error) => error.Message switch
    {
        "CAPTURE_CONSENT_REQUIRED" => "CAPTURE_CONSENT_REQUIRED",
        "CAPTURE_INTEGRITY_FAILED" => "CAPTURE_INTEGRITY_FAILED",
        "CAPTURE_LINK_REJECTED" => "CAPTURE_LINK_REJECTED",
        "CAPTURE_PAYLOAD_NOT_OBJECT" => "CAPTURE_PAYLOAD_NOT_OBJECT",
        "CAPTURE_FORMAT_INVALID" => "CAPTURE_FORMAT_INVALID",
        _ => "CAPTURE_REJECTED"
    };
}
