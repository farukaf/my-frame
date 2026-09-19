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
