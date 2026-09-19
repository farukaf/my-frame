namespace MyFrame.Core;

public interface IPriceCache
{
    Task<MarketQuote?> GetAsync(string slug, CancellationToken cancellationToken = default);
    Task SetAsync(MarketQuote quote, CancellationToken cancellationToken = default);
}
