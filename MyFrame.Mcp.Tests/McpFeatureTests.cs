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
    public void EveryToolIsExplicitlyReadOnlyAndHasThePlannedName()
    {
        var methods = typeof(MyFrameTools).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(x => x.Attribute is not null).ToArray();
        var expected = new[] { "get_capabilities", "get_capture_inbox_status", "get_equipment", "get_inventory_coverage", "get_item", "get_loadout", "get_mods", "get_overview", "get_sync_history", "get_sync_status", "list_collection", "list_farm", "list_relics", "list_sales", "list_surplus", "search_inventory" };

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
    public void CursorIsSignedAndBoundToItsQuery()
    {
        var codec = new CursorCodec(JsonOptions());
        var cursor = codec.Encode("search_inventory", "snapshot", "query", 50);

        Assert.Equal(50, codec.Decode(cursor, "search_inventory", "query").Offset);
        Assert.Throws<QueryProblemException>(() => codec.Decode(cursor, "search_inventory", "another"));
        Assert.Throws<QueryProblemException>(() => codec.Decode(cursor[..^1] + "x", "search_inventory", "query"));
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
        var configuration = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.Name;
        var server = Environment.GetEnvironmentVariable("MYFRAME_MCP_TEST_SERVER") ??
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "MyFrame.Mcp", "bin", configuration, "net10.0", "win-x64", "MyFrame.Mcp.exe"));
        Assert.True(File.Exists(server), $"Server was not built at {server}");
        using var data = new TemporaryFolder();
        await using (var database = new SyncDatabase(Path.Combine(data.Path, "data.db")))
        {
            var payload = "{\"equipment\":[{\"instanceId\":\"instance-f33\",\"typeId\":\"/Lotus/Weapon\",\"rank\":30,\"config\":{\"mods\":[\"/Lotus/Mod\"]}}],\"RawUpgrades\":[{\"instanceId\":\"instance-f33\",\"uniqueName\":\"/Lotus/Mod\",\"rank\":5}]}";
            var envelope = new InventoryEnvelope(1, 8954, "overwolf-native", Guid.NewGuid(), Guid.NewGuid(),
                1, DateTimeOffset.UtcNow, "test", "verified", payload, "f33-hash");
            var projection = InventoryPayloadParser.Parse(payload);
            await database.PublishInventoryAsync(envelope, projection);
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
        var result = await client.CallToolAsync("get_overview",
            new Dictionary<string, object?> { ["includeAccount"] = false });
        var coverageResult = await client.CallToolAsync("get_inventory_coverage");
        var equipmentResult = await client.CallToolAsync("get_equipment");
        var modsResult = await client.CallToolAsync("get_mods",
            new Dictionary<string, object?> { ["ownerInstanceId"] = "instance-f33" });
        var loadoutResult = await client.CallToolAsync("get_loadout",
            new Dictionary<string, object?> { ["typeId"] = "/Lotus/Weapon" });
        var invalid = await client.CallToolAsync("get_overview",
            new Dictionary<string, object?> { ["unexpected"] = true });
        var expired = await client.CallToolAsync("search_inventory",
            new Dictionary<string, object?> { ["snapshotId"] = "not-retained-synthetic-snapshot" });
        var unavailable = await client.CallToolAsync("search_inventory",
            new Dictionary<string, object?>());

        Assert.Equal(16, tools.Count);
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
        Assert.NotEqual(true, result.IsError);
        Assert.True(invalid.IsError);
        Assert.Contains(invalid.Content.OfType<TextContentBlock>(),
            content => content.Text.Contains("INVALID_ARGUMENT", StringComparison.Ordinal));
        AssertTextOnlyError(expired, "SNAPSHOT_EXPIRED", "Retryable=false");
        AssertTextOnlyError(unavailable, "SETUP_REQUIRED", "Retryable=false");
        Assert.Contains("Check isError", client.ServerInstructions);
        Assert.Contains(result.Content.OfType<TextContentBlock>(), x => x.Text.Contains("snapshotId", StringComparison.Ordinal));
        Assert.DoesNotContain(stderr, line => line.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.All(Directory.EnumerateFileSystemEntries(data.Path), path =>
            Assert.Contains(Path.GetFileName(path), new[] { "data.db", "data.db-shm", "data.db-wal" }));
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

    private static void AssertTextOnlyError(CallToolResult result, string code, string retryable)
    {
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Contains(result.Content.OfType<TextContentBlock>(), content =>
            content.Text.Contains(code, StringComparison.Ordinal) &&
            content.Text.Contains(retryable, StringComparison.Ordinal));
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
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
