namespace MyFrame.Core;

public interface IRecommendationEngine
{
    RecommendationResult Evaluate(
        InventorySnapshot inventory,
        CatalogSnapshot catalog,
        IReadOnlyDictionary<string, MarketQuote> quotes,
        IReadOnlyList<MarketOrder> myOrders,
        RecommendationSettings settings);
}
