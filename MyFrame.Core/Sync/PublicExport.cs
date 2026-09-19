using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace MyFrame.Core.Sync;

public sealed record PublicExportIndexEntry(string RelativePath, string? RevisionTag);

public sealed record PublicExportRecord(
    string UniqueName,
    string? Name,
    string? Category,
    string? Description,
    IReadOnlyDictionary<string, string> Aliases,
    string? RawJson = null);

public static class PublicExportIndexParser
{
    private static readonly Regex Sha256 = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<PublicExportIndexEntry> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new List<PublicExportIndexEntry>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length is < 1 or > 2) throw new InvalidDataException("PUBLIC_EXPORT_INDEX_LINE_INVALID");
            var path = fields[0];
            string? tag = null;
            if (fields.Length == 1 && path.LastIndexOf('!') is var separator && separator > 0)
            {
                tag = path[(separator + 1)..];
                path = path[..separator];
                if (tag.Length == 0 || tag.Any(character => !(char.IsLetterOrDigit(character) || "+/_-".Contains(character))))
                    throw new InvalidDataException("PUBLIC_EXPORT_INDEX_TAG_INVALID");
            }
            else
            {
                path = Sha256.IsMatch(fields[0]) && fields.Length == 2 ? fields[1] : fields[0];
                tag = Sha256.IsMatch(fields[0]) && fields.Length == 2 ? fields[0].ToLowerInvariant() : fields.Length == 2 && Sha256.IsMatch(fields[1]) ? fields[1].ToLowerInvariant() : null;
            }
            path = NormalizePath(path);
            entries.Add(new PublicExportIndexEntry(path, tag));
        }
        return entries;
    }

    private static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/').TrimStart('/');
        if (path.Length == 0 || path.Contains("../", StringComparison.Ordinal) || path == ".." || Path.IsPathRooted(path) || path.Contains(':'))
            throw new InvalidDataException("PUBLIC_EXPORT_PATH_INVALID");
        return path;
    }
}

public static class PublicExportDocumentParser
{
    public static IReadOnlyList<PublicExportRecord> Parse(string json, int maximumRecords = 100_000)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("PUBLIC_EXPORT_EMPTY");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("PUBLIC_EXPORT_ROOT_INVALID");
        var records = new List<PublicExportRecord>();
        foreach (var item in root.EnumerateArray())
        {
            if (records.Count >= maximumRecords) throw new InvalidDataException("PUBLIC_EXPORT_TOO_LARGE");
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("uniqueName", out var unique) || unique.ValueKind != JsonValueKind.String)
                continue;
            var uniqueName = unique.GetString();
            if (string.IsNullOrWhiteSpace(uniqueName)) continue;
            var aliases = LocalizedProperties(item, "name");
            foreach (var alias in LocalizedProperties(item, "names"))
                aliases[alias.Key] = alias.Value;
            var name = aliases.GetValueOrDefault("en") ?? StringProperty(item, "name") ?? aliases.Values.FirstOrDefault();
            var category = StringProperty(item, "category");
            var description = StringProperty(item, "description");
            aliases.TryAdd("en", name ?? uniqueName);
            records.Add(new PublicExportRecord(uniqueName, name, category, description, aliases, item.GetRawText()));
        }
        return records;
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Dictionary<string, string> LocalizedProperties(JsonElement element, string name)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty(name, out var value)) return aliases;
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text)) aliases["en"] = text;
            return aliases;
        }
        if (value.ValueKind != JsonValueKind.Object) return aliases;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var text = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(text) || property.Name.Length is < 2 or > 12) continue;
            aliases[property.Name] = text;
        }
        return aliases;
    }
}

public static class PublicExportIdentity
{
    public static string Canonicalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        return string.Join(' ', builder.ToString().Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool Equivalent(string left, string right) =>
        string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.Ordinal);
}

public sealed class PublicExportIndexClient(HttpClient httpClient, Func<byte[], string>? decoder = null)
{
    public const string DefaultIndexUrl = "https://origin.warframe.com/PublicExport/index_en.txt.lzma";
    public async Task<IReadOnlyList<PublicExportIndexEntry>> FetchIndexAsync(Uri? uri = null, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(uri ?? new Uri(DefaultIndexUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"PUBLIC_EXPORT_HTTP_{(int)response.StatusCode}");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("PUBLIC_EXPORT_INDEX_TOO_LARGE");
        if (decoder is null) throw new NotSupportedException("PUBLIC_EXPORT_LZMA_DECODER_NOT_CONFIGURED");
        var text = decoder(bytes);
        return PublicExportIndexParser.Parse(text);
    }
}

public sealed class PublicExportDocumentClient(HttpClient httpClient, LzmaAloneDecoder? lzmaDecoder = null)
{
    // The index is served by origin.warframe.com, while the JSON documents are
    // served from the content host. Keeping these endpoints separate avoids a
    // 403 from the origin host when resolving an index entry.
    public const string DefaultBaseUrl = "https://content.warframe.com/PublicExport/";
    private readonly LzmaAloneDecoder _lzmaDecoder = lzmaDecoder ?? new();

    public async Task<SyncBatch> FetchBatchAsync(PublicExportIndexEntry entry, string sourceId = "public-export", Uri? baseUri = null, CancellationToken cancellationToken = default)
    {
        var publication = await FetchPublicationAsync(entry, sourceId, baseUri, cancellationToken);
        return publication.Batch;
    }

    public async Task<(SyncBatch Batch, IReadOnlyList<PublicExportRecord> Records)> FetchPublicationAsync(
        PublicExportIndexEntry entry, string sourceId = "public-export", Uri? baseUri = null,
        CancellationToken cancellationToken = default)
    {
        var path = entry.RelativePath;
        if (path.EndsWith(".lzma", StringComparison.OrdinalIgnoreCase)) path = path[..^5];
        var uri = new Uri((baseUri ?? new Uri(DefaultBaseUrl)), path);
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"PUBLIC_EXPORT_HTTP_{(int)response.StatusCode}");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0 || bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("PUBLIC_EXPORT_DOCUMENT_TOO_LARGE");
        var json = bytes.Length >= 5 && bytes[0] == 0x5D ? _lzmaDecoder.Decode(bytes) : new UTF8Encoding(false, true).GetString(bytes);
        var records = PublicExportDocumentParser.Parse(json);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (new SyncBatch(sourceId, hash, json, records.Count, "public-export-1"), records);
    }
}
