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
        return new RecommendationResult(collection, farm, sales,
            sales.Where(x => x.Action == RecommendationAction.ExchangeForDucats).Sum(x => x.TotalDucats),
            sales.Where(x => x.Action == RecommendationAction.SellForPlatinum).Sum(x => x.TotalPlatinum ?? 0),
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<CollectionGoal> BuildCollection(InventorySnapshot inventory, CatalogSnapshot catalog) =>
        catalog.Items.Where(x => x.Masterable && IsRelevantGoal(x))
            .Select(item =>
            {
                var owned = inventory.OwnedEquipment.Contains(item.UniqueName);
                var mastered = IsMastered(item, inventory.Experience.GetValueOrDefault(item.UniqueName));
                var required = item.Components.Where(x => x.Tradable).Sum(x => x.Required);
                var have = item.Components.Where(x => x.Tradable)
                    .Sum(x => Math.Min(x.Required, inventory.Stackables.GetValueOrDefault(x.UniqueName)));
                var completion = owned || required == 0 ? (owned ? 1d : 0d) : (double)have / required;
                var status = owned && mastered ? "Coleção + mastery" : owned ? "Mastery pendente" :
                    mastered ? "Já dominado; falta possuir" : required > 0 && have >= required ? "Pronto para construir" : "Em progresso";
                return new CollectionGoal(item.Name, item.Category, owned, mastered, have, required, completion, status);
            }).OrderBy(x => x.Category).ThenBy(x => x.ItemName).ToArray();

    private static Dictionary<string, int> BuildReservations(InventorySnapshot inventory, CatalogSnapshot catalog,
        IReadOnlyList<MarketOrder> myOrders, RecommendationSettings settings)
    {
        var reserved = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in catalog.Items.Where(x => x.Masterable && x.Components.Count > 0))
        {
            var owned = inventory.OwnedEquipment.Contains(item.UniqueName);
            var mastered = IsMastered(item, inventory.Experience.GetValueOrDefault(item.UniqueName));
            var keepBuilt = IsPermanentCollectionItem(item);
            var needBuild = !owned && (keepBuilt || !mastered);
            var speculate = settings.ReserveUnvaultedPrimeWarframeSet && item.Prime && IsWarframe(item) && !item.Vaulted;
            foreach (var component in item.Components.Where(x => x.Tradable))
            {
                var amount = (needBuild ? component.Required : 0) + (speculate ? component.Required : 0);
                reserved[component.UniqueName] = Math.Max(reserved.GetValueOrDefault(component.UniqueName), amount);
            }
        }

        var itemByMarketId = catalog.Items.Where(x => !string.IsNullOrWhiteSpace(x.MarketId))
            .ToDictionary(x => x.MarketId!, StringComparer.Ordinal);
        foreach (var order in myOrders.Where(x => x.Type.Equals("sell", StringComparison.OrdinalIgnoreCase)))
        {
            if (itemByMarketId.TryGetValue(order.ItemId, out var item))
                reserved[item.UniqueName] = reserved.GetValueOrDefault(item.UniqueName) + order.Quantity;
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
            var setCount = tradable.Min(x => excess.GetValueOrDefault(x.UniqueName) / x.Required);
            if (setCount <= 0 || !quotes.TryGetValue(parent.MarketSlug!, out var setQuote) || setQuote.LowestSell is null) continue;
            var partValue = tradable.Sum(component =>
            {
                var identity = MarketForComponent(parent, component, catalog);
                return identity is not null && quotes.TryGetValue(identity.Slug, out var quote)
                    ? (quote.LowestSell ?? 0) * component.Required : 0;
            });
            if (setQuote.LowestSell < partValue) continue;

            foreach (var component in tradable) excess[component.UniqueName] -= setCount * component.Required;
            results.Add(new SaleRecommendation(parent.Name + " Set", parent.UniqueName, parent.MarketSlug,
                setCount, 0, setCount, tradable.Sum(x => x.Ducats * x.Required), setQuote.LowestSell,
                setQuote.HighestBuy, RecommendationAction.SellForPlatinum, parent.Vaulted,
                HasOrder(parent.MarketId, orders), "Set completo vale mais ou igual à soma das peças."));
        }

        foreach (var pair in excess.Where(x => x.Value > 0))
        {
            if (!componentInfo.TryGetValue(pair.Key, out var info) || !info.Component.Tradable) continue;
            var identity = MarketForComponent(info.Parent, info.Component, catalog);
            quotes.TryGetValue(identity?.Slug ?? "", out var quote);
            var platinumWins = quote?.LowestSell is > 0 && quote.LowestSell.Value * settings.DucatsPerPlatinum >= info.Component.Ducats;
            var action = platinumWins ? RecommendationAction.SellForPlatinum : RecommendationAction.ExchangeForDucats;
            var reason = platinumWins
                ? $"Cotação supera o corte de 1p/{settings.DucatsPerPlatinum:0.#} ducats."
                : quote is null ? "Sem cotação pública; valor disponível em ducats." : "Ducats superam o corte configurado.";
            results.Add(new SaleRecommendation(info.DisplayName, pair.Key, identity?.Slug,
                inventory.Stackables.GetValueOrDefault(pair.Key), reservations.GetValueOrDefault(pair.Key), pair.Value,
                info.Component.Ducats, quote?.LowestSell, quote?.HighestBuy, action, info.Parent.Vaulted,
                HasOrder(identity?.Id, orders), reason));
        }

        return results.OrderBy(x => x.Action).ThenByDescending(x => x.TotalPlatinum ?? 0)
            .ThenByDescending(x => x.TotalDucats).ToArray();
    }

    private static IReadOnlyList<FarmRecommendation> BuildFarm(InventorySnapshot inventory, CatalogSnapshot catalog,
        IReadOnlyList<CollectionGoal> collection, IReadOnlyDictionary<string, MarketQuote> quotes)
    {
        var incomplete = collection.Where(x => !x.Owned).Select(x => x.ItemName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relicByName = catalog.Items.Where(x => x.Category.Contains("Relic", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Name, x => x.UniqueName, StringComparer.OrdinalIgnoreCase);
        return catalog.Items.Where(x => incomplete.Contains(x.Name) && x.Components.Any(c => c.Tradable))
            .Select(item =>
            {
                var missing = item.Components.Where(x => x.Tradable && inventory.Stackables.GetValueOrDefault(x.UniqueName) < x.Required).ToArray();
                var ownedRelics = item.Relics.Select(x => x.RelicName).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(name => relicByName.TryGetValue(name, out var unique) && inventory.Stackables.GetValueOrDefault(unique) > 0);
                quotes.TryGetValue(item.MarketSlug ?? "", out var quote);
                return new FarmRecommendation(item.Name, item.Category, missing.Length,
                    item.Components.Count(x => x.Tradable), ownedRelics, item.Vaulted, quote?.LowestSell,
                    missing.Select(x => x.Name).ToArray(), $"Faltam {missing.Length} peças; {ownedRelics} relíquias úteis possuídas.");
            }).OrderBy(x => x.MissingParts).ThenByDescending(x => x.OwnedRelics)
            .ThenByDescending(x => x.EstimatedPlatinum ?? 0).ToArray();
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

    private static bool IsRelevantGoal(CatalogItem item) => !item.Category.Contains("Skin", StringComparison.OrdinalIgnoreCase);
    private static bool IsPermanentCollectionItem(CatalogItem item) => IsWarframe(item) ||
        item.ProductCategory is "Sentinels" or "KubrowPets" or "SpaceSuits" or "MechSuits" ||
        item.Prime || (item.Tradable && (item.Vaulted || item.EstimatedVaultDate is not null));
    private static bool IsWarframe(CatalogItem item) => item.ProductCategory == "Suits" || item.Category == "Warframes";
    private static bool IsMastered(CatalogItem item, long xp)
    {
        var suitLike = item.ProductCategory is "Suits" or "Sentinels" or "KubrowPets" or "SpaceSuits" or "MechSuits";
        return xp >= (suitLike ? 900_000 : 450_000);
    }
    private static bool HasOrder(string? marketId, IReadOnlyList<MarketOrder> orders) => !string.IsNullOrWhiteSpace(marketId) && orders.Any(x => x.ItemId == marketId && x.Type == "sell");
    private sealed record ComponentDetails(CatalogItem Parent, CatalogComponent Component, string DisplayName);
}
