using MyFrame.Core.Sync;

namespace MyFrame.Core.Tests;

public sealed class ReferenceDocumentTests
{
    [Fact]
    public void ParsesAttributedUntrustedDocumentAndSearchesIt()
    {
        var document = ReferenceDocumentParser.Parse("{\"kind\":\"overframe\",\"url\":\"https://overframe.gg/build/123\",\"title\":\"Example build\",\"revision\":\"rev-1\",\"license\":\"community\",\"author\":\"author\",\"sections\":[{\"id\":\"mods\",\"title\":\"Mods\",\"content\":\"Use Serration and fire rate.\"}]}", DateTimeOffset.UtcNow);
        var hits = ReferenceSearch.Search([document], "fire rate");
        Assert.False(document.IsTrustedForFacts);
        Assert.Single(hits);
        Assert.Equal("rev-1", hits[0].Revision);
    }

    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://wiki.warframe.com/page")]
    public void RejectsUnapprovedReferenceHostOrScheme(string url)
    {
        var json = $"{{\"kind\":\"wiki\",\"url\":\"{url}\",\"title\":\"x\",\"revision\":\"r\",\"sections\":[]}}";
        Assert.Throws<InvalidDataException>(() => ReferenceDocumentParser.Parse(json, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task ImporterStoresValidatedReferenceByHashAndIsIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-reference-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.json");
        await File.WriteAllTextAsync(source, "{\"kind\":\"wiki\",\"url\":\"https://wiki.warframe.com/w/Mother_Token\",\"title\":\"Mother Token\",\"revision\":\"r1\",\"license\":\"wiki\",\"sections\":[{\"id\":\"overview\",\"content\":\"Mother Token reference.\"}]}");
        var destination = Path.Combine(root, "references");

        var first = await ReferenceImporter.ImportAsync(source, destination);
        var second = await ReferenceImporter.ImportAsync(source, destination);

        Assert.False(first.AlreadyImported);
        Assert.True(second.AlreadyImported);
        Assert.Equal(first.StoredFile, second.StoredFile);
        Assert.True(File.Exists(first.StoredFile));
        Assert.False(first.Document.IsTrustedForFacts);
    }

    [Fact]
    public async Task SyncRunnerFetchesOnlyAllowedAttributedReferenceAndIsIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-reference-sync-{Guid.NewGuid():N}");
        var payload = "{\"kind\":\"overframe\",\"url\":\"https://overframe.gg/build/123\",\"title\":\"Example build\",\"revision\":\"rev-2\",\"license\":\"community\",\"sections\":[{\"id\":\"mods\",\"content\":\"Use Serration.\"}]}";
        using var client = new HttpClient(new StubHandler(payload));
        var runner = new ReferenceSyncRunner();

        var first = await runner.FetchAsync(client, new Uri("https://overframe.gg/build/123"), root);
        var second = await runner.FetchAsync(client, new Uri("https://overframe.gg/build/123"), root);

        Assert.False(first.AlreadyImported);
        Assert.True(second.AlreadyImported);
        Assert.Equal("rev-2", first.Document.Revision);
        await Assert.ThrowsAsync<InvalidDataException>(() => runner.FetchAsync(client, new Uri("https://example.com/x"), root));
    }

    [Fact]
    public async Task SyncRunnerRejectsRedirectToUnapprovedHost()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-reference-redirect-{Guid.NewGuid():N}");
        using var client = new HttpClient(new RedirectHandler());
        var runner = new ReferenceSyncRunner();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            runner.FetchAsync(client, new Uri("https://wiki.warframe.com/w/Mother_Token"), root));

        Assert.Equal("REFERENCE_REDIRECT_UNSUPPORTED", error.Message);
    }

    [Fact]
    public async Task SyncRunnerRejectsExplicitlyNonJsonResponse()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-reference-content-type-{Guid.NewGuid():N}");
        using var client = new HttpClient(new NonJsonHandler());
        var runner = new ReferenceSyncRunner();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            runner.FetchAsync(client, new Uri("https://wiki.warframe.com/w/Mother_Token"), root));

        Assert.Equal("REFERENCE_CONTENT_TYPE_UNSUPPORTED", error.Message);
    }

    private sealed class StubHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/redirected"),
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });
    }

    private sealed class NonJsonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("not json", System.Text.Encoding.UTF8, "text/html")
            });
    }
}
