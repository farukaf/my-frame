namespace MyFrame.Core;

public interface IMarketItemIndexStore
{
    Task<MarketItemIndex?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MarketItemIndex index, CancellationToken cancellationToken = default);
}
