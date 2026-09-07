namespace MyFrame.Core;

public sealed class RecommendationEngine : IRecommendationEngine
{
    public RecommendationResult Evaluate(InventorySnapshot inventory, CatalogSnapshot catalog,
        IReadOnlyDictionary<string, MarketQuote> quotes, IReadOnlyList<MarketOrder> myOrders,
        RecommendationSettings settings)
    {
        var collection = BuildCollection(inventory, catalog);
        var reservations = BuildReservations(inventory, catalog, myOrders, settings);
        var excess = inventory.Stackables.ToDictionary(x => x.Key,
            x => Math.Max(0, x.Value - reservations.GetValueOrDefault(x.Key)), StringComparer.Ordinal);
        var sales = BuildSales(inventory, catalog, quotes, myOrders, reservations, excess, settings);
        var farm = BuildFarm(inventory, catalog, collection, quotes);
        var relics = BuildRelics(inventory, catalog, quotes);
        return new RecommendationResult(collection, farm, sales, relics,
            sales.Where(x => x.Action == RecommendationAction.ExchangeForDucats).Sum(x => x.TotalDucats),
            sales.Where(x => x.Action == RecommendationAction.SellForPlatinum).Sum(x => x.TotalPlatinum ?? 0),
            DateTimeOffset.UtcNow, settings);
    }

    private static IReadOnlyList<CollectionGoal> BuildCollection(InventorySnapshot inventory, CatalogSnapshot catalog) =>
        catalog.Items.Where(x => x.Masterable && IsRelevantGoal(x))
            .Select(item =>
            {
                var owned = inventory.OwnedEquipment.Contains(item.UniqueName);
                var mastered = IsMastered(item, inventory.Experience.GetValueOrDefault(item.UniqueName));
                var required = item.Components.Sum(x => x.Required);
                var have = item.Components
                    .Sum(x => Math.Min(x.Required, inventory.Stackables.GetValueOrDefault(x.UniqueName)));
                var completion = owned || required == 0 ? (owned ? 1d : 0d) : (double)have / required;
                var status = owned && mastered ? "Collected + mastered" : owned ? "Mastery pending" :
                    mastered ? "Mastered; not owned" : required > 0 && have >= required ? "Ready to build" : "In progress";
                var components = item.Components.Select(component => new CollectionComponentDetail(
                    component.Name, inventory.Stackables.GetValueOrDefault(component.UniqueName),
                    component.Required, component.Tradable, component.ImageUrl)).ToArray();
                return new CollectionGoal(item.Name, item.Category, owned, mastered, have, required, completion, status,
                    item.Prime, item.Vaulted, item.ImageUrl, item.MarketSlug, components);
            }).OrderBy(x => x.Category).ThenBy(x => x.ItemName).ToArray();

    private static Dictionary<string, int> BuildReservations(InventorySnapshot inventory, CatalogSnapshot catalog,
        IReadOnlyList<MarketOrder> myOrders, RecommendationSettings settings)
    {
        var reserved = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in catalog.Items.Where(x => x.Masterable && x.Components.Count > 0))
        {
            var owned = inventory.OwnedEquipment.Contains(item.UniqueName);
            var mastered = IsMastered(item, inventory.Experience.GetValueOrDefault(item.UniqueName));
            var needBuild = !owned && !mastered;
            var speculate = settings.UnvaultedPrimeSetsToReserve > 0 && item.Prime && !item.Vaulted;
            foreach (var component in item.Components.Where(x => x.Tradable))
            {
                var amount = (needBuild ? component.Required : 0) +
                    (speculate ? settings.UnvaultedPrimeSetsToReserve * component.Required : 0);
                reserved[component.UniqueName] = Math.Max(reserved.GetValueOrDefault(component.UniqueName), amount);
            }
        }

        foreach (var order in myOrders.Where(x => x.Type.Equals("sell", StringComparison.OrdinalIgnoreCase)))
        {
            var set = catalog.Items.FirstOrDefault(x => x.MarketId == order.ItemId);
            if (set is not null)
            {
                foreach (var component in set.Components.Where(x => x.Tradable))
                    reserved[component.UniqueName] = reserved.GetValueOrDefault(component.UniqueName) +
                        (order.Quantity * component.Required);
                continue;
            }

            foreach (var item in catalog.Items)
            {
                var component = item.Components.FirstOrDefault(candidate =>
                    MarketForComponent(item, candidate, catalog)?.Id == order.ItemId);
                if (component is null) continue;
                reserved[component.UniqueName] = reserved.GetValueOrDefault(component.UniqueName) + order.Quantity;
                break;
            }
        }
        return reserved;
    }

    private static IReadOnlyList<SaleRecommendation> BuildSales(InventorySnapshot inventory, CatalogSnapshot catalog,
        IReadOnlyDictionary<string, MarketQuote> quotes, IReadOnlyList<MarketOrder> orders,
        IReadOnlyDictionary<string, int> reservations, IDictionary<string, int> excess,
        RecommendationSettings settings)
    {
        var results = new List<SaleRecommendation>();
        var componentInfo = ComponentInfo(catalog);

        // Sets are allocated first only when they are worth at least as much as selling their pieces.
        foreach (var parent in catalog.Items.Where(x => x.Prime && x.Components.Any(c => c.Tradable) && !string.IsNullOrWhiteSpace(x.MarketSlug)))
        {
            var tradable = parent.Components.Where(x => x.Tradable).ToArray();
            var setCount = tradable.Min(x =>
                (excess.TryGetValue(x.UniqueName, out var amount) ? amount : 0) / x.Required);
            if (setCount <= 0 || !quotes.TryGetValue(parent.MarketSlug!, out var setQuote)) continue;
            var setMarketPrice = MarketPrice(setQuote);
            if (setMarketPrice is null) continue;
            var partValue = tradable.Sum(component =>
            {
                var identity = MarketForComponent(parent, component, catalog);
                return identity is not null && quotes.TryGetValue(identity.Slug, out var quote)
                    ? (MarketPrice(quote) ?? 0) * component.Required : 0;
            });
            if (setMarketPrice < partValue) continue;

            foreach (var component in tradable) excess[component.UniqueName] -= setCount * component.Required;
            results.Add(new SaleRecommendation(parent.Name + " Set", parent.UniqueName, parent.MarketSlug,
                setCount, 0, 0, 0, 0, setCount, tradable.Sum(x => x.Ducats * x.Required), setMarketPrice,
                setQuote.HighestBuy, RecommendationAction.SellForPlatinum, parent.Vaulted,
                inventory.OwnedEquipment.Contains(parent.UniqueName),
                IsMastered(parent, inventory.Experience.GetValueOrDefault(parent.UniqueName)),
                HasOrder(parent.MarketId, orders), "The complete set is worth at least as much as its individual parts.",
                parent.ImageUrl));
        }

        foreach (var pair in excess.Where(x => x.Value > 0))
        {
            if (!componentInfo.TryGetValue(pair.Key, out var info) || !info.Component.Tradable) continue;
            var identity = MarketForComponent(info.Parent, info.Component, catalog);
            quotes.TryGetValue(identity?.Slug ?? "", out var quote);
            var marketPrice = MarketPrice(quote);
            var hasPlatinumValue = marketPrice is not null;
            var hasDucatValue = info.Component.Ducats > 0;
            if (!hasPlatinumValue && !hasDucatValue) continue;
            var platinumWins = hasPlatinumValue &&
                marketPrice!.Value * settings.DucatsPerPlatinum >= info.Component.Ducats;
            var action = platinumWins ? RecommendationAction.SellForPlatinum : hasDucatValue
                ? RecommendationAction.ExchangeForDucats : RecommendationAction.Keep;
            var reason = action switch
            {
                RecommendationAction.SellForPlatinum =>
                    $"Market price beats the 1p/{settings.DucatsPerPlatinum} ducat threshold.",
                RecommendationAction.ExchangeForDucats when !hasPlatinumValue =>
                    "No public market price; the item still has ducat value.",
                RecommendationAction.ExchangeForDucats => "Ducats beat the configured threshold.",
                _ => "No public market price or ducat value; keep it instead of exchanging for zero."
            };
            var breakdown = ReservationBreakdown(info.Parent, info.Component, inventory, catalog, orders, settings);
            results.Add(new SaleRecommendation(info.DisplayName, pair.Key, identity?.Slug,
                inventory.Stackables.GetValueOrDefault(pair.Key), reservations.GetValueOrDefault(pair.Key),
                breakdown.Craft, breakdown.FutureSale, breakdown.Orders, pair.Value,
                info.Component.Ducats, marketPrice, quote?.HighestBuy, action, info.Parent.Vaulted,
                inventory.OwnedEquipment.Contains(info.Parent.UniqueName),
                IsMastered(info.Parent, inventory.Experience.GetValueOrDefault(info.Parent.UniqueName)),
                HasOrder(identity?.Id, orders), reason, PartImage(info.Parent, info.Component)));
        }

        // Show fully reserved pieces as an explicit Keep recommendation so the sales screen
        // also explains what must not be sold or exchanged.
        foreach (var pair in inventory.Stackables.Where(x => x.Value > 0 &&
                     reservations.GetValueOrDefault(x.Key) > 0 &&
                     (!excess.TryGetValue(x.Key, out var available) || available == 0)))
        {
            if (!componentInfo.TryGetValue(pair.Key, out var info) || !info.Component.Tradable) continue;
            var identity = MarketForComponent(info.Parent, info.Component, catalog);
            quotes.TryGetValue(identity?.Slug ?? "", out var quote);
            var marketPrice = MarketPrice(quote);
            if (marketPrice is null && info.Component.Ducats <= 0) continue;
            var kept = Math.Min(pair.Value, reservations.GetValueOrDefault(pair.Key));
            var breakdown = ReservationBreakdown(info.Parent, info.Component, inventory, catalog, orders, settings);
            var allocated = AllocateOwnedReservations(kept, breakdown);
            results.Add(new SaleRecommendation(info.DisplayName, pair.Key, identity?.Slug,
                pair.Value, kept, allocated.Craft, allocated.FutureSale, allocated.Orders,
                0, info.Component.Ducats, marketPrice, quote?.HighestBuy,
                RecommendationAction.Keep, info.Parent.Vaulted,
                inventory.OwnedEquipment.Contains(info.Parent.UniqueName),
                IsMastered(info.Parent, inventory.Experience.GetValueOrDefault(info.Parent.UniqueName)),
                HasOrder(identity?.Id, orders),
                "All owned copies are reserved for collection, crafting, or an existing market order.",
                PartImage(info.Parent, info.Component)));
        }

        return results.OrderBy(x => x.Action).ThenByDescending(x => x.TotalPlatinum ?? 0)
            .ThenByDescending(x => x.TotalDucats).ToArray();
    }

    private static ReservationParts ReservationBreakdown(CatalogItem item, CatalogComponent component,
        InventorySnapshot inventory, CatalogSnapshot catalog, IReadOnlyList<MarketOrder> orders,
        RecommendationSettings settings)
    {
        var owned = inventory.OwnedEquipment.Contains(item.UniqueName);
        var mastered = IsMastered(item, inventory.Experience.GetValueOrDefault(item.UniqueName));
        var craft = !owned && !mastered ? component.Required : 0;
        var futureSale = settings.UnvaultedPrimeSetsToReserve > 0 && item.Prime && !item.Vaulted
            ? settings.UnvaultedPrimeSetsToReserve * component.Required : 0;
        var identity = MarketForComponent(item, component, catalog);
        var orderQuantity = orders.Where(x => x.Type.Equals("sell", StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.ItemId == item.MarketId ? order.Quantity * component.Required :
                identity is not null && order.ItemId == identity.Id ? order.Quantity : 0);
        return new ReservationParts(craft, futureSale, orderQuantity);
    }

    private static ReservationParts AllocateOwnedReservations(int owned, ReservationParts requested)
    {
        var craft = Math.Min(owned, requested.Craft);
        var remaining = owned - craft;
        var futureSale = Math.Min(remaining, requested.FutureSale);
        remaining -= futureSale;
        var orders = Math.Min(remaining, requested.Orders);
        return new ReservationParts(craft, futureSale, orders);
    }

    private static IReadOnlyList<FarmRecommendation> BuildFarm(InventorySnapshot inventory, CatalogSnapshot catalog,
        IReadOnlyList<CollectionGoal> collection, IReadOnlyDictionary<string, MarketQuote> quotes)
    {
        var incomplete = collection.Where(x => !x.Owned).Select(x => x.ItemName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relicByName = catalog.Items.Where(x => x.Category.Contains("Relic", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().UniqueName, StringComparer.OrdinalIgnoreCase);
        return catalog.Items.Where(x => incomplete.Contains(x.Name) && x.Components.Any(c => c.Tradable))
            .Select(item =>
            {
                var missing = item.Components.Where(x =>
                    inventory.Stackables.GetValueOrDefault(x.UniqueName) < x.Required).ToArray();
                var ownedRelics = item.Relics.Select(x => x.RelicName).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(name => relicByName.TryGetValue(name, out var unique) && inventory.Stackables.GetValueOrDefault(unique) > 0);
                quotes.TryGetValue(item.MarketSlug ?? "", out var quote);
                return new FarmRecommendation(item.Name, item.Category, missing.Length,
                    item.Components.Count, ownedRelics, item.Vaulted, quote?.LowestSell,
                    missing.Select(x => x.Name).ToArray(), $"Missing {missing.Length} parts; {ownedRelics} useful relics owned.",
                    item.ImageUrl, item.MarketSlug);
            }).OrderBy(x => x.MissingParts).ThenByDescending(x => x.OwnedRelics)
            .ThenByDescending(x => x.EstimatedPlatinum ?? 0).ToArray();
    }

    private static IReadOnlyList<RelicRecommendation> BuildRelics(InventorySnapshot inventory,
        CatalogSnapshot catalog, IReadOnlyDictionary<string, MarketQuote> quotes)
    {
        return catalog.Items
            .Where(item => item.Category.Contains("Relic", StringComparison.OrdinalIgnoreCase) &&
                           inventory.Stackables.GetValueOrDefault(item.UniqueName) > 0)
            .Select(item =>
            {
                quotes.TryGetValue(item.MarketSlug ?? "", out var relicQuote);
                var expected = item.Relics.Sum(reward =>
                {
                    var identity = catalog.MarketByNormalizedName.GetValueOrDefault(
                        ItemNameNormalizer.Normalize(reward.RewardName));
                    return identity is not null && quotes.TryGetValue(identity.Slug, out var rewardQuote)
                        ? (reward.Chance / 100d) * (rewardQuote.LowestSell ?? rewardQuote.HighestBuy ?? 0)
                        : 0;
                });
                var sell = relicQuote?.LowestSell ?? relicQuote?.HighestBuy;
                var hasRewardPrices = expected > 0;
                var action = sell is not null && (!hasRewardPrices || sell.Value >= expected) ? "Sell sealed" :
                    hasRewardPrices ? "Open" : "Hold";
                var reason = action switch
                {
                    "Sell sealed" when hasRewardPrices => $"Sealed price {sell}p is above the {expected:0.0}p expected opening value.",
                    "Sell sealed" => $"Sealed relic is priced at {sell}p; reward prices are unavailable.",
                    "Open" => $"Expected opening value {expected:0.0}p is above the {sell?.ToString() ?? "unknown"}p sealed price.",
                    _ => "Not enough market data to compare selling and opening."
                };
                return new RelicRecommendation(item.Name, item.UniqueName,
                    inventory.Stackables[item.UniqueName], item.Vaulted, sell, expected, action, reason,
                    item.ImageUrl, item.MarketSlug);
            })
            .OrderBy(x => x.Action == "Hold")
            .ThenByDescending(x => Math.Max(x.SellPriceEach ?? 0, x.ExpectedOpenValueEach) * x.Owned)
            .ToArray();
    }

    private static Dictionary<string, ComponentDetails> ComponentInfo(CatalogSnapshot catalog)
    {
        var result = new Dictionary<string, ComponentDetails>(StringComparer.Ordinal);
        foreach (var parent in catalog.Items)
            foreach (var component in parent.Components.Where(x => x.Tradable))
                result.TryAdd(component.UniqueName, new ComponentDetails(parent, component,
                    component.Name.Equals("Blueprint", StringComparison.OrdinalIgnoreCase) ? $"{parent.Name} Blueprint" : $"{parent.Name} {component.Name}"));
        return result;
    }

    private static MarketIdentity? MarketForComponent(CatalogItem parent, CatalogComponent component, CatalogSnapshot catalog)
    {
        var name = component.Name.Equals("Blueprint", StringComparison.OrdinalIgnoreCase) ? $"{parent.Name} Blueprint" : $"{parent.Name} {component.Name}";
        return catalog.MarketByNormalizedName.GetValueOrDefault(ItemNameNormalizer.Normalize(name));
    }

    // A part row shows the part's own icon, which is the icon the game itself uses for it. The
    // main blueprint is the exception: every item in the catalog shares one blueprint.png, so it
    // keeps the parent art and stays tellable apart. Same fallback for a component with no image.
    private static string PartImage(CatalogItem parent, CatalogComponent component) =>
        IsMainBlueprint(component) || component.ImageUrl.Length == 0
            ? parent.ImageUrl : component.ImageUrl;

    private static bool IsMainBlueprint(CatalogComponent component) =>
        component.Name.Equals("Blueprint", StringComparison.OrdinalIgnoreCase);

    private static bool IsRelevantGoal(CatalogItem item) => !item.Category.Contains("Skin", StringComparison.OrdinalIgnoreCase);
    private static bool IsWarframe(CatalogItem item) => item.ProductCategory == "Suits" || item.Category == "Warframes";
    private static bool IsMastered(CatalogItem item, long xp) => item.IsMasteredWith(xp);
    private static bool HasOrder(string? marketId, IReadOnlyList<MarketOrder> orders) => !string.IsNullOrWhiteSpace(marketId) && orders.Any(x => x.ItemId == marketId && x.Type == "sell");
    private static int? MarketPrice(MarketQuote? quote) => quote?.LowestSell is > 0
        ? quote.LowestSell
        : quote?.HighestBuy is > 0 ? quote.HighestBuy : null;
    private sealed record ComponentDetails(CatalogItem Parent, CatalogComponent Component, string DisplayName);
    private sealed record ReservationParts(int Craft, int FutureSale, int Orders);
}
