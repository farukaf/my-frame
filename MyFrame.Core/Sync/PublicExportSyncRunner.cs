namespace MyFrame.Core.Sync;

public sealed record PublicExportSyncOutcome(
    string State,
    int Records,
    string? RevisionId,
    string? ErrorCode,
    string? ParserVersion,
    string? RelativePath = null,
    string? RevisionTag = null);

/// <summary>Shared orchestration boundary for UI, CLI and future scheduled syncs.</summary>
public sealed class PublicExportSyncRunner
{
    public async Task<PublicExportSyncOutcome> RunAsync(
        SyncDatabase database,
        SyncHost host,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(httpClient);

        PublicExportIndexEntry? entry = null;
        try
        {
            var decoder = new LzmaAloneDecoder();
            var indexClient = new PublicExportIndexClient(httpClient, decoder.Decode);
            var documentClient = new PublicExportDocumentClient(httpClient, decoder);
            var entries = await indexClient.FetchIndexAsync(cancellationToken: cancellationToken);
            entry = entries.FirstOrDefault(value =>
                value.RelativePath.Contains("ExportWeapons_en.json", StringComparison.OrdinalIgnoreCase))
                ?? entries.FirstOrDefault(value => value.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            if (entry is null) throw new InvalidDataException("PUBLIC_EXPORT_ENTRY_NOT_FOUND");

            var publication = await host.RunPublicExportOnceAsync("public-export", documentClient, entry,
                cancellationToken: cancellationToken);
            var status = await database.GetStatusAsync("public-export", cancellationToken);
            return publication is not null
                ? new("published", publication.RecordCount, publication.RevisionId, null, status?.ParserVersion, entry.RelativePath, entry.RevisionTag)
                : new(status?.LastRunState ?? "failed", 0, status?.ActiveRevisionId,
                    status?.ErrorCode ?? "SYNC_FAILED", status?.ParserVersion, entry.RelativePath, entry.RevisionTag);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var status = await database.GetStatusAsync("public-export", cancellationToken);
            return new("failed", 0, status?.ActiveRevisionId,
                ErrorCode(error), status?.ParserVersion, entry?.RelativePath, entry?.RevisionTag);
        }
    }

    private static string ErrorCode(Exception error) => error switch
    {
        InvalidDataException data when !string.IsNullOrWhiteSpace(data.Message) => data.Message,
        HttpRequestException request when request.Message.StartsWith("PUBLIC_EXPORT_HTTP_", StringComparison.Ordinal) => request.Message,
        NotSupportedException unsupported when unsupported.Message.Contains("LZMA", StringComparison.OrdinalIgnoreCase) => "PUBLIC_EXPORT_LZMA_DECODER_NOT_CONFIGURED",
        _ => "SYNC_FAILED"
    };
}
