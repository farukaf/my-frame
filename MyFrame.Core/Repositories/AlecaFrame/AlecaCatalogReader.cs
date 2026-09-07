using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MyFrame.Core;

public sealed class AlecaCatalogReader : IAlecaCatalogReader
{
    private readonly ILogger<AlecaCatalogReader> _logger;
    private static readonly HashSet<string> IgnoredFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "lang.json", "Node.json", "Quests.json", "Glyphs.json", "Sigils.json", "Skins.json"
    };

    public AlecaCatalogReader(ILogger<AlecaCatalogReader>? logger = null) =>
        _logger = logger ?? NullLogger<AlecaCatalogReader>.Instance;

    public async Task<CatalogSnapshot> LoadAsync(string alecaDirectory, CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return await LoadOnceAsync(alecaDirectory, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or JsonException)
            {
                lastError = error;
                _logger.LogWarning(error, "Transient catalog read failure on attempt {Attempt}", attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        throw new InvalidDataException("AlecaFrame was still updating its catalogs.", lastError);
    }

    private async Task<CatalogSnapshot> LoadOnceAsync(string alecaDirectory, CancellationToken cancellationToken)
    {
        var jsonDirectory = Path.Combine(alecaDirectory, "cachedData", "json");
        _logger.LogInformation("Loading AlecaFrame catalogs");
        if (!Directory.Exists(jsonDirectory))
            throw new DirectoryNotFoundException($"AlecaFrame catalog not found: {jsonDirectory}");

        var market = new Dictionary<string, MarketIdentity>(StringComparer.Ordinal);
        var relicsByReward = new Dictionary<string, List<RelicSource>>(StringComparer.Ordinal);
        var rewardsByRelic = new Dictionary<string, List<RelicSource>>(StringComparer.OrdinalIgnoreCase);
        var rawItems = new List<RawCatalogItem>();

        foreach (var file in Directory.EnumerateFiles(jsonDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IgnoredFiles.Contains(Path.GetFileName(file))) continue;
            await using var stream = OpenRead(file);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array) continue;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("rewards", out var rewards) && rewards.ValueKind == JsonValueKind.Array)
                    ParseRelic(element, rewards, relicsByReward, rewardsByRelic, market);
                var item = ParseItem(element);
                if (item is not null) rawItems.Add(item);
                ParseMarketIdentity(element, market);
            }
        }

        var items = rawItems.GroupBy(x => x.UniqueName, StringComparer.Ordinal)
            .Select(x => x.First())
            .Select(x => Materialize(x, relicsByReward, rewardsByRelic, market))
            .ToArray();
        _logger.LogInformation("Catalog loaded with {ItemCount} items and {MarketMappingCount} market mappings",
            items.Length, market.Count);
        return new CatalogSnapshot(items, items.ToDictionary(x => x.UniqueName, StringComparer.Ordinal), market);
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static RawCatalogItem? ParseItem(JsonElement element)
    {
        var uniqueName = GetString(element, "uniqueName");
        var name = GetString(element, "name");
        if (string.IsNullOrWhiteSpace(uniqueName) || string.IsNullOrWhiteSpace(name)) return null;

        var components = new List<CatalogComponent>();
        if (element.TryGetProperty("components", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var component in values.EnumerateArray())
            {
                var componentUniqueName = GetString(component, "uniqueName");
                var componentName = GetString(component, "name");
                if (string.IsNullOrWhiteSpace(componentUniqueName) || string.IsNullOrWhiteSpace(componentName)) continue;
                components.Add(new CatalogComponent(componentUniqueName, componentName,
                    Math.Max(1, GetInt(component, "itemCount", 1)), Math.Max(0, GetInt(component, "ducats")),
                    GetBool(component, "tradable"), GetString(component, "imageName") ?? ""));
            }
        }

        return new RawCatalogItem(uniqueName, name, GetString(element, "category") ?? "",
            GetString(element, "productCategory") ?? "", GetString(element, "imageName") ?? "",
            GetBool(element, "masterable"), GetBool(element, "isPrime") || name.Contains(" Prime", StringComparison.OrdinalIgnoreCase),
            GetBool(element, "tradable"), GetBool(element, "vaulted"), GetString(element, "estimatedVaultDate"),
            TryReadMarket(element), components);
    }

    private static CatalogItem Materialize(RawCatalogItem item,
        IReadOnlyDictionary<string, List<RelicSource>> relicsByReward,
        IReadOnlyDictionary<string, List<RelicSource>> rewardsByRelic,
        IDictionary<string, MarketIdentity> market)
    {
        var normalizedItem = ItemNameNormalizer.Normalize(item.Name);
        var identity = item.Market;
        if (identity is null && market.TryGetValue(normalizedItem, out var mapped)) identity = mapped;
        if (identity is null && item.Prime && item.Components.Count > 0)
        {
            identity = new MarketIdentity("", ToSlug(item.Name) + "_set");
            market.TryAdd(normalizedItem, identity);
        }

        var relicSources = new List<RelicSource>();
        foreach (var component in item.Components)
        {
            var fullName = component.Name.Equals("Blueprint", StringComparison.OrdinalIgnoreCase)
                ? $"{item.Name} Blueprint" : $"{item.Name} {component.Name}";
            if (relicsByReward.TryGetValue(ItemNameNormalizer.Normalize(fullName), out var sources))
                relicSources.AddRange(sources);
        }
        if (rewardsByRelic.TryGetValue(item.Name, out var rewards)) relicSources.AddRange(rewards);

        return new CatalogItem(item.UniqueName, item.Name, item.Category, item.ProductCategory, item.ImageName,
            item.Masterable, item.Prime, item.Tradable, item.Vaulted, item.EstimatedVaultDate,
            identity?.Id, identity?.Slug, item.Components, relicSources.Distinct().ToArray());
    }

    private static void ParseRelic(JsonElement relic, JsonElement rewards,
        IDictionary<string, List<RelicSource>> relicsByReward,
        IDictionary<string, List<RelicSource>> rewardsByRelic,
        IDictionary<string, MarketIdentity> market)
    {
        var relicName = GetString(relic, "name") ?? "Relic";
        var vaulted = GetBool(relic, "vaulted");
        foreach (var reward in rewards.EnumerateArray())
        {
            if (!reward.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) continue;
            var rewardName = GetString(item, "name");
            if (string.IsNullOrWhiteSpace(rewardName)) continue;
            var normalized = ItemNameNormalizer.Normalize(rewardName);
            if (!relicsByReward.TryGetValue(normalized, out var list)) relicsByReward[normalized] = list = [];
            var source = new RelicSource(relicName, GetString(reward, "rarity") ?? "", GetDouble(reward, "chance"), vaulted, rewardName);
            list.Add(source);
            if (!rewardsByRelic.TryGetValue(relicName, out var relicRewards))
                rewardsByRelic[relicName] = relicRewards = [];
            relicRewards.Add(source);
            if (item.TryGetProperty("warframeMarket", out var marketElement)) AddMarket(normalized, marketElement, market);
        }
    }

    private static void ParseMarketIdentity(JsonElement element, IDictionary<string, MarketIdentity> market)
    {
        var name = GetString(element, "name");
        if (!string.IsNullOrWhiteSpace(name) && element.TryGetProperty("marketInfo", out var info))
            AddMarket(ItemNameNormalizer.Normalize(name), info, market);
    }

    private static MarketIdentity? TryReadMarket(JsonElement element)
    {
        if (!element.TryGetProperty("marketInfo", out var info)) return null;
        var slug = GetString(info, "urlName") ?? "";
        return string.IsNullOrWhiteSpace(slug) ? null : new MarketIdentity(GetString(info, "id") ?? "", slug);
    }

    private static void AddMarket(string name, JsonElement element, IDictionary<string, MarketIdentity> market)
    {
        var slug = GetString(element, "urlName") ?? "";
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(slug))
            market[name] = new MarketIdentity(GetString(element, "id") ?? "", slug);
    }

    private static string ToSlug(string value) => string.Join('_', value.Split([' ', '-', '/'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToLowerInvariant();
    private static string? GetString(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool GetBool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static int GetInt(JsonElement e, string n, int d = 0) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var x) ? x : d;
    private static double GetDouble(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetDouble(out var x) ? x : 0;

    private sealed record RawCatalogItem(string UniqueName, string Name, string Category, string ProductCategory,
        string ImageName, bool Masterable, bool Prime, bool Tradable, bool Vaulted, string? EstimatedVaultDate,
        MarketIdentity? Market, IReadOnlyList<CatalogComponent> Components);
}
