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
        var equipment = await database.GetInventoryEquipmentAsync(cancellationToken);
        var stackables = await database.GetInventoryStackablesAsync(cancellationToken);
        var records = await database.GetPublicExportItemsAsync("public-export", cancellationToken);
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

        var items = records.Select(record => new CatalogItem(
            record.UniqueName,
            record.Name ?? record.UniqueName,
            record.Category ?? "Unknown",
            "",
            "",
            false,
            false,
            false,
            false,
            null,
            null,
            null,
            [],
            [])).ToArray();
        var catalog = new CatalogSnapshot(items,
            items.ToDictionary(x => x.UniqueName, StringComparer.Ordinal),
            new Dictionary<string, MarketIdentity>(StringComparer.Ordinal));
        return new(inventory, catalog, File.GetLastWriteTimeUtc(databasePath));
    }
}
