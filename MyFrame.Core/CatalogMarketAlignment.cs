namespace MyFrame.Core;

/// <summary>
/// Corrects the catalogue against Warframe.Market's own item list.
///
/// AlecaFrame's catalogue is the source for names, components and ducats, but it is unreliable about
/// trading: for many parts it carries no market identity at all and marks the component
/// <c>tradable: false</c>. Parallax Avionics is the plain example — the catalogue calls it
/// untradable and nameless to the market, while the market lists it as
/// <c>parallax_avionics_blueprint</c> with live sell orders.
///
/// The market is the authority on what the market will trade, so where it names a part, that part is
/// tradable and gets its slug. Nothing is ever taken away: a part the catalogue already calls
/// tradable stays tradable, and an identity the catalogue already knows is left alone, because those
/// were resolved against this same naming convention and are known to work.
/// </summary>
public static class CatalogMarketAlignment
{
    public static CatalogSnapshot AlignToMarket(CatalogSnapshot catalog, MarketItemIndex? index)
    {
        if (index is null || index.ByNormalizedName.Count == 0) return catalog;

        var identities = new Dictionary<string, MarketIdentity>(
            catalog.MarketByNormalizedName, StringComparer.Ordinal);
        foreach (var entry in index.ByNormalizedName)
            identities.TryAdd(entry.Key, entry.Value);

        var items = new List<CatalogItem>(catalog.Items.Count);
        var changed = false;
        foreach (var item in catalog.Items)
        {
            var components = AlignComponents(item, identities);
            if (components is null) { items.Add(item); continue; }
            changed = true;
            items.Add(item with { Components = components });
        }

        if (!changed && identities.Count == catalog.MarketByNormalizedName.Count) return catalog;
        return new CatalogSnapshot(items,
            items.ToDictionary(x => x.UniqueName, StringComparer.Ordinal), identities);
    }

    private static IReadOnlyList<CatalogComponent>? AlignComponents(CatalogItem item,
        IReadOnlyDictionary<string, MarketIdentity> identities)
    {
        List<CatalogComponent>? aligned = null;
        for (var index = 0; index < item.Components.Count; index++)
        {
            var component = item.Components[index];
            if (component.Tradable || !identities.ContainsKey(DisplayKey(item, component))) continue;
            aligned ??= [.. item.Components];
            aligned[index] = component with { Tradable = true };
        }
        return aligned;
    }

    // Matches the name the engine builds for a part, which is also the name the market uses for it.
    private static string DisplayKey(CatalogItem item, CatalogComponent component) =>
        ItemNameNormalizer.Normalize(
            component.Name.Equals("Blueprint", StringComparison.OrdinalIgnoreCase)
                ? $"{item.Name} Blueprint" : $"{item.Name} {component.Name}");
}
