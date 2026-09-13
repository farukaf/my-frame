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
    string? ActiveContentHash,
    string? LastRunState,
    DateTimeOffset? LastRunAt,
    long AcceptedRecords,
    long RejectedRecords);
