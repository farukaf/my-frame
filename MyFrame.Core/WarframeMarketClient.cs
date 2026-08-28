using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MyFrame.Core;

public sealed class WarframeMarketClient : IWarframeMarketClient
{
    private readonly HttpClient _http;
    private readonly string _tokenPath;
    private readonly SemaphoreSlim _rateGate = new(1, 1);
    private readonly ILogger<WarframeMarketClient> _logger;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public WarframeMarketClient(HttpClient httpClient, string tokenPath,
        ILogger<WarframeMarketClient>? logger = null)
    {
        _http = httpClient;
        _tokenPath = tokenPath;
        _logger = logger ?? NullLogger<WarframeMarketClient>.Instance;
        _http.BaseAddress ??= new Uri("https://api.warframe.market/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("my-frame/1.0 (+local desktop application)");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<MarketAccount?> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        using var json = await GetAsync("v2/me", true, cancellationToken);
        if (json is null || !json.RootElement.TryGetProperty("data", out var data)) return null;
        return new MarketAccount(Text(data, "id") ?? "", Text(data, "ingameName") ?? "", Text(data, "platform") ?? "pc");
    }

    public async Task<IReadOnlyList<MarketOrder>> GetMyOrdersAsync(CancellationToken cancellationToken = default)
    {
        using var json = await GetAsync("v2/orders/my", true, cancellationToken);
        if (json is null || !json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        return data.EnumerateArray().Select(ParseOrder).Where(x => x is not null).Cast<MarketOrder>().ToArray();
    }

    public async Task<MarketQuote?> GetTopOrdersAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        using var json = await GetAsync($"v2/orders/item/{Uri.EscapeDataString(slug)}/top", false, cancellationToken);
        if (json is null || !json.RootElement.TryGetProperty("data", out var data)) return null;
        var sells = Prices(data, "sell");
        var buys = Prices(data, "buy");
        return new MarketQuote(slug, sells.Count == 0 ? null : sells.Min(), buys.Count == 0 ? null : buys.Max(), DateTimeOffset.UtcNow);
    }

    private async Task<JsonDocument?> GetAsync(string uri, bool authenticated, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Market GET {MarketEndpoint}; authenticated={Authenticated}", uri, authenticated);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await ThrottleAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (authenticated)
            {
                var token = await ReadValidTokenAsync(cancellationToken);
                if (token is null)
                {
                    _logger.LogWarning("Authenticated market request skipped because the token is absent or expired");
                    return null;
                }
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Market rate limit reached on attempt {Attempt}", attempt + 1);
                await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt + 1), cancellationToken);
                continue;
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Market GET {MarketEndpoint} returned status {StatusCode}", uri, (int)response.StatusCode);
                return null;
            }
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Market GET {MarketEndpoint} completed with status {StatusCode}", uri, (int)response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        return null;
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        await _rateGate.WaitAsync(cancellationToken);
        try
        {
            var wait = TimeSpan.FromMilliseconds(350) - (DateTimeOffset.UtcNow - _lastRequest);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
            _lastRequest = DateTimeOffset.UtcNow;
        }
        finally { _rateGate.Release(); }
    }

    private async Task<string?> ReadValidTokenAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_tokenPath)) return null;
        var token = (await File.ReadAllTextAsync(_tokenPath, cancellationToken)).Trim();
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return json.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds) &&
                   DateTimeOffset.FromUnixTimeSeconds(seconds) <= DateTimeOffset.UtcNow ? null : token;
        }
        catch (Exception e) when (e is FormatException or JsonException) { return null; }
    }

    private static MarketOrder? ParseOrder(JsonElement e)
    {
        var id = Text(e, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;
        return new MarketOrder(id, Text(e, "itemId") ?? "", Text(e, "itemSlug"), Text(e, "type") ?? "",
            Number(e, "platinum"), Math.Max(0, Number(e, "quantity")),
            !e.TryGetProperty("visible", out var visible) || visible.ValueKind == JsonValueKind.True);
    }

    private static List<int> Prices(JsonElement data, string name) =>
        data.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(x => Number(x, "platinum")).Where(x => x > 0).ToList() : [];
    private static string? Text(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int Number(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var x) ? x : 0;
}
