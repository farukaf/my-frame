using System.Security.Cryptography;
using System.Text;

namespace MyFrame.Core.Sync;

public sealed record ReferenceImportResult(
    ReferenceDocument Document,
    string StoredFile,
    bool AlreadyImported);

public static class ReferenceImporter
{
    public static async Task<ReferenceImportResult> ImportAsync(
        string sourcePath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("Reference file is required.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationDirectory)) throw new ArgumentException("Reference destination is required.", nameof(destinationDirectory));
        var fullSource = Path.GetFullPath(sourcePath);
        var info = new FileInfo(fullSource);
        if (!info.Exists) throw new FileNotFoundException("REFERENCE_FILE_NOT_FOUND", fullSource);
        if (info.Length <= 0 || info.Length > ReferenceDocumentParser.MaximumDocumentBytes)
            throw new InvalidDataException("REFERENCE_DOCUMENT_TOO_LARGE");
        var bytes = await File.ReadAllBytesAsync(fullSource, cancellationToken);
        return await ImportBytesAsync(bytes, destinationDirectory, cancellationToken);
    }

    public static async Task<ReferenceImportResult> ImportBytesAsync(
        byte[] bytes,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (bytes is null || bytes.Length <= 0 || bytes.Length > ReferenceDocumentParser.MaximumDocumentBytes)
            throw new InvalidDataException("REFERENCE_DOCUMENT_TOO_LARGE");
        if (string.IsNullOrWhiteSpace(destinationDirectory)) throw new ArgumentException("Reference destination is required.", nameof(destinationDirectory));
        var json = new UTF8Encoding(false, true).GetString(bytes);
        var document = ReferenceDocumentParser.Parse(json, DateTimeOffset.UtcNow);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var directory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(directory);
        var stored = Path.Combine(directory, $"{hash}.json");
        if (File.Exists(stored)) return new(document, stored, true);
        var temporary = Path.Combine(directory, $".{hash}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, stored);
            return new(document, stored, false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class ReferenceSyncRunner
{
    public async Task<ReferenceImportResult> FetchAsync(
        HttpClient client,
        Uri sourceUri,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(sourceUri);
        if (!ReferenceDocumentParser.IsAllowedSourceUri(sourceUri))
            throw new InvalidDataException("REFERENCE_URL_UNSUPPORTED");

        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("MyFrame.Sync/1.0");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > ReferenceDocumentParser.MaximumDocumentBytes)
            throw new InvalidDataException("REFERENCE_DOCUMENT_TOO_LARGE");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var buffer = new MemoryStream();
        var chunk = new byte[32 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > ReferenceDocumentParser.MaximumDocumentBytes)
                throw new InvalidDataException("REFERENCE_DOCUMENT_TOO_LARGE");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return await ReferenceImporter.ImportBytesAsync(buffer.ToArray(), destinationDirectory, cancellationToken);
    }
}
