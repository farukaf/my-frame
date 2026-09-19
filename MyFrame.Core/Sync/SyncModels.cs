namespace MyFrame.Core.Sync;

public sealed record SyncBatch(
    string SourceId,
    string ContentHash,
    string PayloadJson,
    int RecordCount,
    string ParserVersion = "1");

public sealed record SyncPublicationResult(
    string RunId,
    string RevisionId,
    bool AlreadyPublished,
    int RecordCount);

public sealed record SyncStatus(
    string SourceId,
    string? ActiveRevisionId,
    string? ParserVersion,
    string? ActiveContentHash,
    string? LastRunState,
    DateTimeOffset? LastRunAt,
    long AcceptedRecords,
    long RejectedRecords,
    string? ErrorCode = null);

public sealed record SyncRunSummary(
    string RunId,
    string SourceId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    long RecordsReceived,
    long RecordsAccepted,
    long RecordsRejected,
    string? ErrorCode);

public sealed record InventoryRevisionStatus(
    string CaptureMode,
    string Completeness,
    long Sequence);
