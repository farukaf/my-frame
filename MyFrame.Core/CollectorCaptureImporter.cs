using MyFrame.Core.Sync;

namespace MyFrame.Core;

public sealed record CollectorImportResult(
    CollectorCaptureProbe Probe,
    SyncPublicationResult Publication,
    int EquipmentRecords,
    int StackableRecords,
    int UnknownRecords);

public static class CollectorCaptureImporter
{
    public static async Task<CollectorImportResult> ImportAsync(
        string markerPath,
        SyncDatabase database,
        bool allowRawPayload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (!allowRawPayload) throw new InvalidDataException("CAPTURE_CONSENT_REQUIRED");
        var probe = await CollectorCaptureReader.ReadAsync(markerPath, cancellationToken);
        if (!probe.PayloadRootObject) throw new InvalidDataException("CAPTURE_PAYLOAD_NOT_OBJECT");
        var envelope = await CollectorCaptureReader.ReadEnvelopeAsync(markerPath, cancellationToken);
        var projection = InventoryPayloadParser.Parse(envelope.PayloadJson);
        var publication = await database.PublishInventoryAsync(envelope, projection, cancellationToken);
        return new(probe, publication, projection.Equipment.Count, projection.Stackables.Count, projection.Unknown.Count);
    }
}
