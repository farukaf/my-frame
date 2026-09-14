using Microsoft.Data.Sqlite;
using MyFrame.Core.Sync;

namespace MyFrame.Core.Tests;

public sealed class SyncDatabaseTests
{
    [Fact]
    public async Task InitializesLegacyCatalogSchemaByAddingRichRawColumn()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-legacy-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "legacy.db");
        Directory.CreateDirectory(root);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE public_export_items (revision_id TEXT NOT NULL, unique_name TEXT NOT NULL, name TEXT, category TEXT, description TEXT, canonical_name TEXT NOT NULL, PRIMARY KEY(revision_id, unique_name));";
            await command.ExecuteNonQueryAsync();
        }

        await using var db = new SyncDatabase(path);
        await db.InitializeAsync();

        await using var verify = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await verify.OpenAsync();
        await using var check = verify.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('public_export_items') WHERE name='raw_json';";
        Assert.Equal(1L, (long)(await check.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task MigratesLegacySourceRevisionParserVersionWithoutLosingRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-legacy-parser-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "legacy.db");
        Directory.CreateDirectory(root);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE sources(source_id TEXT PRIMARY KEY, kind TEXT NOT NULL, display_name TEXT NOT NULL, enabled INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL);
                CREATE TABLE source_revisions(revision_id TEXT PRIMARY KEY, source_id TEXT NOT NULL, content_hash TEXT NOT NULL, payload_json TEXT NOT NULL, record_count INTEGER NOT NULL, state TEXT NOT NULL, retrieved_at TEXT NOT NULL, published_at TEXT);
                INSERT INTO sources(source_id, kind, display_name, created_at) VALUES ('legacy', 'fixture', 'Legacy', '2026-09-13T12:00:00Z');
                INSERT INTO source_revisions(revision_id, source_id, content_hash, payload_json, record_count, state, retrieved_at, published_at) VALUES ('legacy-revision', 'legacy', 'legacy-hash', '{}', 0, 'active', '2026-09-13T12:00:00Z', '2026-09-13T12:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var db = new SyncDatabase(path);
        await db.InitializeAsync();

        var status = await db.GetStatusAsync("legacy");
        Assert.Equal("legacy-unknown", status!.ParserVersion);
        var publication = await db.PublishAsync(new SyncBatch("legacy", "new-hash", "{}", 0, "legacy-parser-2"));
        Assert.False(publication.AlreadyPublished);
        Assert.Equal("legacy-parser-2", (await db.GetStatusAsync("legacy"))!.ParserVersion);
    }

    [Fact]
    public async Task LegacyMigrationCanBeRolledBackFromBackupAndReapplied()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-legacy-rollback-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "legacy.db");
        var backup = Path.Combine(root, "backup", "legacy.db");
        Directory.CreateDirectory(root);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE sources(source_id TEXT PRIMARY KEY, kind TEXT NOT NULL, display_name TEXT NOT NULL, enabled INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL);
                CREATE TABLE source_revisions(revision_id TEXT PRIMARY KEY, source_id TEXT NOT NULL, content_hash TEXT NOT NULL, payload_json TEXT NOT NULL, record_count INTEGER NOT NULL, state TEXT NOT NULL, retrieved_at TEXT NOT NULL, published_at TEXT);
                INSERT INTO sources(source_id, kind, display_name, created_at) VALUES ('legacy', 'fixture', 'Legacy', '2026-09-13T12:00:00Z');
                INSERT INTO source_revisions(revision_id, source_id, content_hash, payload_json, record_count, state, retrieved_at, published_at) VALUES ('legacy-revision', 'legacy', 'legacy-hash', '{}', 0, 'active', '2026-09-13T12:00:00Z', '2026-09-13T12:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var db = new SyncDatabase(path))
        {
            await db.BackupAsync(backup);
            await db.InitializeAsync();
            Assert.Equal("legacy-unknown", (await db.GetStatusAsync("legacy"))!.ParserVersion);
        }

        await using (var db = new SyncDatabase(path))
        {
            await db.RestoreAsync(backup);
            await db.InitializeAsync();
            Assert.Equal("legacy-unknown", (await db.GetStatusAsync("legacy"))!.ParserVersion);
        }

        await using var verify = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await verify.OpenAsync();
        await using var check = verify.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM source_revisions WHERE revision_id='legacy-revision';";
        Assert.Equal(1L, (long)(await check.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task RejectsDatabaseSchemaNewerThanThisBuildWithoutResettingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-future-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "future.db");
        Directory.CreateDirectory(root);
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL); INSERT INTO schema_migrations(version, applied_at) VALUES (99, 'future'); CREATE TABLE sentinel(value TEXT NOT NULL); INSERT INTO sentinel(value) VALUES ('keep');";
            await command.ExecuteNonQueryAsync();
        }

        await using var db = new SyncDatabase(path);
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => db.InitializeAsync());
        Assert.Equal("SYNC_SCHEMA_NEWER", error.Message);

        await using var verify = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await verify.OpenAsync();
        await using var check = verify.CreateCommand();
        check.CommandText = "SELECT value FROM sentinel;";
        Assert.Equal("keep", await check.ExecuteScalarAsync());
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='source_revisions';";
        Assert.Equal(0L, (long)(await check.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task InitializesIdempotentlyAndPublishesStatus()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await db.InitializeAsync();
        await db.InitializeAsync();
        var result = await db.PublishAsync(new SyncBatch("warframe", "hash-1", "{\"items\":[]}", 0));
        var status = await db.GetStatusAsync("warframe");
        Assert.False(result.AlreadyPublished);
        Assert.Equal(result.RevisionId, status!.ActiveRevisionId);
        Assert.Equal("published", status.LastRunState);
    }

    [Fact]
    public async Task SameContentHashIsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var batch = new SyncBatch("wiki", "same", "{}", 1);
        var first = await db.PublishAsync(batch);
        var second = await db.PublishAsync(batch);
        Assert.False(first.AlreadyPublished);
        Assert.True(second.AlreadyPublished);
        Assert.Equal(first.RevisionId, second.RevisionId);
    }

    [Fact]
    public async Task PrunesOldRetainedRevisionsAndKeepsActiveRevision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-retention-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var publications = new List<SyncPublicationResult>();
        for (var index = 0; index < 4; index++)
            publications.Add(await db.PublishAsync(new SyncBatch("catalog", $"hash-{index}", $"{{\"index\":{index}}}", 1)));

        Assert.Equal(2, await db.PruneRetainedAsync(2));

        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision_id, state FROM source_revisions WHERE source_id='catalog' ORDER BY retrieved_at;";
        var rows = new List<(string RevisionId, string State)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1)));

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.RevisionId == publications[^1].RevisionId && row.State == "active");
        Assert.Contains(rows, row => row.RevisionId == publications[^2].RevisionId && row.State == "retained");
    }

    [Fact]
    public async Task InvalidBatchDoesNotCreateSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await Assert.ThrowsAsync<ArgumentException>(() => db.PublishAsync(new SyncBatch("", "hash", "{}", 1)));
        Assert.Null(await db.GetStatusAsync("missing"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ConcurrentWritersKeepOneActiveRevision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => db.PublishAsync(new SyncBatch("source", $"hash-{i}", "{}", i))));
        var status = await db.GetStatusAsync("source");
        Assert.Equal(4, results.Count(r => !r.AlreadyPublished));
        Assert.NotNull(status!.ActiveRevisionId);
    }

    [Fact]
    public async Task BackupCanBeOpenedReadOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "sync.db");
        var backup = Path.Combine(root, "backup", "sync.db");
        await using (var db = new SyncDatabase(path))
        {
            await db.PublishAsync(new SyncBatch("warframe", "hash", "{}", 2));
            await db.BackupAsync(backup);
        }
        await using var restored = new SyncDatabase(backup);
        var status = await restored.GetStatusAsync("warframe");
        Assert.Equal("hash", status!.ActiveContentHash);
    }

    [Fact]
    public async Task RestoreReplacesActiveDatabaseFromBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}");
        var active = Path.Combine(root, "active.db");
        var backup = Path.Combine(root, "backup.db");
        await using (var source = new SyncDatabase(Path.Combine(root, "source.db")))
        {
            await source.PublishAsync(new SyncBatch("warframe", "restorable", "{}", 1));
            await source.BackupAsync(backup);
        }
        await using var target = new SyncDatabase(active);
        await target.PublishAsync(new SyncBatch("warframe", "old", "{}", 1));
        await target.RestoreAsync(backup);

        var status = await target.GetStatusAsync("warframe");
        Assert.Equal("restorable", status!.ActiveContentHash);
    }

    [Fact]
    public async Task HostRecordsFailureWithoutReplacingActiveRevision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await db.PublishAsync(new SyncBatch("source", "good", "{}", 1));
        await using var host = new SyncHost(db);
        var result = await host.RunOnceAsync("source", _ => throw new InvalidDataException("SCHEMA_INVALID"));
        var status = await db.GetStatusAsync("source");
        Assert.Null(result);
        Assert.Equal("good", status!.ActiveContentHash);
        Assert.Equal("SCHEMA_INVALID", status.ErrorCode);
        Assert.Equal("failed", status.LastRunState);
    }

    [Fact]
    public async Task HostMaintenanceRunsRetentionThroughTheLifecycleBoundary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-host-maintenance-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await db.PublishAsync(new SyncBatch("source", "one", "{}", 1));
        await db.PublishAsync(new SyncBatch("source", "two", "{}", 1));
        await db.PublishAsync(new SyncBatch("source", "three", "{}", 1));
        await using var host = new SyncHost(db);

        Assert.Equal(1, await host.RunMaintenanceAsync(2));
        Assert.True(host.State.Running);
        Assert.NotNull(host.State.LastRunAt);
        Assert.Equal("three", (await db.GetStatusAsync("source"))!.ActiveContentHash);
    }

    [Fact]
    public async Task RecentRunsAreReadOnlyAndOrderedAcrossSources()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await db.PublishAsync(new SyncBatch("source-a", "hash", "{}", 1));
        await db.RecordFailureAsync("source-b", "TEST_FAILURE");

        var runs = await db.GetRecentRunsAsync(limit: 10);

        Assert.Equal(2, runs.Count);
        Assert.Equal("failed", runs[0].State);
        Assert.Equal("TEST_FAILURE", runs[0].ErrorCode);
        Assert.Equal("source-a", runs[1].SourceId);
    }

    [Fact]
    public async Task CatalogPublicationStoresNormalizedItemsAtomically()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var records = new[] { new PublicExportRecord("/Lotus/Test", "Lâmina", "Melee", null, new Dictionary<string, string>()) };
        var result = await db.PublishCatalogAsync(new SyncBatch("public-export", "hash", "[]", 1), records);
        var stored = await db.GetPublicExportItemsAsync("public-export");
        Assert.False(result.AlreadyPublished);
        Assert.Single(stored);
        Assert.Equal("lamina", PublicExportIdentity.Canonicalize(stored[0].Name!));
    }

    [Fact]
    public async Task HostPreservesHttpFailureCodeForStatusPage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await using var host = new SyncHost(db);
        await host.RunOnceAsync("public-export", _ => throw new HttpRequestException("PUBLIC_EXPORT_HTTP_403"));
        var status = await db.GetStatusAsync("public-export");
        Assert.Equal("PUBLIC_EXPORT_HTTP_403", status!.ErrorCode);
    }

    [Fact]
    public async Task InventoryPublicationPreservesInstanceAndCoverage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var envelope = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(), 2, DateTimeOffset.UtcNow, "native", "unverified", "{\"equipment\":[]}", "inventory-hash");
        var projection = new InventoryProjection([new InventoryEquipmentRecord("instance-1", "/Lotus/Test", 30, null, InventoryFieldState.Known, InventoryFieldState.NotObserved, "{\"instanceId\":\"instance-1\"}")], [], [new InventoryUnknownRecord("futureField", "true", "FIELD_NOT_MAPPED")], new Dictionary<string, InventoryFieldState> { ["equipment"] = InventoryFieldState.Known });
        await db.PublishInventoryAsync(envelope, projection);
        var stored = await db.GetInventoryEquipmentAsync();
        var coverage = await db.GetInventoryCoverageAsync();
        Assert.Single(stored);
        Assert.Equal("instance-1", stored[0].InstanceId);
        Assert.Equal(InventoryFieldState.NotObserved, stored[0].ConfigState);
        Assert.Equal(InventoryFieldState.Known, coverage["equipment"]);
    }

    [Fact]
    public async Task InventoryPublicationExposesCaptureModeWithoutMergingDeltas()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-capture-mode-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var envelope = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(), 3,
            DateTimeOffset.UtcNow, "native", "unverified", "{\"equipment\":[]}", "capture-mode-hash", "delta");

        await db.PublishInventoryAsync(envelope, new InventoryProjection([], [], [],
            new Dictionary<string, InventoryFieldState> { ["equipment"] = InventoryFieldState.Known }));

        var status = await db.GetActiveInventoryRevisionStatusAsync();
        Assert.NotNull(status);
        Assert.Equal("delta", status!.CaptureMode);
        Assert.Equal("unverified", status.Completeness);
        Assert.Equal(3, status.Sequence);
    }

    [Fact]
    public async Task InventoryPublicationPreservesAttributedUpgrades()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-upgrades-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var envelope = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(), 1,
            DateTimeOffset.UtcNow, "test", "verified", "{\"mods\":[]}", "upgrades-hash");
        var projection = new InventoryProjection([], [], [],
            new Dictionary<string, InventoryFieldState> { ["upgrades.mods"] = InventoryFieldState.Known },
            [new InventoryUpgradeRecord("instance-1", "mods", "/Lotus/Mod", 5, "{\"id\":\"/Lotus/Mod\",\"rank\":5}")]);

        await db.PublishInventoryAsync(envelope, projection);

        var stored = await db.GetInventoryUpgradesAsync();
        var upgrade = Assert.Single(stored);
        Assert.Equal("instance-1", upgrade.OwnerInstanceId);
        Assert.Equal("/Lotus/Mod", upgrade.UpgradeId);
        Assert.Equal(5, upgrade.Rank);
    }

    [Fact]
    public async Task NewInventoryRevisionReplacesPreviousCoverage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-inventory-coverage-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var first = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(), 1,
            DateTimeOffset.UtcNow, "native", "unverified", "{}", "inventory-coverage-1");
        var second = first with { EventId = Guid.NewGuid(), Sequence = 2, ContentHash = "inventory-coverage-2" };
        await db.PublishInventoryAsync(first, new InventoryProjection([], [], [],
            new Dictionary<string, InventoryFieldState> { ["equipment"] = InventoryFieldState.Known }));
        await db.PublishInventoryAsync(second, new InventoryProjection([], [], [],
            new Dictionary<string, InventoryFieldState> { ["equipment"] = InventoryFieldState.NotObserved }));

        var coverage = await db.GetInventoryCoverageAsync();

        Assert.Equal(InventoryFieldState.NotObserved, coverage["equipment"]);
    }

    [Fact]
    public async Task SynchronizedReaderProjectsPublishedInventoryAndCatalog()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using (var db = new SyncDatabase(path))
        {
            await db.PublishCatalogAsync(new SyncBatch("public-export", "catalog-hash", "[]", 1),
                [new PublicExportRecord("/Lotus/Weapon", "Test Weapon", "Weapon", null,
                    new Dictionary<string, string>(),
                    "{\"uniqueName\":\"/Lotus/Weapon\",\"name\":\"Test Weapon\",\"category\":\"Weapon\",\"components\":[{\"uniqueName\":\"/Lotus/Part\",\"name\":\"Test Part\",\"itemCount\":2,\"ducats\":15,\"tradable\":true}]}" )]);
            var envelope = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(), 1,
                DateTimeOffset.UtcNow, "native", "unverified", "{}", "inventory-hash");
            var projection = new InventoryProjection(
                [new InventoryEquipmentRecord("instance", "/Lotus/Weapon", 30, null, InventoryFieldState.Known, InventoryFieldState.NotObserved, "{}")],
                [new InventoryStackableRecord("/Lotus/Resource", 7, InventoryFieldState.Known, "{}")], [],
                new Dictionary<string, InventoryFieldState>());
            await db.PublishInventoryAsync(envelope, projection);
        }

        var reader = new SqliteSynchronizedDataReader(path);
        var snapshot = await reader.ReadAsync();
        Assert.NotNull(snapshot);
        Assert.Contains("/Lotus/Weapon", snapshot!.Inventory.OwnedEquipment);
        Assert.Equal(7, snapshot.Inventory.Stackables["/Lotus/Resource"]);
        Assert.Single(snapshot.Catalog.Items);
        Assert.Equal("Test Weapon", snapshot.Catalog.Items[0].Name);
        Assert.Single(snapshot.Catalog.Items[0].Components);
        Assert.Equal(2, snapshot.Catalog.Items[0].Components[0].Required);
    }

    [Fact]
    public async Task SynchronizedReaderPreservesMarketIdentityForItemsAndComponents()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-market-map-{Guid.NewGuid():N}.db");
        await using (var db = new SyncDatabase(path))
        {
            await db.PublishCatalogAsync(new SyncBatch("public-export", "market-map", "[]", 1),
                [new PublicExportRecord("/Lotus/Weapon", "Test Weapon", "Weapon", null,
                    new Dictionary<string, string>(),
                    "{\"uniqueName\":\"/Lotus/Weapon\",\"name\":\"Test Weapon\",\"category\":\"Weapon\",\"marketId\":\"set-id\",\"marketSlug\":\"test-weapon\",\"components\":[{\"uniqueName\":\"/Lotus/Part\",\"name\":\"Test Part\",\"itemCount\":1,\"tradable\":true,\"marketId\":\"part-id\",\"marketSlug\":\"test_part\"}]}" )]);
            var envelope = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(), 1,
                DateTimeOffset.UtcNow, "native", "unverified", "{}", "market-map-inventory");
            await db.PublishInventoryAsync(envelope, new InventoryProjection(
                [new InventoryEquipmentRecord("instance", "/Lotus/Weapon", 30, null, InventoryFieldState.Known, InventoryFieldState.NotObserved, "{}")],
                [new InventoryStackableRecord("/Lotus/Part", 1, InventoryFieldState.Known, "{}")], [],
                new Dictionary<string, InventoryFieldState>()));
        }

        var snapshot = await new SqliteSynchronizedDataReader(path).ReadAsync();
        Assert.NotNull(snapshot);
        Assert.Equal("set-id", snapshot!.Catalog.MarketByNormalizedName["testweapon"].Id);
        Assert.Equal("test_part", snapshot.Catalog.MarketByNormalizedName["testweapontestpart"].Slug);
    }

    [Fact]
    public async Task CatalogCoverageReportsRichFieldsWithoutInferringMissingData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-catalog-coverage-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        await db.PublishCatalogAsync(new SyncBatch("public-export", "catalog-rich-coverage", "[]", 1),
            [new PublicExportRecord("/Lotus/Weapon", "Test Weapon", "Weapon", null,
                new Dictionary<string, string>(),
                "{\"uniqueName\":\"/Lotus/Weapon\",\"name\":\"Test Weapon\",\"category\":\"Weapon\",\"marketId\":\"set-id\",\"marketSlug\":\"test-weapon\",\"components\":[]}")]);

        var coverage = await db.GetSourceCoverageAsync("public-export");

        Assert.Equal(InventoryFieldState.Known, coverage["components"]);
        Assert.Equal(InventoryFieldState.Known, coverage["marketIdentity"]);
        Assert.Equal(InventoryFieldState.NotObserved, coverage["relics"]);
        Assert.Equal(InventoryFieldState.NotObserved, coverage["imageName"]);
    }

    [Fact]
    public async Task SynchronizedReaderDoesNotProjectDeltaAsCompleteInventory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-delta-reader-{Guid.NewGuid():N}.db");
        await using (var db = new SyncDatabase(path))
        {
            await db.PublishCatalogAsync(new SyncBatch("public-export", "catalog-delta-reader", "[]", 1),
                [new PublicExportRecord("/Lotus/Weapon", "Test Weapon", "Weapon", null,
                    new Dictionary<string, string>())]);
            var envelope = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(), 1,
                DateTimeOffset.UtcNow, "native", "unverified", "{\"equipment\":[]}", "inventory-delta-reader", "delta");
            await db.PublishInventoryAsync(envelope, new InventoryProjection([], [], [],
                new Dictionary<string, InventoryFieldState> { ["equipment"] = InventoryFieldState.Known }));
        }

        var snapshot = await new SqliteSynchronizedDataReader(path).ReadAsync();
        Assert.Null(snapshot);
    }

    [Fact]
    public async Task WorldStatePublicationKeepsExpiryAndSourceRevision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"myframe-{Guid.NewGuid():N}.db");
        await using var db = new SyncDatabase(path);
        var snapshot = new WorldStateSnapshot(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "fixture", [new WorldStateBounty("bounty", "Entrati", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10), [new WorldStateJob("job", "Sample", null, 0, [100], [new WorldStateReward("Endo", 50, 100, "Common")])])], [], "world-hash", true, new Dictionary<string, InventoryFieldState>());
        await db.PublishWorldStateAsync(snapshot, new SyncBatch("worldstate-pc", "world-hash", "{}", 1));
        var current = await db.GetCurrentWorldStateBountiesAsync(DateTimeOffset.UtcNow);
        Assert.Single(current);
        Assert.Equal("Entrati", current[0].Syndicate);
    }
}
