namespace MyFrame.Core;

public interface IAlecaFramePath
{
    string DirectoryPath { get; }
    event EventHandler<string>? Changed;
    void SetDirectory(string directoryPath);
}

public interface IAlecaFrameReader
{
    Task<InventorySnapshot> ReadAsync(string alecaDirectory, CancellationToken cancellationToken = default);
}

public interface IAlecaCatalogReader
{
    Task<CatalogSnapshot> LoadAsync(string alecaDirectory, CancellationToken cancellationToken = default);
}

public interface IWarframeMarketClient
{
    Task<MarketAccount?> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketOrder>> GetMyOrdersAsync(CancellationToken cancellationToken = default);
    Task<MarketQuote?> GetTopOrdersAsync(string slug, CancellationToken cancellationToken = default);
}

public interface IRecommendationEngine
{
    RecommendationResult Evaluate(
        InventorySnapshot inventory,
        CatalogSnapshot catalog,
        IReadOnlyDictionary<string, MarketQuote> quotes,
        IReadOnlyList<MarketOrder> myOrders,
        RecommendationSettings settings);
}

public interface IPriceCache
{
    Task<MarketQuote?> GetAsync(string slug, CancellationToken cancellationToken = default);
    Task SetAsync(MarketQuote quote, CancellationToken cancellationToken = default);
}
