using MyFrame.Core.Sync;
using System.Net;
using System.Net.Http;

namespace MyFrame.Core.Tests;

public sealed class PublicExportTests
{
    [Fact]
    public void ParsesHashFirstAndPathFirstIndexLines()
    {
        var entries = PublicExportIndexParser.Parse("# comment\nABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789 ExportWarframes_en.json.lzma\nExportWeapons_en.json.lzma 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        Assert.Equal(2, entries.Count);
        Assert.Equal("ExportWarframes_en.json.lzma", entries[0].RelativePath);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", entries[1].RevisionTag);
    }

    [Theory]
    [InlineData("../escape.lzma")]
    [InlineData("C:/escape.lzma")]
    public void RejectsUnsafeIndexPath(string path) => Assert.Throws<InvalidDataException>(() => PublicExportIndexParser.Parse(path));

    [Fact]
    public void ParsesOfficialOpaqueRevisionTagFormat()
    {
        var entries = PublicExportIndexParser.Parse("ExportWarframes_en.json!00_gFCc6M4iI-LF11CBMzq4FQ");
        Assert.Equal("ExportWarframes_en.json", entries[0].RelativePath);
        Assert.Equal("00_gFCc6M4iI-LF11CBMzq4FQ", entries[0].RevisionTag);
    }

    [Fact]
    public void ParsesDocumentsWithoutInventingMissingFields()
    {
        var records = PublicExportDocumentParser.Parse("[{\"uniqueName\":\"/Lotus/Weapons/Test\",\"name\":\"Lâmina\",\"category\":\"Melee\"},{\"name\":\"ignored\"}]");
        Assert.Single(records);
        Assert.Null(records[0].Description);
        Assert.Equal("lamina", PublicExportIdentity.Canonicalize("  LÂMINA "));
        Assert.True(PublicExportIdentity.Equivalent("Lâmina", "lamina"));
    }

    [Fact]
    public void DecodesLzmaAlonePayloadWithOutputLimit()
    {
        var compressed = Convert.FromBase64String("XQAAgAD//////////wA0GUnujmgh////ueAAAA==");
        Assert.Equal("hello", new LzmaAloneDecoder().Decode(compressed));
        Assert.Throws<InvalidDataException>(() => new LzmaAloneDecoder(4).Decode(compressed));
    }

    [Fact]
    public async Task IndexClientUsesInjectedDecoderAndEnforcesHttpContract()
    {
        var compressed = Convert.FromBase64String("XQAAgAD//////////wAingoHEY9IBeKEpQlqWyzFq6gupkTDS/nb37Fgs4PDdHpkQjeGTuafd2S/MQTD2NGn53OMefAD//8SYAAA");
        using var client = new HttpClient(new FixtureHandler(compressed));
        var decoder = new PublicExportIndexClient(client, bytes => new LzmaAloneDecoder().Decode(bytes));
        var entries = await decoder.FetchIndexAsync();
        Assert.Single(entries);
        Assert.Equal("ExportWarframes_en.json", entries[0].RelativePath);
    }

    [Fact]
    public async Task DocumentClientCreatesSyncBatchFromFixture()
    {
        const string json = "[{\"uniqueName\":\"/Lotus/Test\",\"name\":\"Test\"}]";
        using var client = new HttpClient(new FixtureHandler(System.Text.Encoding.UTF8.GetBytes(json)));
        var entry = new PublicExportIndexEntry("ExportWarframes_en.json", "00_fixture");
        var batch = await new PublicExportDocumentClient(client).FetchBatchAsync(entry, baseUri: new Uri("https://fixture.invalid/PublicExport/"));
        Assert.Equal(1, batch.RecordCount);
        Assert.Equal("public-export-1", batch.ParserVersion);
        Assert.Contains("uniqueName", batch.PayloadJson);
    }

    [Fact]
    public async Task HostPublishesFetchedPublicExportRecordsAtomically()
    {
        const string json = "[{\"uniqueName\":\"/Lotus/Test\",\"name\":\"Test\",\"category\":\"Melee\"}]";
        using var client = new HttpClient(new FixtureHandler(System.Text.Encoding.UTF8.GetBytes(json)));
        var root = Path.Combine(Path.GetTempPath(), $"myframe-public-export-{Guid.NewGuid():N}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);
        var result = await host.RunPublicExportOnceAsync("public-export", new PublicExportDocumentClient(client),
            new PublicExportIndexEntry("ExportWeapons_en.json", "fixture"), new Uri("https://fixture.invalid/PublicExport/"));

        Assert.NotNull(result);
        var records = await database.GetPublicExportItemsAsync("public-export");
        Assert.Equal("Test", Assert.Single(records).Name);
    }

    [Fact]
    public async Task HostResolvesLatestIndexEntryBeforePublishing()
    {
        const string document = "[{\"uniqueName\":\"/Lotus/Latest\",\"name\":\"Latest\"}]";
        using var client = new HttpClient(new RoutedFixtureHandler(
            "ExportWeapons_en.json!fixture", document));
        var index = new PublicExportIndexClient(client, bytes => System.Text.Encoding.UTF8.GetString(bytes));
        var root = Path.Combine(Path.GetTempPath(), $"myframe-public-latest-{Guid.NewGuid():N}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);

        var result = await host.RunPublicExportLatestOnceAsync("public-export", index,
            new PublicExportDocumentClient(client), "ExportWeapons_en.json",
            new Uri("https://fixture.invalid/index"), new Uri("https://fixture.invalid/PublicExport/"));

        Assert.NotNull(result);
        Assert.Equal("Latest", Assert.Single(await database.GetPublicExportItemsAsync("public-export")).Name);
    }

    private sealed class FixtureHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }

    private sealed class RoutedFixtureHandler(string index, string document) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = request.RequestUri?.AbsolutePath.EndsWith("/index", StringComparison.Ordinal) == true
                ? index : document;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(payload, System.Text.Encoding.UTF8, "text/plain") });
        }
    }
}
