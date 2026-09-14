using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    public async Task<PublicExportSyncOutcome> RunDirectoryAsync(
        SyncDatabase database,
        SyncHost host,
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(directoryPath)) throw new ArgumentException("Public Export directory is required.", nameof(directoryPath));
        try
        {
            var fullPath = Path.GetFullPath(directoryPath);
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("PUBLIC_EXPORT_DIRECTORY_NOT_FOUND");
            var files = Directory.EnumerateFiles(fullPath, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length == 0) throw new InvalidDataException("PUBLIC_EXPORT_DIRECTORY_EMPTY");
            var recordsByUniqueName = new Dictionary<string, PublicExportRecord>(StringComparer.Ordinal);
            var rawParts = new List<string>();
            long totalBytes = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(file);
                totalBytes += info.Length;
                if (totalBytes > 256 * 1024 * 1024) throw new InvalidDataException("PUBLIC_EXPORT_DIRECTORY_TOO_LARGE");
                var json = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken);
                var parsed = PublicExportDocumentParser.Parse(json);
                foreach (var record in parsed)
                {
                    if (recordsByUniqueName.TryGetValue(record.UniqueName, out var existing))
                    {
                        if (!string.Equals(existing.RawJson, record.RawJson, StringComparison.Ordinal))
                            throw new InvalidDataException("PUBLIC_EXPORT_DUPLICATE_UNIQUE_NAME");
                        continue;
                    }
                    recordsByUniqueName.Add(record.UniqueName, record);
                    rawParts.Add(record.RawJson ?? JsonSerializer.Serialize(new { uniqueName = record.UniqueName, name = record.Name, category = record.Category, description = record.Description }));
                }
            }
            var payload = $"[{string.Join(',', rawParts)}]";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            var batch = new SyncBatch("public-export", hash, payload, recordsByUniqueName.Count, "public-export-directory-1");
            var records = recordsByUniqueName.Values.ToArray();
            var publication = await host.RunCatalogOnceAsync("public-export", _ =>
                Task.FromResult<(SyncBatch, IReadOnlyList<PublicExportRecord>)>((batch, records)), cancellationToken);
            var status = await database.GetStatusAsync("public-export", cancellationToken);
            return publication is not null
                ? new("published", publication.RecordCount, publication.RevisionId, null, status?.ParserVersion, Path.GetFileName(fullPath))
                : new(status?.LastRunState ?? "failed", 0, status?.ActiveRevisionId,
                    status?.ErrorCode ?? "SYNC_FAILED", status?.ParserVersion, Path.GetFileName(fullPath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var status = await database.GetStatusAsync("public-export", cancellationToken);
            return new("failed", 0, status?.ActiveRevisionId, ErrorCode(error), status?.ParserVersion, Path.GetFileName(directoryPath));
        }
    }

    public async Task<PublicExportSyncOutcome> RunFileAsync(
        SyncDatabase database,
        SyncHost host,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Public Export file is required.", nameof(filePath));

        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var info = new FileInfo(fullPath);
            if (!info.Exists) throw new FileNotFoundException("PUBLIC_EXPORT_FILE_NOT_FOUND", fullPath);
            if (info.Length is <= 0 or > 64 * 1024 * 1024) throw new InvalidDataException("PUBLIC_EXPORT_DOCUMENT_TOO_LARGE");
            var json = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);
            var records = PublicExportDocumentParser.Parse(json);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            var batch = new SyncBatch("public-export", hash, json, records.Count, "public-export-file-1");
            var publication = await host.RunCatalogOnceAsync("public-export", _ =>
                Task.FromResult<(SyncBatch, IReadOnlyList<PublicExportRecord>)>((batch, records)), cancellationToken);
            var status = await database.GetStatusAsync("public-export", cancellationToken);
            return publication is not null
                ? new("published", publication.RecordCount, publication.RevisionId, null, status?.ParserVersion, info.Name)
                : new(status?.LastRunState ?? "failed", 0, status?.ActiveRevisionId,
                    status?.ErrorCode ?? "SYNC_FAILED", status?.ParserVersion, info.Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var status = await database.GetStatusAsync("public-export", cancellationToken);
            return new("failed", 0, status?.ActiveRevisionId, ErrorCode(error), status?.ParserVersion, Path.GetFileName(filePath));
        }
    }

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
            var catalogEntries = entries
                .Where(value => value.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (catalogEntries.Length > 128) throw new InvalidDataException("PUBLIC_EXPORT_TOO_MANY_DOCUMENTS");
            if (catalogEntries.Length == 0) throw new InvalidDataException("PUBLIC_EXPORT_ENTRY_NOT_FOUND");
            entry = catalogEntries[0];

            var recordsByUniqueName = new Dictionary<string, PublicExportRecord>(StringComparer.Ordinal);
            var rawParts = new List<string>();
            foreach (var candidate in catalogEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var publication = await documentClient.FetchPublicationAsync(candidate, "public-export", cancellationToken: cancellationToken);
                foreach (var record in publication.Records)
                {
                    if (recordsByUniqueName.TryGetValue(record.UniqueName, out var existing))
                    {
                        if (!string.Equals(existing.RawJson, record.RawJson, StringComparison.Ordinal))
                            throw new InvalidDataException("PUBLIC_EXPORT_DUPLICATE_UNIQUE_NAME");
                        continue;
                    }

                    recordsByUniqueName.Add(record.UniqueName, record);
                    rawParts.Add(record.RawJson ?? JsonSerializer.Serialize(new
                    {
                        uniqueName = record.UniqueName,
                        name = record.Name,
                        category = record.Category,
                        description = record.Description
                    }));
                }
            }

            var payload = $"[{string.Join(',', rawParts)}]";
            if (Encoding.UTF8.GetByteCount(payload) > 256 * 1024 * 1024)
                throw new InvalidDataException("PUBLIC_EXPORT_CATALOG_TOO_LARGE");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            var batch = new SyncBatch("public-export", hash, payload, recordsByUniqueName.Count,
                catalogEntries.Length == 1 ? "public-export-1" : "public-export-aggregate-1");
            var records = recordsByUniqueName.Values.ToArray();
            var publicationResult = await host.RunCatalogOnceAsync("public-export", _ =>
                Task.FromResult<(SyncBatch, IReadOnlyList<PublicExportRecord>)>((batch, records)), cancellationToken);
            var status = await database.GetStatusAsync("public-export", cancellationToken);
            var sourcePath = catalogEntries.Length == 1 ? catalogEntries[0].RelativePath : $"aggregate:{catalogEntries.Length}";
            var sourceTag = catalogEntries.Length == 1 ? catalogEntries[0].RevisionTag : null;
            return publicationResult is not null
                ? new("published", publicationResult.RecordCount, publicationResult.RevisionId, null, status?.ParserVersion, sourcePath, sourceTag)
                : new(status?.LastRunState ?? "failed", 0, status?.ActiveRevisionId,
                    status?.ErrorCode ?? "SYNC_FAILED", status?.ParserVersion, sourcePath, sourceTag);
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
        HttpRequestException => "PUBLIC_EXPORT_NETWORK_UNAVAILABLE",
        NotSupportedException unsupported when unsupported.Message.Contains("LZMA", StringComparison.OrdinalIgnoreCase) => "PUBLIC_EXPORT_LZMA_DECODER_NOT_CONFIGURED",
        _ => "SYNC_FAILED"
    };
}
