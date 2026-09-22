using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MyFrame.Core;
using MyFrame.Core.Sync;
using MyFrame.Mcp;
using Xunit.Abstractions;

namespace MyFrame.Mcp.Tests;

public sealed class McpFeatureTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AcquisitionReportsUnavailableSourcesWithoutInventingRows()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-acquisition-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            var response = await new PlatformStatusService().GetAcquisitionAsync("/Lotus/Unknown");
            Assert.Equal("not_initialized", response.State);
            Assert.Empty(response.Components);
            Assert.Empty(response.Relics);
            Assert.Empty(response.Bounties);
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task CaptureInboxStatusReportsRuntimeReadinessWithoutPathOrPayload()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-capture-status-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            var response = await new PlatformStatusService().GetCaptureInboxStatusAsync();

            Assert.Equal("not_initialized", response.State);
            Assert.Equal(0, response.PendingMarkers);
            Assert.False(response.HeartbeatFresh);
            Assert.Null(response.HeartbeatState);
            Assert.DoesNotContain(root, JsonSerializer.Serialize(response), StringComparison.Ordinal);
            var history = await new PlatformStatusService().GetInventoryHistoryAsync();
            Assert.Equal("not_initialized", history.State);
            Assert.Empty(history.Items);
            var changes = await new PlatformStatusService().GetInventoryChangesAsync();
            Assert.Equal("not_initialized", changes.State);
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task WorldStateQueriesExposeRevisionAndParserProvenance()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-worldstate-provenance-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            var retrieved = DateTimeOffset.UtcNow;
            var snapshot = new WorldStateSnapshot(retrieved, retrieved, "fixture", [], [],
                "worldstate-provenance-hash", true,
                new Dictionary<string, InventoryFieldState>
                {
                    ["bountyRewards"] = InventoryFieldState.Known,
                    ["motherTokens"] = InventoryFieldState.NotObserved
                });
            await using (var database = new SyncDatabase(Path.Combine(root, "data.db")))
            {
                await database.PublishWorldStateAsync(snapshot,
                    new SyncBatch("worldstate-pc", snapshot.ContentHash, "{}", 0, "worldstate-official-1"));
            }

            var response = await new PlatformStatusService().GetWorldStateAsync();
            Assert.Equal("available", response.State);
            Assert.Equal("worldstate-official-1", response.ParserVersion);
            Assert.NotNull(response.ActiveRevisionId);
            Assert.Equal("NotObserved", response.Coverage["motherTokens"]);

            var bounties = await new PlatformStatusService().GetBountiesAsync();
            Assert.Equal("worldstate-official-1", bounties.ParserVersion);
            Assert.Equal(response.ActiveRevisionId, bounties.ActiveRevisionId);
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task AcquisitionPreservesWorldStateParserProvenance()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-acquisition-provenance-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            await using (var database = new SyncDatabase(Path.Combine(root, "data.db")))
            {
                await database.PublishCatalogAsync(
                    new SyncBatch("public-export", "catalog-acquisition-provenance", "[]", 1,
                        "public-export-1"),
                    [new PublicExportRecord("/Lotus/Test", "Test Weapon", "Weapon", null,
                        new Dictionary<string, string> { ["en"] = "Test Weapon" }, "{\"uniqueName\":\"/Lotus/Test\"}")]);
                var now = DateTimeOffset.UtcNow;
                var snapshot = new WorldStateSnapshot(now, now, "fixture", [], [],
                    "worldstate-acquisition-provenance", true,
                    new Dictionary<string, InventoryFieldState>());
                await database.PublishWorldStateAsync(snapshot,
                    new SyncBatch("worldstate-pc", snapshot.ContentHash, "{}", 0,
                        "worldstate-community-1"));
            }

            var response = await new PlatformStatusService().GetAcquisitionAsync("/Lotus/Test");
            Assert.Equal("available", response.State);
            Assert.Equal("worldstate-community-1", response.WorldStateParserVersion);
            Assert.NotNull(response.WorldStateRevisionId);
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task CaptureInboxStatusReturnsSanitizedCallbackDiagnostics()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-callback-status-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            var directory = Path.Combine(root, "captures");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "collector-status.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, kind = "my-frame-collector", state = "started",
                timestampUtc = DateTimeOffset.UtcNow, collectorState = "waitingForInventory",
                supportedFeatures = new[] { "match_info" },
                eventCounts = new Dictionary<string, int> { ["match_info"] = 3 },
                lastEventFeature = "match_info", lastEventAt = DateTimeOffset.UtcNow
            }));

            var response = await new PlatformStatusService().GetCaptureInboxStatusAsync();
            Assert.Equal("heartbeat-only", response.State);
            Assert.Equal("waitingForInventory", response.CollectorState);
            Assert.Contains("match_info", response.SupportedFeatures!);
            Assert.Equal(3, response.EventCounts!["match_info"]);
            Assert.Equal("match_info", response.LastEventFeature);
            Assert.NotNull(response.LastEventAt);
            Assert.DoesNotContain("waitingForInventory", response.HeartbeatState is null ? "" : response.HeartbeatState,
                StringComparison.Ordinal); // state remains the generic heartbeat state
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task SourceCoverageAllowsRegisteredMarketSource()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-market-coverage-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            var response = await new PlatformStatusService().GetSourceCoverageAsync(" WARFRAME-MARKET ");

            Assert.Equal("not_initialized", response.State);
            Assert.Null(response.ActiveRevisionId);
            Assert.Null(response.ParserVersion);

            var references = Path.Combine(root, "references");
            Directory.CreateDirectory(references);
            await File.WriteAllTextAsync(Path.Combine(references, "reference.json"),
                "{\"kind\":\"wiki\",\"url\":\"https://wiki.warframe.com/w/Test\",\"title\":\"Test\",\"revision\":\"r1\",\"sections\":[{\"id\":\"overview\",\"content\":\"Test\"}]} ");
            var referenceCoverage = await new PlatformStatusService().GetSourceCoverageAsync(" REFERENCES ");
            Assert.Contains(referenceCoverage.Items, item => item.SourceId == "references" && item.FieldPath == "documents");
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task SourceCoverageReadsMarketStateFromSqliteStore()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-market-coverage-populated-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            var store = new SqliteMarketStore(MyFrameStoragePaths.DataDatabasePath);
            await store.SetAsync(new MarketQuote("test-item", 10, 8, DateTimeOffset.UtcNow));
            await ((IMarketStateStore)store).SaveAsync(new MarketState(
                new MarketAccount("account", "Tenno", "pc"),
                [new MarketOrder("order", "item", "test-item", "sell", 10, 1, true)],
                DateTimeOffset.UtcNow));
            await ((IMarketItemIndexStore)store).SaveAsync(new MarketItemIndex(
                new Dictionary<string, MarketIdentity>
                {
                    ["test item"] = new("item", "test-item")
                }, DateTimeOffset.UtcNow));

            var response = await new PlatformStatusService().GetSourceCoverageAsync(" WARFRAME-MARKET ");

            Assert.Equal("available", response.State);
            Assert.All(response.Items, item => Assert.Equal("warframe-market", item.SourceId));
            Assert.Contains(response.Items, item => item.FieldPath == "quotes" && item.State == "Known");
            Assert.Contains(response.Items, item => item.FieldPath == "orders" && item.State == "Known");
            Assert.Contains(response.Items, item => item.FieldPath == "account" && item.State == "Known");
            Assert.Contains(response.Items, item => item.FieldPath == "marketItems" && item.State == "Known");
            Assert.All(response.Items.Where(item => item.State == "Known"), item => Assert.NotNull(item.ObservedAt));
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task PublicExportSearchReturnsNormalizedTechnicalAndRecipeFields()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-public-export-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            await using (var database = new SyncDatabase(Path.Combine(root, "data.db")))
                await database.PublishCatalogAsync(new SyncBatch("public-export", "mcp-catalog-rich", "[]", 1),
                [new PublicExportRecord("/Lotus/Test", "Test Weapon", "Weapon", "desc",
                    new Dictionary<string, string> { ["en"] = "Test Weapon", ["pt"] = "Arma de Teste" },
                    "{\"uniqueName\":\"/Lotus/Test\",\"name\":{\"en\":\"Test Weapon\",\"pt\":\"Arma de Teste\"},\"category\":\"Weapon\",\"productCategory\":\"LongGuns\",\"masterable\":true,\"tradable\":true,\"marketId\":\"set-id\",\"marketSlug\":\"test-weapon\",\"components\":[{\"uniqueName\":\"/Lotus/TestPart\",\"name\":\"Barrel\",\"itemCount\":2}],\"relics\":[{\"relicName\":\"A1\",\"rarity\":\"Rare\",\"chance\":10,\"rewardName\":\"Test Weapon\"}]}")]);

            var response = await new PlatformStatusService().SearchPublicExportAsync("Test Weapon");
            var item = Assert.Single(response.Items);
            Assert.Equal("published", response.State);
            Assert.Equal("LongGuns", item.ProductCategory);
            Assert.True(item.Masterable);
            Assert.True(item.Tradable);
            Assert.Equal("test-weapon", item.MarketSlug);
            Assert.Equal("Arma de Teste", item.Aliases["pt"]);
            Assert.Equal("/Lotus/TestPart", Assert.Single(item.Components).UniqueName);
            Assert.Equal("A1", Assert.Single(item.Relics).RelicName);

            var direct = await new PlatformStatusService().GetPublicExportItemAsync("Arma de Teste");
            Assert.Equal("published", direct.State);
            Assert.Equal("/Lotus/Test", direct.Item?.UniqueName);
            Assert.Equal("LongGuns", direct.Item?.ProductCategory);
            var byCategory = await new PlatformStatusService().SearchPublicExportAsync(category: "LongGuns");
            Assert.Single(byCategory.Items);
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task InventoryChangesCompareCompleteSnapshotsWithoutRawPayloads()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-inventory-changes-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            await using (var database = new SyncDatabase(Path.Combine(root, "data.db")))
            {
                var first = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(), 1,
                    DateTimeOffset.UtcNow.AddMinutes(-1), "native", "verified", "{}", "changes-1");
                var second = first with { EventId = Guid.NewGuid(), Sequence = 2, ReceivedAt = DateTimeOffset.UtcNow, ContentHash = "changes-2" };
                await database.PublishInventoryAsync(first, new InventoryProjection(
                    [new InventoryEquipmentRecord("instance", "/Lotus/Weapon", 10, null, InventoryFieldState.Known, InventoryFieldState.NotObserved, "{\"private\":true}"),
                     new InventoryEquipmentRecord("config-instance", "/Lotus/Weapon", 30, "{\"configSecret\":\"old\"}", InventoryFieldState.Known, InventoryFieldState.Known, "{}")],
                    [new InventoryStackableRecord("/Lotus/Resource", 2, InventoryFieldState.Known, "{}")], [], new Dictionary<string, InventoryFieldState>(),
                    [new InventoryUpgradeRecord("instance", "mods", "/Lotus/OldMod", 3, "{\"secret\":true}")]));
                await database.PublishInventoryAsync(second, new InventoryProjection(
                    [new InventoryEquipmentRecord("instance", "/Lotus/Weapon", 20, null, InventoryFieldState.Known, InventoryFieldState.NotObserved, "{\"private\":false}"),
                     new InventoryEquipmentRecord("config-instance", "/Lotus/Weapon", 30, "{\"configSecret\":\"new\"}", InventoryFieldState.Known, InventoryFieldState.Known, "{}")],
                    [new InventoryStackableRecord("/Lotus/Resource", 5, InventoryFieldState.Known, "{}"), new InventoryStackableRecord("/Lotus/New", 1, InventoryFieldState.Known, "{}")], [], new Dictionary<string, InventoryFieldState>(),
                    [new InventoryUpgradeRecord("instance", "mods", "/Lotus/NewMod", 5, "{\"secret\":false}")]));
            }

            var response = await new PlatformStatusService().GetInventoryChangesAsync();
            Assert.Equal("available", response.State);
            Assert.Equal(5, response.Items.Count);
            Assert.Contains(response.Items, value => value.Kind == "equipment" && value.Change == "changed" && value.AfterRank == 20);
            Assert.Contains(response.Items, value => value.Kind == "equipment" && value.Key == "config-instance" && value.Change == "changed");
            Assert.Contains(response.Items, value => value.Kind == "stackable" && value.Key == "/Lotus/New" && value.Change == "added");
            Assert.Contains(response.Items, value => value.Kind == "upgrade" && value.Change == "changed" && value.BeforeTypeId == "/Lotus/OldMod" && value.AfterTypeId == "/Lotus/NewMod" && value.AfterRank == 5);
            Assert.DoesNotContain("private", JsonSerializer.Serialize(response), StringComparison.Ordinal);
            Assert.DoesNotContain("secret", JsonSerializer.Serialize(response), StringComparison.Ordinal);
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task InventoryChangesRejectsDifferentContexts()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-inventory-context-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            await using (var database = new SyncDatabase(Path.Combine(root, "data.db")))
            {
                var first = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(), 1,
                    DateTimeOffset.UtcNow.AddMinutes(-1), "native", "verified", "{}", "context-1", "snapshot", "account-a");
                var second = first with { EventId = Guid.NewGuid(), Sequence = 2, ReceivedAt = DateTimeOffset.UtcNow,
                    ContentHash = "context-2", ContextId = "account-b" };
                var projection = new InventoryProjection(
                    [new InventoryEquipmentRecord("instance", "/Lotus/Weapon", 10, null,
                        InventoryFieldState.Known, InventoryFieldState.NotObserved, "{}")], [], [],
                    new Dictionary<string, InventoryFieldState>());
                await database.PublishInventoryAsync(first, projection);
                await database.PublishInventoryAsync(second, projection);
            }

            var response = await new PlatformStatusService().GetInventoryChangesAsync();
            Assert.Equal("context_mismatch", response.State);
            Assert.Empty(response.Items);
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task ReferenceSearchReadsOnlyValidatedLocalDocumentsAndPreservesAttribution()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-references-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            Directory.CreateDirectory(Path.Combine(root, "references"));
            File.WriteAllText(Path.Combine(root, "references", "build.json"), "{\"kind\":\"overframe\",\"url\":\"https://overframe.gg/build/123\",\"title\":\"Test build\",\"revision\":\"r1\",\"license\":\"community\",\"author\":\"tester\",\"sections\":[{\"id\":\"mods\",\"title\":\"Mods\",\"content\":\"Use Serration for fire rate.\"}]}");
            File.WriteAllText(Path.Combine(root, "references", "rejected.json"), "{\"kind\":\"wiki\",\"url\":\"https://example.com/not-allowed\",\"title\":\"bad\",\"revision\":\"r1\",\"sections\":[]}");

            var response = await new PlatformStatusService().SearchReferencesAsync("fire rate");

            Assert.Equal("partial", response.State);
            Assert.Equal(1, response.Documents);
            Assert.Equal(1, response.RejectedDocuments);
            var hit = Assert.Single(response.Hits);
            Assert.Equal("Overframe", hit.Kind);
            Assert.Equal("community", hit.License);
            Assert.Equal("tester", hit.Author);
            Assert.False(hit.TrustedForFacts);
            var section = await new PlatformStatusService().GetReferenceSectionAsync(hit.Url, hit.SectionId, hit.Revision);
            Assert.Equal("available", section.State);
            Assert.Equal("Use Serration for fire rate.", section.Content);
            Assert.Equal("community", section.License);
            Assert.False(section.TrustedForFacts);
            var status = await new PlatformStatusService().GetSyncStatusAsync();
            var referenceStatus = Assert.Single(status.Sources, value => value.SourceId == "references");
            Assert.Equal("partial", referenceStatus.State);
            Assert.Equal("reference-file-1", referenceStatus.ParserVersion);
            Assert.Equal(1, referenceStatus.AcceptedRecords);
            Assert.Equal(1, referenceStatus.RejectedRecords);
            var coverage = await new PlatformStatusService().GetSourceCoverageAsync("references");
            Assert.Equal("partial", coverage.State);
            Assert.NotNull(coverage.ServedAt);
            Assert.Null(coverage.ActiveRevisionId);
            Assert.Equal("Known", Assert.Single(coverage.Items, value => value.FieldPath == "documents").State);
            Assert.Equal("Known", Assert.Single(coverage.Items, value => value.FieldPath == "attribution").State);
            Assert.Equal("Invalid", Assert.Single(coverage.Items, value => value.FieldPath == "rejectedDocuments").State);
        }
        finally { Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task OverframeToolReadsExactTypedSqliteKeyWithoutRefreshingNetwork()
    {
        var previous = Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), $"myframe-mcp-overframe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", root);
            var now = DateTimeOffset.UtcNow;
            var store = new OverframeCacheStore(Path.Combine(root, "data.db"));
            await store.UpsertAsync(new("item:haalvu", OverframeEntityType.Item, "haalvu", "Haalvu",
                new Uri("https://overframe.gg/items/arsenal/8015/haalvu/"),
                "{\"type\":\"Item\",\"name\":\"Haalvu\",\"trustedForFacts\":false}", "hash",
                OverframePageParser.ParserVersion, now, now.AddHours(8)));

            var response = await new PlatformStatusService().GetOverframeReferenceAsync("Item", "Haalvu");
            var missing = await new PlatformStatusService().GetOverframeReferenceAsync("Mod", "Haalvu");

            Assert.Equal("available", response.State);
            Assert.Equal("item:haalvu", response.CacheKey);
            Assert.Equal("Haalvu", response.Reference!.Name);
            Assert.False(response.Reference.TrustedForFacts);
            Assert.Equal("not_cached", missing.State);
            Assert.Null(missing.Reference);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", previous);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task MarketCredentialStatusNeverReturnsCredentialValue()
    {
        var response = await new PlatformStatusService(new EmptyTokenStore()).GetMarketCredentialStatusAsync();

        Assert.Equal("missing", response.State);
        Assert.False(response.IsConfigured);
        Assert.Null(response.ExpiresAt);
    }

    [Fact]
    public void EveryToolIsExplicitlyReadOnlyAndHasThePlannedName()
    {
        var methods = typeof(MyFrameTools).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(x => x.Attribute is not null).ToArray();
        var expected = new[] { "get_acquisition", "get_activity", "get_bounties", "get_capabilities", "get_capture_inbox_status", "get_equipment", "get_inventory_changes", "get_inventory_coverage", "get_inventory_history", "get_item", "get_loadout", "get_market_credential_status", "get_mods", "get_overframe_reference", "get_overview", "get_public_export_item", "get_reference_section", "get_source_coverage", "get_sync_history", "get_sync_status", "get_world_state", "list_collection", "list_farm", "list_relics", "list_sales", "list_surplus", "search_inventory", "search_public_export", "search_references" };

        Assert.Equal(expected, methods.Select(x => x.Attribute!.Name).Order(StringComparer.Ordinal));
        Assert.All(methods, value =>
        {
            Assert.True(value.Attribute!.ReadOnly);
            Assert.False(value.Attribute.Destructive);
            Assert.True(value.Attribute.Idempotent);
            Assert.False(value.Attribute.OpenWorld);
            Assert.True(value.Attribute.UseStructuredContent);
            Assert.NotNull(value.Method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>());
        });
    }

    [Fact]
    public void McpDocumentationListsEveryRegisteredTool()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "docs", "MCP.md")))
            root = root.Parent;
        Assert.NotNull(root);
        var documentation = File.ReadAllText(Path.Combine(root!.FullName, "docs", "MCP.md"));
        var tools = typeof(MyFrameTools).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name);
        Assert.All(tools, name => Assert.Contains($"`{name}`", documentation, StringComparison.Ordinal));
    }

    [Fact]
    public void CursorIsSignedAndBoundToItsQuery()
    {
        var codec = new CursorCodec(JsonOptions());
        var cursor = codec.Encode("search_inventory", "snapshot", "query", 50);

        Assert.Equal(50, codec.Decode(cursor, "search_inventory", "query").Offset);
        Assert.Throws<QueryProblemException>(() => codec.Decode(cursor, "search_inventory", "another"));
        Assert.Throws<QueryProblemException>(() => codec.Decode(cursor[..^1] + "x", "search_inventory", "query"));
    }

    [Fact]
    public void ToolSchemasUsePortableNullableKeyword()
    {
        var tools = StrictToolRegistration.Create(JsonOptions());

        foreach (var tool in tools)
        {
            Assert.Empty(TypeArrayPaths(tool.ProtocolTool.InputSchema));
            if (tool.ProtocolTool.OutputSchema is { } output)
                Assert.Empty(TypeArrayPaths(output));
        }
    }

    [Fact]
    public async Task DashboardAndMcpReadTheSameSnapshotProvider()
    {
        var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
            new Dictionary<string, int> { ["/parity/item"] = 7 },
            new HashSet<string>(), new Dictionary<string, long>(), 1, 2, "parity");
        var item = CatalogItem("/parity/item", "Parity Item");
        var snapshot = Snapshot("parity-snapshot", inventory, [item]);
        var provider = new FakeProvider(snapshot);
        using var folder = new TemporaryFolder();
        using var dashboard = new DashboardService(new ParityPath(folder.Path),
            new EmptyInventoryReader(), new EmptyCatalogReader(), new EmptyMarket(),
            new EmptyPriceCache(), new EmptyMarketState(), new EmptyMarketItems(),
            new RecommendationEngine(), snapshotProvider: provider);

        var uiSnapshot = await dashboard.RefreshAsync(refreshPrices: false);
        var mcpPage = await Service(provider).SearchInventoryAsync(null, null, null, null,
            "all", false, 50, null, null, default);

        var mcpItem = Assert.Single(mcpPage.Items);
        Assert.Equal(uiSnapshot.Inventory.Stackables["/parity/item"], mcpItem.Quantity);
        Assert.Equal(uiSnapshot.Inventory.Stackables.Count, mcpPage.TotalCount);
        Assert.Equal(item.Name, mcpItem.Name);
    }

    [Fact]
    public async Task CursorKeepsTheOriginalSnapshotWhenCurrentDataChanges()
    {
        var oldSnapshot = Snapshot("old", ("/a", "Alpha"), ("/b", "Beta"));
        var newSnapshot = Snapshot("new", ("/c", "Changed"));
        var provider = new FakeProvider(oldSnapshot, newSnapshot);
        var service = Service(provider);

        var first = await service.SearchInventoryAsync(null, null, null, null, "all", false,
            1, null, null, default);
        provider.Current = newSnapshot;
        var second = await service.SearchInventoryAsync(null, null, null, null, "all", false,
            1, first.NextCursor, null, default);

        Assert.Equal("old", first.Meta.SnapshotId);
        Assert.Equal("old", second.Meta.SnapshotId);
        Assert.Equal("Alpha", Assert.Single(first.Items).Name);
        Assert.Equal("Beta", Assert.Single(second.Items).Name);
    }

    [Fact]
    public async Task EquipmentPresenceDoesNotInventAQuantity()
    {
        var item = CatalogItem("/frame", "Frame");
        var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
            new Dictionary<string, int>(), new HashSet<string> { item.UniqueName },
            new Dictionary<string, long>(), 1, 2, "synthetic");
        var snapshot = Snapshot("snapshot", inventory, [item]);
        var service = Service(new FakeProvider(snapshot));

        var page = await service.SearchInventoryAsync(null, null, null, null, "all", false,
            50, null, null, default);

        var result = Assert.Single(page.Items);
        Assert.True(result.Owned);
        Assert.False(result.QuantityKnown);
        Assert.Null(result.Quantity);
    }

    [Fact]
    public async Task ItemComponentsUseSnapshotBoundSectionPagination()
    {
        var item = new CatalogItem("/frame", "Frame", "Warframes", "Suits", "", true,
            false, false, false, null, null, null,
            [new("/a", "Blueprint", 1, 0, false), new("/b", "Systems", 1, 0, false)], []);
        var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
            new Dictionary<string, int> { ["/a"] = 1, ["/b"] = 1 }, new HashSet<string>(),
            new Dictionary<string, long>(), 1, 2, "synthetic");
        var service = Service(new FakeProvider(Snapshot("snapshot", inventory, [item])));

        var first = await service.GetItemAsync(item.UniqueName, "components", 1, null, null, default);
        var second = await service.GetItemAsync(item.UniqueName, "components", 1,
            first.NextCursor, null, default);

        Assert.Equal(2, first.TotalCount);
        Assert.Equal("/a", Assert.Single(first.Item!.Components).ItemId);
        Assert.Equal("/b", Assert.Single(second.Item!.Components).ItemId);
        Assert.Equal(first.Meta.SnapshotId, second.Meta.SnapshotId);
        Assert.NotNull(first.NextCursor);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task ItemDetailsExposeOptionalCatalogDescription()
    {
        var item = new CatalogItem("/frame", "Frame", "Warframes", "Suits", "", true,
            false, false, false, null, null, null, [], [], Description: "Attributed catalog text");
        var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
            new Dictionary<string, int>(), new HashSet<string>(), new Dictionary<string, long>(), 1, 2, "synthetic");
        var response = await Service(new FakeProvider(Snapshot("snapshot", inventory, [item])))
            .GetItemAsync(item.UniqueName, "summary", 50, null, null, default);

        Assert.Equal("Attributed catalog text", response.Item!.Description);
    }

    [Fact]
    public async Task SurplusTotalsExcludeComponentsAllocatedToCompleteSets()
    {
        var item = new CatalogItem("/set", "Test Prime", "Warframes", "Suits", "", true,
            true, true, false, null, "set-id", "test_prime_set",
            [new("/bp", "Blueprint", 1, 45, true), new("/systems", "Systems", 1, 15, true)], []);
        var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
            new Dictionary<string, int> { ["/bp"] = 2, ["/systems"] = 2 },
            new HashSet<string> { item.UniqueName }, new Dictionary<string, long>(), 1, 2, "synthetic");
        var now = DateTimeOffset.UtcNow;
        var quotes = new Dictionary<string, MarketQuote>
        {
            ["test_prime_set"] = new("test_prime_set", 100, 90, now),
            ["test_prime_blueprint"] = new("test_prime_blueprint", 10, 9, now),
            ["test_prime_systems"] = new("test_prime_systems", 20, 18, now)
        };
        var market = new Dictionary<string, MarketIdentity>
        {
            [ItemNameNormalizer.Normalize("Test Prime Blueprint")] = new("bp", "test_prime_blueprint"),
            [ItemNameNormalizer.Normalize("Test Prime Systems")] = new("systems", "test_prime_systems")
        };
        var service = Service(new FakeProvider(Snapshot("snapshot", inventory, [item], quotes, market,
            new RecommendationSettings(10, 0))));

        var page = await service.ListSurplusAsync("all", null, null, 50, null, null, default);

        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, value =>
        {
            Assert.Equal(2, value.AllocatedToSets);
            Assert.Equal(0, value.AvailableToSell);
            Assert.Equal(0, value.TotalPlatinum);
            Assert.Equal(0, value.TotalDucats);
        });
        Assert.Equal(0, page.Totals.Platinum);
        Assert.Equal(0, page.Totals.Ducats);
    }

    [Fact]
    public async Task MissingSetupStillReturnsAnOverview()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new MyFrameSnapshot("setup", now, now, null, null, null, null, [],
            new Dictionary<string, MarketQuote>(), null,
            new Dictionary<string, SnapshotSource> { ["settings"] = new("missing", null) },
            [new("SETUP_REQUIRED", "Open My Frame.")], true, false, now.AddSeconds(30));
        var service = Service(new FakeProvider(snapshot));

        var overview = await service.GetOverviewAsync(false, null, default);

        Assert.True(overview.Overview.SetupRequired);
        Assert.Contains(overview.Meta.Warnings, x => x.Code == "SETUP_REQUIRED");
    }

    [Fact]
    public async Task QueryArgumentsRejectInvalidEnumsAndNegativeMinimums()
    {
        var service = Service(new FakeProvider(Snapshot("snapshot", ("/a", "Alpha"))));

        var entityError = await Assert.ThrowsAsync<QueryProblemException>(() =>
            service.SearchInventoryAsync(null, "invented", null, null, "all", false,
                50, null, null, default));
        var quantityError = await Assert.ThrowsAsync<QueryProblemException>(() =>
            service.SearchInventoryAsync(null, null, null, -1, "all", false,
                50, null, null, default));
        var relicError = await Assert.ThrowsAsync<QueryProblemException>(() =>
            service.ListRelicsAsync("all", null, -1, null, null,
                50, null, null, default));

        Assert.Equal("INVALID_ARGUMENT", entityError.Code);
        Assert.Equal("INVALID_ARGUMENT", quantityError.Code);
        Assert.Equal("INVALID_ARGUMENT", relicError.Code);
    }

    [Fact]
    public async Task ExecutionGateRejectsCallsBeyondRunningAndQueuedCapacity()
    {
        var gate = new QueryExecutionGate(1, 1, TimeSpan.FromSeconds(5));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gate.RunAsync(async token =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return 1;
        }, default);
        await entered.Task;
        var second = gate.RunAsync(async token =>
        {
            await release.Task.WaitAsync(token);
            return 2;
        }, default);

        var error = await Assert.ThrowsAsync<QueryProblemException>(() =>
            gate.RunAsync(_ => Task.FromResult(3), default));

        Assert.Equal("SERVER_BUSY", error.Code);
        release.SetResult();
        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
    }

    [Fact]
    public async Task ExecutionGateTurnsItsProcessingDeadlineIntoTypedTimeout()
    {
        var gate = new QueryExecutionGate(1, 0, TimeSpan.FromMilliseconds(30));

        var error = await Assert.ThrowsAsync<QueryProblemException>(() => gate.RunAsync(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }, default));

        Assert.Equal("TIMEOUT", error.Code);
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task RealStdioServerListsAndCallsStructuredCapabilities()
    {
        var server = ResolveMcpServerPath();
        Assert.True(File.Exists(server), $"Server was not built at {server}");
        using var data = new TemporaryFolder();
        await using (var database = new SyncDatabase(Path.Combine(data.Path, "data.db")))
        {
            var payload = "{\"equipment\":[{\"instanceId\":\"instance-f33\",\"typeId\":\"/Lotus/Weapon\",\"rank\":30,\"config\":{\"mods\":[\"/Lotus/Mod\"]}}],\"RawUpgrades\":[{\"instanceId\":\"instance-f33\",\"uniqueName\":\"/Lotus/Mod\",\"rank\":5}]}";
            var envelope = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(),
                1, DateTimeOffset.UtcNow, "test", "verified", payload, "f33-hash");
            var projection = InventoryPayloadParser.Parse(payload);
            await database.PublishInventoryAsync(envelope, projection);
            await database.PublishCatalogAsync(new SyncBatch("public-export", "catalog-f33", "[]", 1,
                "public-export-aggregate-1"),
                [new PublicExportRecord("/Lotus/Weapon", "Test Weapon", "Weapon", null,
                    new Dictionary<string, string>(),
                    "{\"uniqueName\":\"/Lotus/Weapon\",\"name\":\"Test Weapon\",\"components\":[{\"uniqueName\":\"/Lotus/Part\",\"name\":\"Test Part\",\"itemCount\":2,\"ducats\":15,\"tradable\":true}],\"relics\":[{\"relicName\":\"Lith A1\",\"rarity\":\"Rare\",\"chance\":0.1,\"rewardName\":\"Test Weapon\"}]}" )]);
            var bounty = new WorldStateBounty("deimos-f33", "Entrati", DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddMinutes(55), [new WorldStateJob("job-f33", "Sample bounty", null, 3,
                    [100], [new WorldStateReward("Endo", 50, 100, "Common"), new WorldStateReward("Test Weapon", null, 1, "Rare")])]);
            var world = new WorldStateSnapshot(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "fixture",
                [bounty], [new WorldStateCycle("cetusCycle", "day", DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(50))], "world-f33", true,
                new Dictionary<string, InventoryFieldState> { ["motherTokens"] = InventoryFieldState.NotObserved });
            await database.PublishWorldStateAsync(world, new SyncBatch("worldstate-pc", "world-f33", "{}", 1, "worldstate-official-1"));
        }
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["MYFRAME_DATA_ROOT"] = data.Path;
        var stderr = new List<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "my-frame-test",
            Command = server,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            StandardErrorLines = line => stderr.Add(line),
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        });
        await using var client = await McpClient.CreateAsync(transport);
        output.WriteLine($"Negotiated MCP protocol: {client.NegotiatedProtocolVersion}");

        var tools = await client.ListToolsAsync();
        var resources = await client.ListResourcesAsync();
        var prompts = await client.ListPromptsAsync();
        var concurrentLocalRead = Task.Run(async () =>
        {
            await using var localDatabase = new SyncDatabase(Path.Combine(data.Path, "data.db"));
            for (var index = 0; index < 10; index++)
            {
                var localBounties = await localDatabase.GetCurrentWorldStateBountiesAsync(DateTimeOffset.UtcNow);
                Assert.Single(localBounties);
                await Task.Delay(10);
            }
        });
        var concurrentMcpRead = client.CallToolAsync("get_activity",
            new Dictionary<string, object?> { ["syndicate"] = "Entrati" }).AsTask();
        await Task.WhenAll(concurrentLocalRead, concurrentMcpRead);
        var concurrentResult = await concurrentMcpRead;
        Assert.NotEqual(true, concurrentResult.IsError);
        Assert.Contains("deimos-f33", JsonSerializer.Serialize(concurrentResult.StructuredContent));
        var result = await client.CallToolAsync("get_overview",
            new Dictionary<string, object?> { ["includeAccount"] = false });
        var coverageResult = await client.CallToolAsync("get_inventory_coverage");
        var equipmentResult = await client.CallToolAsync("get_equipment");
        var modsResult = await client.CallToolAsync("get_mods",
            new Dictionary<string, object?> { ["ownerInstanceId"] = "instance-f33" });
        var loadoutResult = await client.CallToolAsync("get_loadout",
            new Dictionary<string, object?> { ["typeId"] = "/Lotus/Weapon" });
        var acquisitionResult = await client.CallToolAsync("get_acquisition",
            new Dictionary<string, object?> { ["itemId"] = "/Lotus/Weapon" });
        var bountiesResult = await client.CallToolAsync("get_bounties");
        var rewardBountiesResult = await client.CallToolAsync("get_bounties",
            new Dictionary<string, object?> { ["reward"] = "endo" });
        var syncStatusResult = await client.CallToolAsync("get_sync_status");
        var activityResult = await client.CallToolAsync("get_activity",
            new Dictionary<string, object?> { ["syndicate"] = "Entrati" });
        var rewardActivityResult = await client.CallToolAsync("get_activity",
            new Dictionary<string, object?> { ["reward"] = "endo" });
        var worldStateResult = await client.CallToolAsync("get_world_state",
            new Dictionary<string, object?> { ["limit"] = 50, ["syndicate"] = "entrati" });
        var filteredWorldState = await client.CallToolAsync("get_world_state",
            new Dictionary<string, object?> { ["syndicate"] = "Ostrons" });
        var invalidWorldState = await client.CallToolAsync("get_world_state",
            new Dictionary<string, object?> { ["limit"] = 0 });
        var invalid = await client.CallToolAsync("get_overview",
            new Dictionary<string, object?> { ["unexpected"] = true });
        var expired = await client.CallToolAsync("search_inventory",
            new Dictionary<string, object?> { ["snapshotId"] = "not-retained-synthetic-snapshot" });
        var unavailable = await client.CallToolAsync("search_inventory",
            new Dictionary<string, object?>());

        Assert.Equal(28, tools.Count);
        Assert.All(tools, tool =>
        {
            Assert.Equal(JsonValueKind.Object, tool.ProtocolTool.InputSchema.ValueKind);
            Assert.True(tool.ProtocolTool.InputSchema.TryGetProperty("properties", out _));
            Assert.True(tool.ProtocolTool.InputSchema.TryGetProperty("additionalProperties", out var additional));
            Assert.Equal(JsonValueKind.False, additional.ValueKind);
            Assert.NotNull(tool.ProtocolTool.OutputSchema);
        });
        Assert.Contains(resources, x => x.Uri == "myframe://overview");
        Assert.Contains(resources, x => x.Uri == "myframe://schema");
        Assert.Contains(prompts, x => x.Name == "review_inventory");
        Assert.NotNull(result.StructuredContent);
        Assert.NotEqual(true, coverageResult.IsError);
        Assert.NotEqual(true, acquisitionResult.IsError);
        var acquisitionJson = JsonSerializer.Serialize(acquisitionResult.StructuredContent);
        Assert.Contains("available", acquisitionJson);
        Assert.Contains("catalog.components", acquisitionJson);
        Assert.Contains("worldstate.bountyRewards", acquisitionJson);
        Assert.Contains("Test Part", acquisitionJson);
        Assert.Contains("Lith A1", acquisitionJson);
        Assert.Contains("deimos-f33", acquisitionJson);
        Assert.NotNull(coverageResult.StructuredContent);
        Assert.NotEqual(true, equipmentResult.IsError);
        Assert.NotNull(equipmentResult.StructuredContent);
        Assert.Contains("instance-f33", JsonSerializer.Serialize(equipmentResult.StructuredContent));
        Assert.NotEqual(true, modsResult.IsError);
        Assert.NotNull(modsResult.StructuredContent);
        Assert.Contains("/Lotus/Mod", JsonSerializer.Serialize(modsResult.StructuredContent));
        Assert.NotEqual(true, loadoutResult.IsError);
        Assert.NotNull(loadoutResult.StructuredContent);
        Assert.Contains("/Lotus/Mod", JsonSerializer.Serialize(loadoutResult.StructuredContent));
        Assert.NotEqual(true, bountiesResult.IsError);
        Assert.NotEqual(true, rewardBountiesResult.IsError);
        Assert.NotNull(bountiesResult.StructuredContent);
        Assert.NotEqual(true, syncStatusResult.IsError);
        var syncStatusJson = JsonSerializer.Serialize(syncStatusResult.StructuredContent);
        Assert.Contains("activeRevisionId", syncStatusJson);
        Assert.Contains("worldstate-official-1", syncStatusJson);
        Assert.Contains("acceptedRecords", syncStatusJson);
        Assert.Contains("warframe-market", syncStatusJson);
        var bountiesJson = JsonSerializer.Serialize(bountiesResult.StructuredContent);
        Assert.Contains("available", bountiesJson);
        Assert.Contains("Entrati", bountiesJson);
        Assert.Contains("Endo", bountiesJson);
        Assert.NotEqual(true, worldStateResult.IsError);
        Assert.NotEqual(true, activityResult.IsError);
        Assert.Contains("deimos-f33", JsonSerializer.Serialize(rewardBountiesResult.StructuredContent));
        Assert.NotEqual(true, rewardActivityResult.IsError);
        Assert.Contains("deimos-f33", JsonSerializer.Serialize(activityResult.StructuredContent));
        Assert.Contains("deimos-f33", JsonSerializer.Serialize(rewardActivityResult.StructuredContent));
        var worldStateJson = JsonSerializer.Serialize(worldStateResult.StructuredContent);
        Assert.Contains("cetusCycle", worldStateJson);
        Assert.Contains("Entrati", worldStateJson);
        Assert.Contains("motherTokens", worldStateJson);
        Assert.Contains("activeRevisionId", worldStateJson);
        var filteredWorldStateJson = JsonSerializer.Serialize(filteredWorldState.StructuredContent);
        Assert.DoesNotContain("deimos-f33", filteredWorldStateJson);
        Assert.True(invalidWorldState.IsError);
        Assert.NotEqual(true, result.IsError);
        Assert.True(invalid.IsError);
        Assert.Contains(invalid.Content.OfType<TextContentBlock>(),
            content => content.Text.Contains("INVALID_ARGUMENT", StringComparison.Ordinal));
        AssertTextOnlyError(expired, "SNAPSHOT_EXPIRED", "Retryable=false");
        Assert.NotEqual(true, unavailable.IsError);
        Assert.Contains("Check isError", client.ServerInstructions);
        Assert.Contains(result.Content.OfType<TextContentBlock>(), x => x.Text.Contains("snapshotId", StringComparison.Ordinal));
        Assert.DoesNotContain(stderr, line => line.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.All(Directory.EnumerateFileSystemEntries(data.Path), path =>
            Assert.Contains(Path.GetFileName(path), new[] { "data.db", "data.db-shm", "data.db-wal" }));
    }

    [Fact]
    public async Task ActiveMcpServerReopensMigratedLegacyDatabase()
    {
        var server = ResolveMcpServerPath();
        Assert.True(File.Exists(server), $"Server was not built at {server}");

        using var data = new TemporaryFolder();
        var databasePath = Path.Combine(data.Path, "data.db");
        await using (var database = new SyncDatabase(databasePath))
        {
            await database.InitializeAsync();
            await database.PublishAsync(new SyncBatch("worldstate-pc", "legacy-hash", "{}", 0, "legacy-parser-1"));
        }
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["MYFRAME_DATA_ROOT"] = data.Path;
        var stderr = new List<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "my-frame-legacy-upgrade-test",
            Command = server,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            StandardErrorLines = line => stderr.Add(line),
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        });
        await using var client = await McpClient.CreateAsync(transport);

        var migrated = await client.CallToolAsync("get_sync_status");
        Assert.NotEqual(true, migrated.IsError);
        var migratedJson = JsonSerializer.Serialize(migrated.StructuredContent);
        Assert.Contains("legacy-parser-1", migratedJson);
        Assert.Contains("worldstate-pc", migratedJson);

        var reopened = await client.CallToolAsync("get_sync_status");
        Assert.NotEqual(true, reopened.IsError);
        var reopenedJson = JsonSerializer.Serialize(reopened.StructuredContent);
        Assert.Contains("legacy-parser-1", reopenedJson);
        Assert.Contains("worldstate-pc", reopenedJson);

        await client.DisposeAsync();
        await Task.Delay(2_000);
    }

    [Fact]
    public async Task TwoRealStdioServersServeConcurrentCallsWithinBudget()
    {
        var server = ResolveMcpServerPath();
        Assert.True(File.Exists(server), $"Server was not built at {server}");

        using var firstData = new TemporaryFolder();
        using var secondData = new TemporaryFolder();
        var firstEnvironment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        firstEnvironment["MYFRAME_DATA_ROOT"] = firstData.Path;
        var secondEnvironment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        secondEnvironment["MYFRAME_DATA_ROOT"] = secondData.Path;
        var firstTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "my-frame-concurrent-1", Command = server, InheritEnvironmentVariables = false,
            EnvironmentVariables = firstEnvironment, ShutdownTimeout = TimeSpan.FromSeconds(5)
        });
        var secondTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "my-frame-concurrent-2", Command = server, InheritEnvironmentVariables = false,
            EnvironmentVariables = secondEnvironment, ShutdownTimeout = TimeSpan.FromSeconds(5)
        });
        await using var first = await McpClient.CreateAsync(firstTransport);
        await using var second = await McpClient.CreateAsync(secondTransport);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var firstCall = first.CallToolAsync("get_capabilities").AsTask();
        var secondCall = second.CallToolAsync("get_sync_status").AsTask();
        await Task.WhenAll(firstCall, secondCall);
        stopwatch.Stop();
        var firstResult = await firstCall;
        var secondResult = await secondCall;

        Assert.NotEqual(true, firstResult.IsError);
        Assert.NotEqual(true, secondResult.IsError);
        Assert.NotNull(firstResult.StructuredContent);
        Assert.NotNull(secondResult.StructuredContent);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Concurrent stdio calls exceeded 5 seconds: {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");

        await first.DisposeAsync();
        await second.DisposeAsync();
        await Task.Delay(2_000);
    }

    [Fact]
    public async Task ValidSearchWithNoMatchesIsAnEmptyPageNotAnError()
    {
        var service = Service(new FakeProvider(Snapshot("snapshot", ("/a", "Alpha"))));

        var page = await service.SearchInventoryAsync("no-such-synthetic-item", null, null,
            null, "all", false, 50, null, null, default);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Count);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal("snapshot", page.Meta.SnapshotId);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task WarmSearchHandlesTwentyThousandInventoryEntriesWithinBudget()
    {
        var values = Enumerable.Range(0, 20_000)
            .Select(index => ($"/synthetic/item-{index:D5}", $"Synthetic Item {index:D5}"))
            .ToArray();
        var service = Service(new FakeProvider(Snapshot("large-snapshot", values)));

        var warm = await service.SearchInventoryAsync(null, null, null, null,
            "all", false, 100, null, null, default);
        Assert.Equal(20_000, warm.TotalCount);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        PageResponse<InventoryItemDto>? page = null;
        for (var index = 0; index < 5; index++)
            page = await service.SearchInventoryAsync(null, null, null, null,
                "all", false, 100, null, null, default);
        stopwatch.Stop();

        Assert.NotNull(page);
        Assert.Equal(100, page!.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Warm 20k search exceeded 5 seconds: {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.True(JsonSerializer.Serialize(page).Length < 128 * 1024,
            "The paged response exceeded the 128 KiB response budget.");
    }

    private static void AssertTextOnlyError(CallToolResult result, string code, string retryable)
    {
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Contains(result.Content.OfType<TextContentBlock>(), content =>
            content.Text.Contains(code, StringComparison.Ordinal) &&
            content.Text.Contains(retryable, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> TypeArrayPaths(JsonElement schema)
    {
        var paths = new List<string>();
        Walk(schema, "$", paths);
        return paths;

        static void Walk(JsonElement value, string path, List<string> matches)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject())
                {
                    var propertyPath = $"{path}.{property.Name}";
                    if (property.NameEquals("type") && property.Value.ValueKind == JsonValueKind.Array)
                        matches.Add(propertyPath);
                    Walk(property.Value, propertyPath, matches);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    Walk(item, $"{path}[{index++}]", matches);
            }
        }
    }

    private static string ResolveMcpServerPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("MYFRAME_MCP_TEST_SERVER");
        if (!string.IsNullOrWhiteSpace(configuredPath)) return configuredPath;

        var configuration = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Name ?? "Debug";
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "MyFrame.Mcp", "bin", configuration, "net10.0", "win-x64", "MyFrame.Mcp.exe"));
    }

    private static MyFrameQueryService Service(IMyFrameSnapshotProvider provider)
    {
        var json = JsonOptions();
        return new(provider, new CursorCodec(json), json, TimeProvider.System);
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static MyFrameSnapshot Snapshot(string id, params (string Id, string Name)[] values)
    {
        var items = values.Select(value => CatalogItem(value.Id, value.Name)).ToArray();
        var inventory = new InventorySnapshot(DateTimeOffset.UtcNow,
            values.ToDictionary(x => x.Id, _ => 1), new HashSet<string>(),
            new Dictionary<string, long>(), 1, 2, "synthetic");
        return Snapshot(id, inventory, items);
    }

    private static MyFrameSnapshot Snapshot(string id, InventorySnapshot inventory, CatalogItem[] items,
        IReadOnlyDictionary<string, MarketQuote>? quotes = null,
        IReadOnlyDictionary<string, MarketIdentity>? market = null,
        RecommendationSettings? recommendationSettings = null)
    {
        var now = DateTimeOffset.UtcNow;
        var catalog = new CatalogSnapshot(items, items.ToDictionary(x => x.UniqueName),
            market ?? new Dictionary<string, MarketIdentity>());
        quotes ??= new Dictionary<string, MarketQuote>();
        var recommendations = new RecommendationEngine().Evaluate(inventory, catalog,
            quotes, [], recommendationSettings ?? new());
        var settings = new MyFrameSettingsDocument(1, 1, "synthetic", 10, 1, now);
        return new(id, now, now, inventory, catalog, recommendations, null, [],
            quotes, settings,
            new Dictionary<string, SnapshotSource>
            {
                ["settings"] = new("valid", now), ["inventory"] = new("valid", now),
                ["catalog"] = new("valid", now), ["prices"] = new("missing", null),
                ["orders"] = new("missing", null)
            }, [], false, false, now.AddMinutes(1));
    }

    private static CatalogItem CatalogItem(string id, string name) => new(id, name, "Items", "",
        "", false, false, false, false, null, null, null, [], []);

    private sealed class ParityPath(string directory) : IAlecaFramePath
    {
        public string DirectoryPath { get; private set; } = directory;
        public event EventHandler<string>? Changed;
        public void SetDirectory(string directoryPath)
        {
            DirectoryPath = directoryPath;
            Changed?.Invoke(this, directoryPath);
        }
    }

    private sealed class EmptyInventoryReader : IAlecaFrameReader
    {
        public Task<InventorySnapshot> ReadAsync(string alecaDirectory, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Snapshot provider should supply inventory for this parity test.");
    }

    private sealed class EmptyCatalogReader : IAlecaCatalogReader
    {
        public Task<CatalogSnapshot> LoadAsync(string alecaDirectory, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Snapshot provider should supply catalog for this parity test.");
    }

    private sealed class EmptyMarket : IWarframeMarketClient
    {
        public Task<MarketAccount?> GetAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult<MarketAccount?>(null);
        public Task<IReadOnlyList<MarketOrder>> GetMyOrdersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MarketOrder>>([]);
        public Task<MarketQuote?> GetTopOrdersAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult<MarketQuote?>(null);
        public Task<MarketItemIndex?> GetItemIndexAsync(CancellationToken cancellationToken = default) => Task.FromResult<MarketItemIndex?>(null);
    }

    private sealed class EmptyPriceCache : IPriceCache
    {
        public Task<MarketQuote?> GetAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult<MarketQuote?>(null);
        public Task SetAsync(MarketQuote quote, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyMarketState : IMarketStateStore
    {
        public Task<MarketState?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<MarketState?>(null);
        public Task SaveAsync(MarketState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyMarketItems : IMarketItemIndexStore
    {
        public Task<MarketItemIndex?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<MarketItemIndex?>(null);
        public Task SaveAsync(MarketItemIndex index, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyTokenStore : IMarketTokenStore
    {
        public Task<string?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class FakeProvider(params MyFrameSnapshot[] snapshots) : IMyFrameSnapshotProvider
    {
        private readonly Dictionary<string, MyFrameSnapshot> _values = snapshots.ToDictionary(x => x.SnapshotId);
        public MyFrameSnapshot Current { get; set; } = snapshots[0];
        public Task<MyFrameSnapshot> GetAsync(string? snapshotId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshotId is null ? Current : _values[snapshotId]);
        public void Invalidate() { }
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "my-frame-mcp-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose()
        {
            for (var attempt = 0; attempt < 50 && Directory.Exists(Path); attempt++)
            {
                try
                {
                    Directory.Delete(Path, true);
                }
                catch (IOException) when (attempt < 49)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException) when (attempt < 49)
                {
                    Thread.Sleep(100);
                }
                catch (IOException)
                {
                    // A child process can keep the SQLite handle briefly after stdio shutdown.
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
        }
    }
}
