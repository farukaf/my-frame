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
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", entries[1].Sha256);
    }

    [Theory]
    [InlineData("../escape.lzma")]
    [InlineData("C:/escape.lzma")]
    public void RejectsUnsafeIndexPath(string path) => Assert.Throws<InvalidDataException>(() => PublicExportIndexParser.Parse(path));

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
        var compressed = Convert.FromBase64String("XQAAgAD//////////wAingoHEY9IBeKEpQlqWyzFq6gupkTDTCzn+p/JfCj9I/r//3LWAAA=");
        using var client = new HttpClient(new FixtureHandler(compressed));
        var decoder = new PublicExportIndexClient(client, bytes => new LzmaAloneDecoder().Decode(bytes));
        var entries = await decoder.FetchIndexAsync();
        Assert.Single(entries);
        Assert.Equal("ExportWarframes_en.json.lzma", entries[0].RelativePath);
    }

    private sealed class FixtureHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }
}
