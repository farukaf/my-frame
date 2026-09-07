namespace MyFrame.Core;

public interface IMarketStateStore
{
    Task<MarketState?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MarketState state, CancellationToken cancellationToken = default);
}
