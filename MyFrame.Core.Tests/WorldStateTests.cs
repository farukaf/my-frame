using System.Text.Json;
using System.Net;
using System.Net.Http;
using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.Core.Tests;

public sealed class WorldStateTests
{
    [Fact]
    public void ParsesBountiesCyclesAndDoesNotInventMotherTokens()
    {
        var json = """
            {"timestamp":"2026-09-13T12:00:00Z","buildLabel":"fixture","syndicateMissions":[{"id":"deimos-1","syndicate":"Entrati","activation":"2026-09-13T11:00:00Z","expiry":"2026-09-13T13:00:00Z","jobs":[{"id":"bounty-1","type":"Sample bounty","minMR":3,"standingStages":[100,200],"rewardPoolDrops":[{"item":"Endo","chance":50,"count":100,"rarity":"Common"}]}]}],"cambionCycle":{"state":"vome","activation":"2026-09-13T12:00:00Z","expiry":"2026-09-13T13:00:00Z"}}
            """;
        var snapshot = WorldStateParser.Parse(json, DateTimeOffset.Parse("2026-09-13T12:01:00Z"));
        var current = WorldStateParser.CurrentBounties(snapshot, DateTimeOffset.Parse("2026-09-13T12:30:00Z"));
        Assert.Single(current);
        Assert.Equal("Entrati", current[0].Syndicate);
        Assert.Equal(50, current[0].Jobs[0].Rewards[0].Chance);
        Assert.Equal(InventoryFieldState.NotObserved, snapshot.Coverage["motherTokens"]);
        Assert.Equal("vome", snapshot.Cycles[0].State);
    }

    [Fact]
    public void ExpiredBountyIsNotCurrent()
    {
        var json = "{\"timestamp\":\"2026-09-13T12:00:00Z\",\"syndicateMissions\":[{\"id\":\"old\",\"expiry\":\"2026-09-13T12:00:00Z\",\"jobs\":[]}] }";
        var snapshot = WorldStateParser.Parse(json, DateTimeOffset.Parse("2026-09-13T12:01:00Z"));
        Assert.Empty(WorldStateParser.CurrentBounties(snapshot, DateTimeOffset.Parse("2026-09-13T12:01:00Z")));
    }

    [Fact]
    public void MarksMotherTokenCoverageOnlyWhenRewardIsExplicit()
    {
        var json = """
            {"syndicateMissions":[{"id":"deimos-token","syndicate":"Entrati","jobs":[{"id":"job-token","rewardPoolDrops":[{"item":"Mother Token","count":1}]}]}]}
            """;

        var snapshot = WorldStateParser.Parse(json, DateTimeOffset.UtcNow);

        Assert.Equal(InventoryFieldState.Known, snapshot.Coverage["motherTokens"]);
    }

    [Fact]
    public void ParsesOfficialWorldStateAliasesWithoutInventingRewardDrops()
    {
        var json = """
            {"Timestamp":{"$date":{"$numberLong":"1789291200000"}},"SyndicateMissions":[{"Tag":"DeimosSyndicate","Activation":{"$date":{"$numberLong":"1789290900000"}},"Expiry":{"$date":{"$numberLong":"1789294500000"}},"Jobs":[{"jobType":"DeimosMission","rewards":"/Lotus/Types/Gameplay/Deimos/Jobs/DeimosMissionRewards","minEnemyLevel":10,"maxEnemyLevel":20,"xpAmounts":[100,200]}]}],"CetusCycle":{"State":"day","Activation":{"$date":{"$numberLong":"1789290000000"}},"Expiry":{"$date":{"$numberLong":"1789293600000"}}}}
            """;

        var snapshot = WorldStateParser.Parse(json, DateTimeOffset.FromUnixTimeMilliseconds(1789291201000));

        var bounty = Assert.Single(snapshot.Bounties);
        Assert.Equal("Entrati", bounty.Syndicate);
        Assert.Equal("DeimosMission", bounty.Jobs[0].Type);
        Assert.Equal(InventoryFieldState.Known, snapshot.Coverage["syndicateMissions"]);
        Assert.Equal(InventoryFieldState.NotObserved, snapshot.Coverage["bountyRewards"]);
        Assert.Equal(InventoryFieldState.NotObserved, snapshot.Coverage["motherTokens"]);
        Assert.Equal("day", Assert.Single(snapshot.Cycles).State);
    }

    [Fact]
    public void UsesOfficialWorldStateEndpointByDefault()
    {
        Assert.Equal("https://content.warframe.com/dynamic/worldState.php", WorldStateClient.DefaultUrl);
        Assert.Equal("https://api.warframestat.us/pc", WorldStateClient.CommunityFallbackUrl);
    }

    [Fact]
    public async Task ClientProducesRevisionedSyncBatch()
    {
        const string json = "{\"timestamp\":\"2026-09-13T12:00:00Z\",\"syndicateMissions\":[]}";
        using var client = new HttpClient(new FixtureHandler(System.Text.Encoding.UTF8.GetBytes(json)));
        var result = await new WorldStateClient(client).FetchAsync(new Uri("https://fixture.invalid/world"));
        Assert.Equal("worldstate-pc", result.Batch.SourceId);
        Assert.Equal("worldstate-1", result.Batch.ParserVersion);
        Assert.Equal(0, result.Batch.RecordCount);
    }

    [Fact]
    public async Task ClientLabelsParserByWorldStateEndpoint()
    {
        const string json = "{\"Timestamp\":{\"$date\":{\"$numberLong\":\"1789291200000\"}},\"SyndicateMissions\":[]}";
        using var client = new HttpClient(new FixtureHandler(System.Text.Encoding.UTF8.GetBytes(json)));

        var official = await new WorldStateClient(client).FetchAsync(new Uri(WorldStateClient.DefaultUrl));
        var community = await new WorldStateClient(client).FetchAsync(new Uri(WorldStateClient.CommunityFallbackUrl));

        Assert.Equal("worldstate-official-1", official.Batch.ParserVersion);
        Assert.Equal("worldstate-community-1", community.Batch.ParserVersion);
    }

    [Fact]
    public async Task ClientUsesCommunityFallbackOnlyForOfficialTransportFailure()
    {
        const string json = "{\"syndicateMissions\":[]}";
        using var client = new HttpClient(new FailingOfficialThenCommunityHandler(System.Text.Encoding.UTF8.GetBytes(json)));

        var result = await new WorldStateClient(client, allowCommunityFallback: true).FetchAsync();

        Assert.Equal("worldstate-community-1", result.Batch.ParserVersion);
    }

    [Fact]
    public async Task ClientRetriesTransientOfficialFailures()
    {
        const string json = "{\"syndicateMissions\":[]}";
        using var handler = new TransientHandler(System.Text.Encoding.UTF8.GetBytes(json));
        using var client = new HttpClient(handler);

        var result = await new WorldStateClient(client).FetchAsync(new Uri(WorldStateClient.DefaultUrl));

        Assert.Equal("worldstate-official-1", result.Batch.ParserVersion);
        Assert.Equal(3, handler.Attempts);
    }

    [Fact]
    public async Task ClientDoesNotFallbackWhenOfficialPayloadIsInvalid()
    {
        using var client = new HttpClient(new InvalidOfficialHandler());

        await Assert.ThrowsAnyAsync<JsonException>(() =>
            new WorldStateClient(client, allowCommunityFallback: true).FetchAsync());
    }

    [Fact]
    public async Task HostPublishesFetchedWorldStateRevision()
    {
        const string json = "{\"timestamp\":\"2026-09-13T12:00:00Z\",\"syndicateMissions\":[{\"id\":\"deimos-1\",\"syndicate\":\"Entrati\",\"jobs\":[{\"id\":\"job-1\",\"type\":\"Sample bounty\",\"rewardPoolDrops\":[{\"item\":\"Endo\",\"chance\":50,\"count\":100,\"rarity\":\"Common\"}]}]}] }";
        using var client = new HttpClient(new FixtureHandler(System.Text.Encoding.UTF8.GetBytes(json)));
        var root = Path.Combine(Path.GetTempPath(), $"myframe-worldstate-{Guid.NewGuid():N}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);

        var result = await host.RunWorldStateOnceAsync(new WorldStateClient(client), new Uri("https://fixture.invalid/world"));

        Assert.NotNull(result);
        var bounties = await database.GetCurrentWorldStateBountiesAsync(DateTimeOffset.Parse("2026-09-13T12:30:00Z"));
        Assert.Equal("Entrati", Assert.Single(bounties).Syndicate);
        Assert.Equal("Sample bounty", bounties[0].Jobs[0].Type);
        Assert.Equal("Endo", bounties[0].Jobs[0].Rewards[0].Item);
    }

    [Fact]
    public async Task SharedWorldStateRunnerPublishesAndReportsParser()
    {
        const string json = "{\"timestamp\":\"2026-09-13T12:00:00Z\",\"syndicateMissions\":[]}";
        using var client = new HttpClient(new FixtureHandler(System.Text.Encoding.UTF8.GetBytes(json)));
        var root = Path.Combine(Path.GetTempPath(), $"myframe-worldstate-runner-{Guid.NewGuid():N}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);

        var result = await new WorldStateSyncRunner().RunAsync(database, host, client);

        Assert.Equal("published", result.State);
        Assert.Equal("worldstate-official-1", result.ParserVersion);
        Assert.Equal(0, result.Records);
    }

    [Fact]
    public async Task SharedWorldStateRunnerClassifiesTransportFailureWithoutLeakingDetails()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-worldstate-network-{Guid.NewGuid():N}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);
        using var client = new HttpClient(new ThrowingHandler());

        var result = await new WorldStateSyncRunner().RunAsync(database, host, client);

        Assert.Equal("failed", result.State);
        Assert.Equal("WORLDSTATE_NETWORK_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task SharedWorldStateRunnerPublishesLocalFileWithExplicitParserVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-worldstate-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "worldState.json");
        await File.WriteAllTextAsync(source, "{\"syndicateMissions\":[{\"id\":\"deimos-1\",\"syndicate\":\"Entrati\",\"jobs\":[]}]}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);

        var result = await new WorldStateSyncRunner().RunFileAsync(database, host, source);

        Assert.Equal("published", result.State);
        Assert.Equal("worldstate-file-1", result.ParserVersion);
        Assert.Equal(1, result.Records);
        Assert.Equal("Entrati", Assert.Single(await database.GetCurrentWorldStateBountiesAsync(DateTimeOffset.UtcNow.AddMinutes(1))).Syndicate);
    }

    [Fact]
    public async Task OfficialWorldStateFlowsThroughHostAndPersistsProvenance()
    {
        const string json = """
            {"Timestamp":{"$date":{"$numberLong":"1789291200000"}},"SyndicateMissions":[{"Tag":"DeimosSyndicate","Activation":{"$date":{"$numberLong":"1789290900000"}},"Expiry":{"$date":{"$numberLong":"1789294500000"}},"Jobs":[{"jobType":"DeimosMission","rewards":"/Lotus/Types/Gameplay/Deimos/Jobs/DeimosMissionRewards","minEnemyLevel":10,"maxEnemyLevel":20,"xpAmounts":[100,200]}] }]}
            """;
        using var client = new HttpClient(new FixtureHandler(System.Text.Encoding.UTF8.GetBytes(json)));
        var root = Path.Combine(Path.GetTempPath(), $"myframe-worldstate-official-{Guid.NewGuid():N}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);

        var result = await host.RunWorldStateOnceAsync(new WorldStateClient(client));

        Assert.NotNull(result);
        var status = await database.GetStatusAsync("worldstate-pc");
        Assert.Equal("worldstate-official-1", status!.ParserVersion);
        var bounty = Assert.Single(await database.GetCurrentWorldStateBountiesAsync(DateTimeOffset.FromUnixTimeMilliseconds(1789291201000)));
        Assert.Equal("Entrati", bounty.Syndicate);
        Assert.Empty(bounty.Jobs[0].Rewards);
    }

    [Fact]
    public async Task NewWorldStateRevisionReplacesPreviousCoverage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-worldstate-coverage-{Guid.NewGuid():N}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        var now = DateTimeOffset.UtcNow;
        var first = new WorldStateSnapshot(now, now, "fixture", [], [], "coverage-known", true,
            new Dictionary<string, InventoryFieldState> { ["motherTokens"] = InventoryFieldState.Known });
        var second = first with
        {
            RetrievedAt = now.AddMinutes(1),
            ContentHash = "coverage-not-observed",
            Coverage = new Dictionary<string, InventoryFieldState> { ["motherTokens"] = InventoryFieldState.NotObserved }
        };
        await database.PublishWorldStateAsync(first, new SyncBatch("worldstate-pc", first.ContentHash, "{}", 0, "worldstate-official-1"));
        Assert.Equal("worldstate-official-1", (await database.GetStatusAsync("worldstate-pc"))!.ParserVersion);
        await database.PublishWorldStateAsync(second, new SyncBatch("worldstate-pc", second.ContentHash, "{}", 0));

        var coverage = await database.GetSourceCoverageAsync("worldstate-pc");

        Assert.Equal(InventoryFieldState.NotObserved, coverage["motherTokens"]);
    }

    private sealed class FixtureHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }

    private sealed class FailingOfficialThenCommunityHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri == WorldStateClient.DefaultUrl)
                throw new HttpRequestException("fixture transport failure");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        }
    }

    private sealed class TransientHandler(byte[] payload) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts < 3)
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        }
    }

    private sealed class InvalidOfficialHandler : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("proxy details must not escape");
    }
}
