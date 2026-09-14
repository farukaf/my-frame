using MyFrame.Core.Sync;
using System.Net;
using System.Net.Http;

namespace MyFrame.Core.Tests;

public sealed class PublicExportTests
{
    [Fact]
    public void PublicExportUsesSeparateOfficialIndexAndDocumentHosts()
    {
        Assert.Equal("https://origin.warframe.com/PublicExport/index_en.txt.lzma", PublicExportIndexClient.DefaultIndexUrl);
        Assert.Equal("https://content.warframe.com/PublicExport/Manifest/", PublicExportDocumentClient.DefaultBaseUrl);
    }

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
    public void PreservesLocalizedAliasesAndUsesEnglishAsPrimaryName()
    {
        var records = PublicExportDocumentParser.Parse("[{\"uniqueName\":\"/Lotus/Test\",\"name\":{\"en\":\"Blade\",\"pt\":\"Lâmina\",\"fr\":\"Lame\"}}]");
        var record = Assert.Single(records);
        Assert.Equal("Blade", record.Name);
        Assert.Equal("Lâmina", record.Aliases["pt"]);
        Assert.Equal("Lame", record.Aliases["fr"]);
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
    public async Task DocumentClientPreservesRevisionTagInOfficialPath()
    {
        using var client = new HttpClient(new RecordingPathHandler());
        await new PublicExportDocumentClient(client).FetchBatchAsync(
            new PublicExportIndexEntry("ExportWeapons_en.json", "00_fixture"),
            baseUri: new Uri("https://fixture.invalid/PublicExport/"));

        Assert.Equal("/PublicExport/ExportWeapons_en.json!00_fixture", RecordingPathHandler.LastPath);
    }

    [Fact]
    public async Task PublicExportRetriesTransientHttpFailures()
    {
        using var handler = new RetryHandler();
        using var client = new HttpClient(handler);
        var entries = await new PublicExportIndexClient(client, _ => "ExportWeapons_en.json!00_fixture")
            .FetchIndexAsync(new Uri("https://fixture.invalid/index_en.txt.lzma"));

        Assert.Single(entries);
        Assert.Equal(3, handler.Attempts);
    }

    [Fact]
    public async Task PublicExportDoesNotRetryNotFound()
    {
        using var handler = new NotFoundHandler();
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new PublicExportIndexClient(client, _ => "ExportWeapons_en.json!00_fixture")
                .FetchIndexAsync(new Uri("https://fixture.invalid/index_en.txt.lzma")));

        Assert.Equal("PUBLIC_EXPORT_HTTP_404", error.Message);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task PublicExportRetriesTransportTimeouts()
    {
        using var handler = new TimeoutHandler();
        using var client = new HttpClient(handler);
        var entries = await new PublicExportIndexClient(client, _ => "ExportWeapons_en.json!00_fixture")
            .FetchIndexAsync(new Uri("https://fixture.invalid/index_en.txt.lzma"));

        Assert.Single(entries);
        Assert.Equal(3, handler.Attempts);
    }

    [Fact]
    public async Task HostPublishesFetchedPublicExportRecordsAtomically()
    {
        const string json = "[{\"uniqueName\":\"/Lotus/Test\",\"name\":{\"en\":\"Test\",\"pt\":\"Teste\"},\"category\":\"Melee\"}]";
        using var client = new HttpClient(new FixtureHandler(System.Text.Encoding.UTF8.GetBytes(json)));
        var root = Path.Combine(Path.GetTempPath(), $"myframe-public-export-{Guid.NewGuid():N}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);
        var result = await host.RunPublicExportOnceAsync("public-export", new PublicExportDocumentClient(client),
            new PublicExportIndexEntry("ExportWeapons_en.json", "fixture"), new Uri("https://fixture.invalid/PublicExport/"));

        Assert.NotNull(result);
        var records = await database.GetPublicExportItemsAsync("public-export");
        var record = Assert.Single(records);
        Assert.Equal("Test", record.Name);
        Assert.Equal("Test", record.Aliases["en"]);
        Assert.Equal("Teste", record.Aliases["pt"]);
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

    [Fact]
    public async Task SharedRunnerPublishesOfficiallyShapedFixtureAndReportsSource()
    {
        const string compressedIndex = "XQAAgAD//////////wAingoHEY9IBeKEpQlqWyzFq6gupkTDS/nb37Fgs4PDdHpkQjeGTuafd2S/MQTD2NGn53OMefAD//8SYAAA";
        using var client = new HttpClient(new OfficialShapeFixtureHandler(
            Convert.FromBase64String(compressedIndex),
            "[{\"uniqueName\":\"/Lotus/Test\",\"name\":{\"en\":\"Test\",\"pt\":\"Teste\"}}]"));
        var root = Path.Combine(Path.GetTempPath(), $"myframe-public-runner-{Guid.NewGuid():N}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);

        var result = await new PublicExportSyncRunner().RunAsync(database, host, client);

        Assert.Equal("published", result.State);
        Assert.Equal(1, result.Records);
        Assert.Equal("ExportWarframes_en.json", result.RelativePath);
        Assert.Equal("00_gFCc6M4iI-LF11CBMzq4FQ", result.RevisionTag);
        Assert.Equal("Teste", Assert.Single(await database.GetPublicExportItemsAsync("public-export")).Aliases["pt"]);
        var coverage = await database.GetSourceCoverageAsync("public-export");
        Assert.Equal(InventoryFieldState.Known, coverage["uniqueName"]);
        Assert.Equal(InventoryFieldState.Known, coverage["aliases"]);
        Assert.Equal(InventoryFieldState.NotObserved, coverage["category"]);
    }

    [Fact]
    public async Task SharedRunnerClassifiesTransportFailureWithoutLeakingNetworkDetails()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-public-network-{Guid.NewGuid():N}");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);
        using var client = new HttpClient(new ThrowingHandler());

        var result = await new PublicExportSyncRunner().RunAsync(database, host, client);

        Assert.Equal("failed", result.State);
        Assert.Equal("PUBLIC_EXPORT_NETWORK_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task SharedRunnerPublishesLocalPublicExportFileWithItsParserVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-public-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "ExportWeapons_en.json");
        await File.WriteAllTextAsync(source, "[{\"uniqueName\":\"/Lotus/Test\",\"name\":{\"en\":\"Blade\",\"pt\":\"Lâmina\"},\"category\":\"Weapon\"}]");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);

        var result = await new PublicExportSyncRunner().RunFileAsync(database, host, source);

        Assert.Equal("published", result.State);
        Assert.Equal(1, result.Records);
        Assert.Equal("ExportWeapons_en.json", result.RelativePath);
        Assert.Equal("public-export-file-1", result.ParserVersion);
        Assert.Equal("Lâmina", Assert.Single(await database.GetPublicExportItemsAsync("public-export")).Aliases["pt"]);
    }

    [Fact]
    public async Task SharedRunnerAggregatesLocalPublicExportDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"myframe-public-directory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "ExportWeapons_en.json"), "[{\"uniqueName\":\"/Lotus/Weapon\",\"name\":\"Blade\"}]");
        await File.WriteAllTextAsync(Path.Combine(root, "ExportWarframes_en.json"), "[{\"uniqueName\":\"/Lotus/Frame\",\"name\":\"Frame\"}]");
        await using var database = new SyncDatabase(Path.Combine(root, "data.db"));
        await using var host = new SyncHost(database);

        var result = await new PublicExportSyncRunner().RunDirectoryAsync(database, host, root);

        Assert.Equal("published", result.State);
        Assert.Equal(2, result.Records);
        Assert.Equal("public-export-directory-1", result.ParserVersion);
        Assert.Equal(2, (await database.GetPublicExportItemsAsync("public-export")).Count);
    }

    private sealed class FixtureHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }

    private sealed class RecordingPathHandler : HttpMessageHandler
    {
        public static string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("[{\"uniqueName\":\"/Lotus/Test\",\"name\":\"Test\"}]", System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class RetryHandler : HttpMessageHandler
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts < 3) throw new TaskCanceledException("fixture timeout");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        }
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

    private sealed class OfficialShapeFixtureHandler(byte[] compressedIndex, string document) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("index_en.txt.lzma", StringComparison.Ordinal) == true)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(compressedIndex) });
            var index = "ExportWeapons_en.json!00_fixture";
            if (request.RequestUri?.AbsolutePath.Contains(".json", StringComparison.Ordinal) == true)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(document) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index) });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("proxy details must not escape");
    }
}
