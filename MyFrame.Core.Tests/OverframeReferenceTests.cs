using System.Net;
using System.Text.Json;
using MyFrame.Core.Sync;

namespace MyFrame.Core.Tests;

public sealed class OverframeReferenceTests
{
    [Fact]
    public async Task SynchronizerUsesRobotsSitemapDeterministicParserAndSqliteTtl()
    {
        using var directory = new TemporaryDirectory();
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/robots.txt" => Text("User-agent: *\nAllow: /\nDisallow: /api/\nDisallow: /search/\nSitemap: https://overframe.gg/sitemap.xml"),
            "/sitemap.xml" => Text("<?xml version=\"1.0\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>https://overframe.gg/items/arsenal/8015/haalvu/</loc></url><url><loc>https://overframe.gg/search/</loc></url></urlset>", "application/xml"),
            "/items/arsenal/8015/haalvu/" => Text("<html><head><meta name=\"description\" content=\"Sentient artillery weapon.\"></head><body><h1>Haalvu</h1><a href=\"/build/123/haalvu/example/\">Example build</a></body></html>", "text/html"),
            _ => new(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler);
        var store = new OverframeCacheStore(Path.Combine(directory.Path, "data.db"));
        var synchronizer = new OverframeReferenceSynchronizer(client, store);

        var first = await synchronizer.SyncAsync(OverframeEntityType.Item, "Haalvu",
            new(2, TimeSpan.Zero, TimeSpan.FromHours(8)));
        var requestsAfterFirst = handler.Requests;
        var second = await synchronizer.SyncAsync(OverframeEntityType.Item, "Haalvu",
            new(2, TimeSpan.Zero, TimeSpan.FromHours(8)));
        var cached = await store.GetAsync(OverframeEntityType.Item, "Haalvu", readOnly: true);

        Assert.Equal("refreshed", first.State);
        Assert.Equal("cached", second.State);
        Assert.Equal(requestsAfterFirst, handler.Requests);
        Assert.Equal(3, handler.Requests);
        Assert.NotNull(cached);
        Assert.Equal("item:haalvu", cached.CacheKey);
        Assert.Equal(TimeSpan.FromHours(8), cached.ExpiresAt - cached.FetchedAt);
        using var payload = JsonDocument.Parse(cached.PayloadJson);
        Assert.Equal("Haalvu", payload.RootElement.GetProperty("name").GetString());
        Assert.Equal("Example build", payload.RootElement.GetProperty("popularBuilds")[0].GetProperty("name").GetString());
        Assert.False(payload.RootElement.GetProperty("trustedForFacts").GetBoolean());
    }

    [Fact]
    public void TypeKeyOptionsAndRobotsRulesAreBounded()
    {
        Assert.Equal("mod:serration", OverframeCacheKey.Create(OverframeEntityType.Mod, " Serration "));
        Assert.Equal(OverframeEntityType.Warframe, OverframeCacheKey.ParseType("warframe"));
        Assert.True(OverframeReferenceSynchronizer.RobotsAllows("User-agent: *\nDisallow: /api/", "/items/mods/1/serration/"));
        Assert.False(OverframeReferenceSynchronizer.RobotsAllows("User-agent: *\nDisallow: /api/", "/api/items"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverframeSyncOptions(0).Validate());
        Assert.Equal(TimeSpan.FromMilliseconds(800), new OverframeSyncOptions().EffectiveRequestDelay);
        Assert.Equal(TimeSpan.FromHours(8), new OverframeSyncOptions().EffectiveTimeToLive);
    }

    private static HttpResponseMessage Text(string value, string mediaType = "text/plain") =>
        new(HttpStatusCode.OK) { Content = new StringContent(value, System.Text.Encoding.UTF8, mediaType) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            var result = response(request);
            result.RequestMessage = request;
            return Task.FromResult(result);
        }
    }
}
