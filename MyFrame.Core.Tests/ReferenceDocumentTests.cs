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
}
