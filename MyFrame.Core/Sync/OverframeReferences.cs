using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace MyFrame.Core.Sync;

public enum OverframeEntityType { Item, Mod, Warframe }

public sealed record OverframeSyncOptions(int Workers = 2, TimeSpan? RequestDelay = null,
    TimeSpan? TimeToLive = null)
{
    public TimeSpan EffectiveRequestDelay => RequestDelay ?? TimeSpan.FromMilliseconds(800);
    public TimeSpan EffectiveTimeToLive => TimeToLive ?? TimeSpan.FromHours(8);

    public void Validate()
    {
        if (Workers is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(Workers));
        if (EffectiveRequestDelay < TimeSpan.Zero || EffectiveRequestDelay > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(RequestDelay));
        if (EffectiveTimeToLive < TimeSpan.FromMinutes(1) || EffectiveTimeToLive > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(TimeToLive));
    }
}

public sealed record OverframeCacheEntry(string CacheKey, OverframeEntityType EntityType,
    string CanonicalTerm, string DisplayName, Uri SourceUrl, string PayloadJson, string ContentHash,
    string ParserVersion, DateTimeOffset FetchedAt, DateTimeOffset ExpiresAt,
    string? ETag = null, string? LastModified = null)
{
    public bool IsFresh(DateTimeOffset now) => ExpiresAt > now;
}

public sealed record OverframeSyncResult(string State, string CacheKey, int SitemapMatches,
    int PagesFetched, OverframeCacheEntry? Entry);

public static class OverframeCacheKey
{
    public static string Create(OverframeEntityType type, string term) =>
        $"{type.ToString().ToLowerInvariant()}:{CanonicalTerm(term)}";

    public static string CanonicalTerm(string term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length > 200)
            throw new ArgumentException("A specific term from 1 to 200 characters is required.", nameof(term));
        return PublicExportIdentity.Canonicalize(term.Trim());
    }

    public static OverframeEntityType ParseType(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "item" => OverframeEntityType.Item,
        "mod" => OverframeEntityType.Mod,
        "warframe" => OverframeEntityType.Warframe,
        _ => throw new ArgumentException("Type must be Item, Mod, or Warframe.", nameof(value))
    };
}

public sealed class OverframeCacheStore(string databasePath)
{
    private readonly string _databasePath = Path.GetFullPath(databasePath);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadWriteCreate, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS overframe_cache(cache_key TEXT PRIMARY KEY, entity_type TEXT NOT NULL, canonical_term TEXT NOT NULL, display_name TEXT NOT NULL, source_url TEXT NOT NULL, payload_json TEXT NOT NULL, content_hash TEXT NOT NULL, parser_version TEXT NOT NULL, fetched_at TEXT NOT NULL, expires_at TEXT NOT NULL, etag TEXT, last_modified TEXT);
            CREATE INDEX IF NOT EXISTS ix_overframe_cache_lookup ON overframe_cache(entity_type, canonical_term);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OverframeCacheEntry?> GetAsync(OverframeEntityType type, string term,
        bool readOnly = false, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_databasePath)) return null;
        await using var connection = await OpenAsync(readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            cancellationToken);
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='overframe_cache';";
        if (await exists.ExecuteScalarAsync(cancellationToken) is null) return null;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cache_key, entity_type, canonical_term, display_name, source_url, payload_json,
                   content_hash, parser_version, fetched_at, expires_at, etag, last_modified
            FROM overframe_cache WHERE cache_key=$key LIMIT 1;
            """;
        command.Parameters.AddWithValue("$key", OverframeCacheKey.Create(type, term));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task UpsertAsync(OverframeCacheEntry entry, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO overframe_cache(cache_key, entity_type, canonical_term, display_name, source_url,
              payload_json, content_hash, parser_version, fetched_at, expires_at, etag, last_modified)
            VALUES($key,$type,$term,$name,$url,$payload,$hash,$parser,$fetched,$expires,$etag,$modified)
            ON CONFLICT(cache_key) DO UPDATE SET display_name=excluded.display_name,
              source_url=excluded.source_url, payload_json=excluded.payload_json,
              content_hash=excluded.content_hash, parser_version=excluded.parser_version,
              fetched_at=excluded.fetched_at, expires_at=excluded.expires_at,
              etag=excluded.etag, last_modified=excluded.last_modified;
            """;
        command.Parameters.AddWithValue("$key", entry.CacheKey);
        command.Parameters.AddWithValue("$type", entry.EntityType.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$term", entry.CanonicalTerm);
        command.Parameters.AddWithValue("$name", entry.DisplayName);
        command.Parameters.AddWithValue("$url", entry.SourceUrl.AbsoluteUri);
        command.Parameters.AddWithValue("$payload", entry.PayloadJson);
        command.Parameters.AddWithValue("$hash", entry.ContentHash);
        command.Parameters.AddWithValue("$parser", entry.ParserVersion);
        command.Parameters.AddWithValue("$fetched", entry.FetchedAt.ToString("O"));
        command.Parameters.AddWithValue("$expires", entry.ExpiresAt.ToString("O"));
        command.Parameters.AddWithValue("$etag", (object?)entry.ETag ?? DBNull.Value);
        command.Parameters.AddWithValue("$modified", (object?)entry.LastModified ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Mode = mode, Cache = SqliteCacheMode.Shared, Pooling = false
        }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }

    private static OverframeCacheEntry Read(SqliteDataReader reader) => new(reader.GetString(0),
        OverframeCacheKey.ParseType(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
        new Uri(reader.GetString(4)), reader.GetString(5), reader.GetString(6), reader.GetString(7),
        DateTimeOffset.Parse(reader.GetString(8)), DateTimeOffset.Parse(reader.GetString(9)),
        reader.IsDBNull(10) ? null : reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetString(11));
}

public static partial class OverframePageParser
{
    public const string ParserVersion = "overframe-html-1";
    public const int MaximumPageBytes = 4 * 1024 * 1024;

    public static OverframeCacheEntry Parse(OverframeEntityType type, string requestedTerm, Uri sourceUrl,
        string html, DateTimeOffset fetchedAt, TimeSpan ttl, string? etag = null, string? lastModified = null)
    {
        if (Encoding.UTF8.GetByteCount(html) is <= 0 or > MaximumPageBytes)
            throw new InvalidDataException("OVERFRAME_PAGE_SIZE_INVALID");
        var heading = MatchValue(HeadingRegex(), html) ?? throw new InvalidDataException("OVERFRAME_TITLE_MISSING");
        var name = Clean(heading);
        var description = Clean(MatchValue(DescriptionRegex(), html) ?? "");
        var builds = BuildRegex().Matches(html).Select(match => new
        {
            name = Clean(match.Groups[2].Value),
            url = new Uri(sourceUrl, WebUtility.HtmlDecode(match.Groups[1].Value)).AbsoluteUri
        }).Where(value => value.name.Length > 0).DistinctBy(value => value.url).Take(50).ToArray();
        var payload = JsonSerializer.Serialize(new
        {
            type = type.ToString(), name, description, sourceUrl = sourceUrl.AbsoluteUri,
            popularBuilds = builds, trustedForFacts = false
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return new(OverframeCacheKey.Create(type, requestedTerm), type,
            OverframeCacheKey.CanonicalTerm(requestedTerm), name, sourceUrl, payload, hash,
            ParserVersion, fetchedAt, fetchedAt + ttl, etag, lastModified);
    }

    private static string? MatchValue(Regex regex, string input) =>
        regex.Match(input) is { Success: true } match ? match.Groups[1].Value : null;
    private static string Clean(string value) => WebUtility.HtmlDecode(TagRegex().Replace(value, " "))
        .Replace('\u00a0', ' ').Trim().Replace("  ", " ");

    [GeneratedRegex("<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HeadingRegex();
    [GeneratedRegex("<meta[^>]+name=[\"']description[\"'][^>]+content=[\"'](.*?)[\"']", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DescriptionRegex();
    [GeneratedRegex("<a[^>]+href=[\"']([^\"']*/build/[^\"']*)[\"'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BuildRegex();
    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();
}

public sealed class OverframeReferenceSynchronizer(HttpClient client, OverframeCacheStore cache,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private static readonly Uri RobotsUri = new("https://overframe.gg/robots.txt");
    private static readonly Uri SitemapUri = new("https://overframe.gg/sitemap.xml");

    public async Task<OverframeSyncResult> SyncAsync(OverframeEntityType type, string term,
        OverframeSyncOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new(); options.Validate();
        var key = OverframeCacheKey.Create(type, term);
        var now = _timeProvider.GetUtcNow();
        var existing = await cache.GetAsync(type, term, cancellationToken: cancellationToken);
        if (existing?.IsFresh(now) == true) return new("cached", key, 1, 0, existing);

        var limiter = new SharedRequestLimiter(options.EffectiveRequestDelay, _timeProvider);
        var robots = await GetStringAsync(RobotsUri, limiter, cancellationToken);
        if (!RobotsAllows(robots, "/items/")) throw new InvalidDataException("OVERFRAME_ROBOTS_DISALLOWED");
        var sitemap = await GetStringAsync(SitemapUri, limiter, cancellationToken);
        var sitemapEntries = ParseSitemap(sitemap);
        var nested = sitemapEntries.Where(uri => uri.AbsolutePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Take(32).ToArray();
        var indexedPages = sitemapEntries.Where(uri => !uri.AbsolutePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var nestedSitemap in nested)
            indexedPages.AddRange(ParseSitemap(await GetStringAsync(nestedSitemap, limiter, cancellationToken)));
        var matches = indexedPages.Where(uri => Matches(type, term, uri)).Distinct().Take(20).ToArray();
        if (matches.Length == 0) return new("not_found", key, 0, 0, null);

        var results = new List<OverframeCacheEntry>();
        await Parallel.ForEachAsync(matches, new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Workers, CancellationToken = cancellationToken
        }, async (uri, token) =>
        {
            var html = await GetStringAsync(uri, limiter, token);
            var entry = OverframePageParser.Parse(type, term, uri, html, _timeProvider.GetUtcNow(),
                options.EffectiveTimeToLive);
            await cache.UpsertAsync(entry, token);
            lock (results) results.Add(entry);
        });
        return new("refreshed", key, matches.Length, results.Count,
            results.OrderBy(value => value.SourceUrl.AbsoluteUri, StringComparer.Ordinal).FirstOrDefault());
    }

    public static IReadOnlyList<Uri> ParseSitemap(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.None);
        return document.Descendants().Where(value => value.Name.LocalName == "loc")
            .Select(value => Uri.TryCreate(value.Value.Trim(), UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri is not null && uri.Scheme == Uri.UriSchemeHttps &&
                uri.Host.Equals("overframe.gg", StringComparison.OrdinalIgnoreCase))
            .Cast<Uri>().ToArray();
    }

    public static bool RobotsAllows(string robots, string path)
    {
        var applies = false;
        foreach (var raw in robots.Split('\n'))
        {
            var line = raw.Split('#', 2)[0].Trim();
            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
                applies = line["User-agent:".Length..].Trim() == "*";
            else if (applies && line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
            {
                var denied = line["Disallow:".Length..].Trim();
                if (denied.Length > 0 && path.StartsWith(denied.TrimEnd('*'), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        return true;
    }

    private static bool Matches(OverframeEntityType type, string term, Uri uri)
    {
        if (!RobotsAllows("User-agent: *\nDisallow: /api/\nDisallow: /Lotus/\nDisallow: /app/\nDisallow: /overwolf/\nDisallow: /user/\nDisallow: /search/\nDisallow: /account/", uri.AbsolutePath)) return false;
        var prefix = type == OverframeEntityType.Mod ? "/items/mods/" : "/items/arsenal/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var slug = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? "";
        return PublicExportIdentity.Equivalent(slug.Replace('-', ' '), term);
    }

    private async Task<string> GetStringAsync(Uri uri, SharedRequestLimiter limiter,
        CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("MyFrame.Sync/1.0 (+local-reference-cache)");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var final = response.RequestMessage?.RequestUri;
        if (final is null || final.Scheme != Uri.UriSchemeHttps ||
            !final.Host.Equals("overframe.gg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("OVERFRAME_REDIRECT_UNSUPPORTED");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidDataException("OVERFRAME_ACCESS_BLOCKED");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > OverframePageParser.MaximumPageBytes)
            throw new InvalidDataException("OVERFRAME_PAGE_SIZE_INVALID");
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private sealed class SharedRequestLimiter(TimeSpan delay, TimeProvider timeProvider)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTimeOffset _next = DateTimeOffset.MinValue;
        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var now = timeProvider.GetUtcNow();
                if (_next > now) await Task.Delay(_next - now, timeProvider, cancellationToken);
                _next = timeProvider.GetUtcNow() + delay;
            }
            finally { _gate.Release(); }
        }
    }
}
