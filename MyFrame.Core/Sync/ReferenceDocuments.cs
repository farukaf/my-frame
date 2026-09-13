using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyFrame.Core.Sync;

public enum ReferenceKind { Wiki, Overframe }

public sealed record ReferenceSection(string Id, string? Title, string Content, string ContentHash);

public sealed record ReferenceDocument(
    ReferenceKind Kind,
    Uri Url,
    string Title,
    string Revision,
    string? License,
    string? Author,
    DateTimeOffset RetrievedAt,
    IReadOnlyList<ReferenceSection> Sections,
    bool IsTrustedForFacts = false);

public sealed record ReferenceHit(string SectionId, string? Title, string Snippet, double Score, Uri SourceUrl, string Revision);

public static class ReferenceDocumentParser
{
    public const int MaximumDocumentBytes = 16 * 1024 * 1024;
    public static ReferenceDocument Parse(string json, DateTimeOffset retrievedAt)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes) throw new InvalidDataException("REFERENCE_DOCUMENT_TOO_LARGE");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        var kind = String(root, "kind")?.ToLowerInvariant() switch { "wiki" => ReferenceKind.Wiki, "overframe" => ReferenceKind.Overframe, _ => throw new InvalidDataException("REFERENCE_KIND_UNSUPPORTED") };
        var urlText = String(root, "url");
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || !AllowedHost(kind, url.Host)) throw new InvalidDataException("REFERENCE_URL_UNSUPPORTED");
        var title = String(root, "title") ?? throw new InvalidDataException("REFERENCE_TITLE_MISSING");
        var revision = String(root, "revision") ?? throw new InvalidDataException("REFERENCE_REVISION_MISSING");
        if (!root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array) throw new InvalidDataException("REFERENCE_SECTIONS_MISSING");
        var parsed = new List<ReferenceSection>();
        foreach (var section in sections.EnumerateArray())
        {
            if (parsed.Count >= 1000 || section.ValueKind != JsonValueKind.Object) throw new InvalidDataException("REFERENCE_SECTIONS_INVALID");
            var id = String(section, "id");
            var content = String(section, "content");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(content) || Encoding.UTF8.GetByteCount(content) > 2 * 1024 * 1024) throw new InvalidDataException("REFERENCE_SECTION_INVALID");
            parsed.Add(new(id, String(section, "title"), content, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant()));
        }
        return new(kind, url, title, revision, String(root, "license"), String(root, "author"), retrievedAt, parsed);
    }

    private static bool AllowedHost(ReferenceKind kind, string host) => kind switch
    {
        ReferenceKind.Wiki => host.Equals("wiki.warframe.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".wiki.warframe.com", StringComparison.OrdinalIgnoreCase),
        ReferenceKind.Overframe => host.Equals("overframe.gg", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".overframe.gg", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
    private static string? String(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public static class ReferenceSearch
{
    public static IReadOnlyList<ReferenceHit> Search(IEnumerable<ReferenceDocument> documents, string query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0) return [];
        var terms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return documents.SelectMany(document => document.Sections.Select(section => (document, section)))
            .Select(pair => (pair.document, pair.section, score: Score(pair.section.Content, terms)))
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.section.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(item => new ReferenceHit(item.section.Id, item.section.Title, Snippet(item.section.Content, terms), item.score, item.document.Url, item.document.Revision))
            .ToArray();
    }

    private static double Score(string content, string[] terms) => terms.Length == 0 ? 0 : terms.Count(term => Normalize(content).Contains(term, StringComparison.Ordinal)) / (double)terms.Length;
    private static string Snippet(string content, string[] terms) { var index = terms.Select(term => Normalize(content).IndexOf(term, StringComparison.Ordinal)).Where(index => index >= 0).DefaultIfEmpty(0).Min(); var start = Math.Max(0, index - 80); return content[start..Math.Min(content.Length, start + 280)]; }
    private static string Normalize(string value) => PublicExportIdentity.Canonicalize(value);
}
