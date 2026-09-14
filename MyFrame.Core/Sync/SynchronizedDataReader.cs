using System.Text.Json;

namespace MyFrame.Core.Sync;

public sealed record SynchronizedDataSnapshot(
    InventorySnapshot Inventory,
    CatalogSnapshot Catalog,
    DateTimeOffset RetrievedAt);

public interface ISynchronizedDataReader
{
    Task<SynchronizedDataSnapshot?> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed class SqliteSynchronizedDataReader(string databasePath) : ISynchronizedDataReader
{
    public async Task<SynchronizedDataSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath)) return null;
        await using var database = new SyncDatabase(databasePath);
        // Settings/market stores may create the shared database before the first sync publication.
        // Ensure the complete schema exists so a clean installation reports setup-required data
        // instead of leaking a "no such table" SQLite exception through MCP.
        await database.InitializeAsync(cancellationToken);
        var inventoryRevision = await database.GetActiveInventoryRevisionStatusAsync(cancellationToken);
        // A delta is evidence of change, not a complete inventory. Until a
        // reconciler applies it to a verified snapshot, refuse to project it
        // as authoritative MCP inventory rather than turning omissions into
        // zero/absent ownership.
        if (inventoryRevision is { CaptureMode: "delta" }) return null;
        var equipment = await database.GetInventoryEquipmentAsync(cancellationToken);
        var stackables = await database.GetInventoryStackablesAsync(cancellationToken);
        var records = await database.GetPublicExportItemsAsync("public-export", cancellationToken);
        var componentRows = await database.GetPublicExportComponentsAsync("public-export", cancellationToken);
        var relicRows = await database.GetPublicExportRelicsAsync("public-export", cancellationToken);
        if (equipment.Count == 0 && stackables.Count == 0 || records.Count == 0) return null;

        var stackableValues = stackables
            .Where(x => x.TypeId is not null && x.Quantity is not null)
            .GroupBy(x => x.TypeId!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Sum(item => item.Quantity!.Value), StringComparer.Ordinal);
        var owned = equipment.Where(x => x.TypeId is not null)
            .Select(x => x.TypeId!)
            .ToHashSet(StringComparer.Ordinal);
        var inventory = new InventorySnapshot(
            File.GetLastWriteTimeUtc(databasePath), stackableValues, owned,
            new Dictionary<string, long>(StringComparer.Ordinal), 0, 0, "my-frame-sqlite");

        var componentsByParent = componentRows.GroupBy(row => row.ParentUniqueName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PublicExportComponentRow>)group.ToArray(), StringComparer.Ordinal);
        var relicsByReward = relicRows.GroupBy(row => row.RewardUniqueName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PublicExportRelicRow>)group.ToArray(), StringComparer.Ordinal);
        var items = records.Select(record => PublicExportCatalogMapper.Map(record,
            componentsByParent.GetValueOrDefault(record.UniqueName), relicsByReward.GetValueOrDefault(record.UniqueName))).ToArray();
        var market = PublicExportCatalogMapper.MarketMappings(records, items);
        var catalog = new CatalogSnapshot(items,
            items.ToDictionary(x => x.UniqueName, StringComparer.Ordinal),
            market);
        return new(inventory, catalog, File.GetLastWriteTimeUtc(databasePath));
    }
}

internal static class PublicExportCatalogMapper
{
    public static CatalogItem Map(PublicExportRecord record,
        IReadOnlyList<PublicExportComponentRow>? normalizedComponents = null,
        IReadOnlyList<PublicExportRelicRow>? normalizedRelics = null)
    {
        if (string.IsNullOrWhiteSpace(record.RawJson))
            return Minimal(record);
        try
        {
            using var document = JsonDocument.Parse(record.RawJson);
            var root = document.RootElement;
            var components = normalizedComponents is { Count: > 0 }
                ? normalizedComponents.Select(row => new CatalogComponent(row.UniqueName, row.Name, row.RequiredCount,
                    row.Ducats, row.Tradable, row.ImageName ?? "")).ToArray()
                : ReadComponents(root);
            var relics = normalizedRelics is { Count: > 0 }
                ? normalizedRelics.Select(row => new RelicSource(row.RelicName, row.Rarity, row.Chance,
                    row.Vaulted, row.RewardName)).ToArray()
                : ReadRelics(root);
            return new(
                record.UniqueName,
                String(root, "name") ?? record.Name ?? record.UniqueName,
                String(root, "category") ?? record.Category ?? "Unknown",
                String(root, "productCategory") ?? String(root, "product_category") ?? "",
                String(root, "imageName") ?? String(root, "image_name") ?? "",
                Bool(root, "masterable") ?? Bool(root, "masterableType") ?? false,
                Bool(root, "prime") ?? false,
                Bool(root, "tradable") ?? false,
                Bool(root, "vaulted") ?? false,
                String(root, "estimatedVaultDate"),
                String(root, "marketId"),
                String(root, "marketSlug"),
                components,
                relics,
                String(root, "itemType") ?? "",
                record.Description ?? String(root, "description"));
        }
        catch (JsonException) { return Minimal(record); }
    }

    public static IReadOnlyDictionary<string, MarketIdentity> MarketMappings(
        IReadOnlyList<PublicExportRecord> records, IReadOnlyList<CatalogItem> items)
    {
        var result = new Dictionary<string, MarketIdentity>(StringComparer.Ordinal);
        foreach (var pair in records.Zip(items))
        {
            if (string.IsNullOrWhiteSpace(pair.First.RawJson)) continue;
            try
            {
                using var document = JsonDocument.Parse(pair.First.RawJson);
                var root = document.RootElement;
                Add(root, pair.Second.Name, result);
                if (!root.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var component in components.EnumerateArray())
                {
                    var componentName = String(component, "name");
                    if (string.IsNullOrWhiteSpace(componentName)) continue;
                    Add(component, $"{pair.Second.Name} {componentName}", result);
                }
            }
            catch (JsonException) { }
        }
        return result;
    }

    private static void Add(JsonElement element, string displayName,
        IDictionary<string, MarketIdentity> result)
    {
        var id = String(element, "marketId");
        var slug = String(element, "marketSlug");
        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(slug))
            result[ItemNameNormalizer.Normalize(displayName)] = new(id, slug);
    }

    private static CatalogItem Minimal(PublicExportRecord record) => new(record.UniqueName,
        record.Name ?? record.UniqueName, record.Category ?? "Unknown", "", "", false, false,
        false, false, null, null, null, [], []);

    private static IReadOnlyList<CatalogComponent> ReadComponents(JsonElement root)
    {
        if (!root.TryGetProperty("components", out var values) || values.ValueKind != JsonValueKind.Array) return [];
        var result = new List<CatalogComponent>();
        foreach (var value in values.EnumerateArray())
        {
            var unique = String(value, "uniqueName") ?? String(value, "unique_name");
            var name = String(value, "name");
            if (string.IsNullOrWhiteSpace(unique) || string.IsNullOrWhiteSpace(name)) continue;
            result.Add(new(unique, name, Math.Max(1, Int(value, "itemCount") ?? Int(value, "count") ?? 1),
                Math.Max(0, Int(value, "ducats") ?? 0), Bool(value, "tradable") ?? false,
                String(value, "imageName") ?? ""));
        }
        return result;
    }

    private static IReadOnlyList<RelicSource> ReadRelics(JsonElement root)
    {
        if (!root.TryGetProperty("relics", out var values) || values.ValueKind != JsonValueKind.Array) return [];
        var result = new List<RelicSource>();
        foreach (var value in values.EnumerateArray())
        {
            var relic = String(value, "relicName") ?? String(value, "relic");
            var reward = String(value, "rewardName") ?? String(value, "item");
            if (string.IsNullOrWhiteSpace(relic) || string.IsNullOrWhiteSpace(reward)) continue;
            result.Add(new(relic, String(value, "rarity") ?? "Unknown", Double(value, "chance") ?? 0, false, reward));
        }
        return result;
    }

    private static string? String(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static int? Int(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var result) ? result : null;
    private static double? Double(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var result) ? result : null;
    private static bool? Bool(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : null;
}
