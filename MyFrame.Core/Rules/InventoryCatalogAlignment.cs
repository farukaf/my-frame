namespace MyFrame.Core;

/// <summary>
/// AlecaFrame stores an owned part under the unique name of its recipe, while the catalog
/// describes the same piece as a component of the parent item. For warframe parts the recipe is
/// <c>…ChassisBlueprint</c> and the component is <c>…ChassisComponent</c>; for weapon parts the
/// recipe is <c>…StockBlueprint</c> and the component is the bare <c>…Stock</c>. Both spellings
/// point at the same tradable item, so the stack has to be re-keyed onto the catalog name before
/// any lookup. Without this, pieces such as the Oberon Prime Chassis are invisible to collection
/// progress, market quote selection and sale recommendations.
/// </summary>
public static class InventoryCatalogAlignment
{
    private const string BlueprintSuffix = "Blueprint";
    private const string ComponentSuffix = "Component";

    public static InventorySnapshot AlignToCatalog(InventorySnapshot inventory, CatalogSnapshot catalog)
    {
        var known = KnownUniqueNames(catalog);
        Dictionary<string, int>? aligned = null;
        foreach (var stack in inventory.Stackables)
        {
            if (known.Contains(stack.Key) || !TryResolveAlias(stack.Key, known, out var alias)) continue;
            aligned ??= new Dictionary<string, int>(inventory.Stackables, StringComparer.Ordinal);
            aligned.Remove(stack.Key);
            aligned[alias] = aligned.GetValueOrDefault(alias) + stack.Value;
        }

        return aligned is null ? inventory : inventory with { Stackables = aligned };
    }

    private static HashSet<string> KnownUniqueNames(CatalogSnapshot catalog)
    {
        var known = new HashSet<string>(catalog.ByUniqueName.Keys, StringComparer.Ordinal);
        foreach (var item in catalog.Items)
            foreach (var component in item.Components)
                known.Add(component.UniqueName);
        return known;
    }

    // Only aliases that exist in the catalog are accepted, so an unrelated recipe keeps its own key.
    private static bool TryResolveAlias(string uniqueName, HashSet<string> known, out string alias)
    {
        alias = "";
        if (!uniqueName.EndsWith(BlueprintSuffix, StringComparison.Ordinal)) return false;
        var stem = uniqueName[..^BlueprintSuffix.Length];
        if (stem.Length == 0) return false;

        var component = stem + ComponentSuffix;
        if (known.Contains(component)) alias = component;
        else if (known.Contains(stem)) alias = stem;
        return alias.Length > 0;
    }
}
