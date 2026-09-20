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

    private sealed class FixtureHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }
}
