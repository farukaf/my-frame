using System.Net;
using System.Text;
using System.Text.Json;
using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class WarframeMarketClientTests
{
    [Fact]
    public async Task UsesBearerOnlyForPrivateGetEndpoints()
    {
        using var directory = new TemporaryDirectory();
        var tokenPath = System.IO.Path.Combine(directory.Path, "token.tk");
        await File.WriteAllTextAsync(tokenPath, CreateToken(DateTimeOffset.UtcNow.AddHours(1)));
        var handler = new RecordingHandler();
        var client = new WarframeMarketClient(new HttpClient(handler), tokenPath);

        var account = await client.GetAccountAsync();
        var quote = await client.GetTopOrdersAsync("test_prime_set");

        Assert.Equal("Tenno", account?.IngameName);
        Assert.Equal(12, quote?.LowestSell);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.NotNull(handler.Requests[0].Authorization);
        Assert.Null(handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task ExpiredTokenPreventsAuthenticatedRequest()
    {
        using var directory = new TemporaryDirectory();
        var tokenPath = System.IO.Path.Combine(directory.Path, "token.tk");
        await File.WriteAllTextAsync(tokenPath, CreateToken(DateTimeOffset.UtcNow.AddMinutes(-1)));
        var handler = new RecordingHandler();

        var account = await new WarframeMarketClient(new HttpClient(handler), tokenPath).GetAccountAsync();

        Assert.Null(account);
        Assert.Empty(handler.Requests);
    }

    private static string CreateToken(DateTimeOffset expires)
    {
        static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"none\"}")}.{Encode(JsonSerializer.Serialize(new { exp = expires.ToUnixTimeSeconds() }))}.signature";
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RequestRecord> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RequestRecord(request.Method, request.Headers.Authorization?.ToString()));
            var json = request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal)
                ? "{\"data\":{\"id\":\"1\",\"ingameName\":\"Tenno\",\"platform\":\"pc\"}}"
                : "{\"data\":{\"sell\":[{\"platinum\":12}],\"buy\":[{\"platinum\":9}]}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record RequestRecord(HttpMethod Method, string? Authorization);
}
