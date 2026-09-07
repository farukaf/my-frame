namespace MyFrame.Core;

public interface IWarframeMarketClient
{
    Task<MarketAccount?> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketOrder>> GetMyOrdersAsync(CancellationToken cancellationToken = default);
    Task<MarketQuote?> GetTopOrdersAsync(string slug, CancellationToken cancellationToken = default);
}
