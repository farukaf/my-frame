using System.Text.Json;
using MyFrame.Core;

namespace MyFrame.Mcp;

public sealed class MyFrameQueryService
{
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 200;
    private const int MaximumSerializedItemsBytes = 60 * 1024;

    private readonly IMyFrameSnapshotProvider _snapshots;
    private readonly CursorCodec _cursors;
    private readonly JsonSerializerOptions _json;
    private readonly TimeProvider _time;

    public MyFrameQueryService(IMyFrameSnapshotProvider snapshots, CursorCodec cursors,
        JsonSerializerOptions json, TimeProvider time)
    {
        _snapshots = snapshots;
        _cursors = cursors;
        _json = json;
        _time = time;
    }

    public async Task<OverviewResponse> GetOverviewAsync(bool includeAccount, string? snapshotId,
        CancellationToken cancellationToken)
    {
        var snapshot = await SnapshotAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        var recommendations = snapshot.Recommendations;
        var inventory = snapshot.Inventory;
        var collection = recommendations?.Collection ?? [];
        var overview = new OverviewDto(snapshot.SetupRequired,
            inventory?.Stackables.Count ?? 0, inventory?.OwnedEquipment.Count ?? 0,
            inventory?.PlayerLevel ?? 0, inventory?.TradesRemaining ?? 0,
            collection.Count, collection.Count(x => x.Mastered), recommendations?.Farm.Count ?? 0,
            recommendations?.Sales.Count ?? 0, recommendations?.Relics.Count ?? 0,
            recommendations?.Surplus.Sum(x => x.Surplus) ?? 0,
            recommendations?.EstimatedPlatinum ?? 0, recommendations?.TotalDucats ?? 0,
            snapshot.Quotes.Values.Count(x => !x.IsStale), snapshot.Quotes.Values.Count(x => x.IsStale),
            "Stackable quantities are aggregated. Equipment presence is known, but equipment quantity is not.",
            snapshot.Settings is null ? null : new(snapshot.Settings.DucatsPerPlatinum,
                snapshot.Settings.UnvaultedPrimeSetsToReserve, snapshot.Settings.Revision),
            includeAccount && snapshot.Account is not null ? new(snapshot.Account.IngameName,
                snapshot.Account.Platform, snapshot.Sources.GetValueOrDefault("orders")?.RetrievedAt) : null);
        return new(Meta(snapshot), overview);
    }

    public async Task<PageResponse<InventoryItemDto>> SearchInventoryAsync(
        string? text, string? entityType, string? category, int? minimumQuantity,
        string state, bool includeUnknownQuantity, int limit, string? cursor, string? snapshotId,
        CancellationToken cancellationToken)
    {
        limit = ValidateLimit(limit);
        if (minimumQuantity is < 0)
            throw new QueryProblemException("INVALID_ARGUMENT", "minimumQuantity cannot be negative.");
        entityType = NormalizeOptionalEnum(entityType, "entityType",
            "equipment", "component", "relic", "item", "stackable");
        state = NormalizeEnum(state, "state", "all", "built", "stackable");
        var query = QueryKey(text, entityType, category, minimumQuantity, state, includeUnknownQuantity);
        var (snapshot, offset) = await ResolveAsync("search_inventory", query, cursor, snapshotId,
            cancellationToken).ConfigureAwait(false);
        RequireInventory(snapshot);
        var values = Inventory(snapshot)
            .Where(x => Match(text, x.Name, x.ItemId, x.Category))
            .Where(x => string.IsNullOrWhiteSpace(entityType) || x.EntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(category) || x.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Where(x => state == "all" || state == "built" && x.Built || state == "stackable" && x.Stackable)
            .Where(x => minimumQuantity is null || x.Quantity is >= 0 && x.Quantity >= minimumQuantity ||
                        includeUnknownQuantity && !x.QuantityKnown)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ItemId, StringComparer.Ordinal)
            .ToArray();
        return Page("search_inventory", query, snapshot, values, offset, limit,
            items => new(items.Length, Quantity: items.Where(x => x.QuantityKnown).Sum(x => x.Quantity ?? 0)));
    }

    public async Task<ItemResponse> GetItemAsync(string itemId, string section, int limit,
        string? cursor, string? snapshotId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId) || itemId.Length > 512)
            throw new QueryProblemException("INVALID_ARGUMENT", "itemId must contain 1 to 512 characters.");
        section = NormalizeEnum(section, "section", "all", "summary", "components", "relics", "recommendations");
        limit = ValidateLimit(limit);
        var query = QueryKey(itemId, section);
        var (snapshot, offset) = await ResolveAsync("get_item", query, cursor, snapshotId,
            cancellationToken).ConfigureAwait(false);
        RequireInventory(snapshot);
        var inventoryEntry = Inventory(snapshot).FirstOrDefault(x => x.ItemId == itemId);
        var catalog = snapshot.Catalog!;
        CatalogItem? parent = catalog.ByUniqueName.GetValueOrDefault(itemId);
        CatalogComponent? selectedComponent = null;
        if (parent is null)
        {
            foreach (var candidate in catalog.Items)
            {
                selectedComponent = candidate.Components.FirstOrDefault(x => x.UniqueName == itemId);
                if (selectedComponent is not null) { parent = candidate; break; }
            }
        }
        if (parent is null && inventoryEntry is null)
            return new(Meta(snapshot), null, new("ITEM_NOT_FOUND", "No item has the requested itemId.", false),
                section, 0, 0, null);

        var recommendation = snapshot.Recommendations!;
        var item = parent;
        var components = item?.Components.Select(component =>
        {
            var sale = recommendation.Sales.FirstOrDefault(x => x.UniqueName == component.UniqueName);
            var identity = MarketIdentity(item, component, catalog);
            var reserved = recommendation.Reservations?.GetValueOrDefault(component.UniqueName) ?? sale?.Reserved ?? 0;
            var allocated = recommendation.SetAllocations?.GetValueOrDefault(component.UniqueName) ?? 0;
            var owned = snapshot.Inventory!.Stackables.GetValueOrDefault(component.UniqueName);
            return new ComponentDto(component.UniqueName,
                component.Name.Equals("Blueprint", StringComparison.OrdinalIgnoreCase)
                    ? $"{item!.Name} Blueprint" : $"{item!.Name} {component.Name}",
                owned, component.Required, Math.Max(0, component.Required - owned),
                reserved, allocated, Math.Max(0, owned - reserved - allocated), component.Ducats, component.Tradable,
                identity?.Slug, Price(snapshot, identity?.Slug));
        }).ToArray() ?? [];
        var evidence = new List<EvidenceDto>();
        foreach (var value in recommendation.Sales.Where(x => x.UniqueName == itemId || x.ItemName == item?.Name))
            evidence.Add(new(value.ReasonCode, value.Reason, new Dictionary<string, string>
            {
                ["owned"] = value.Owned.ToString(), ["reserved"] = value.Reserved.ToString(),
                ["availableToSell"] = value.Excess.ToString()
            }));
        foreach (var value in recommendation.Farm.Where(x => x.ItemName == item?.Name))
            evidence.Add(new(value.ReasonCode, value.Reason, new Dictionary<string, string>
            {
                ["missingUnits"] = value.MissingUnits.ToString(),
                ["priceCoverage"] = $"{value.PriceCountKnown}/{value.PriceCountRequired}"
            }));
        var entity = inventoryEntry?.EntityType ?? (selectedComponent is null ? EntityType(item) : "component");
        var allComponents = components;
        var allRelics = item?.Relics.Select(x => x.RelicName).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var allEvidence = evidence.ToArray();
        var totalCount = section switch
        {
            "components" => allComponents.Length,
            "relics" => allRelics.Length,
            "recommendations" => allEvidence.Length,
            "summary" => 0,
            _ => allComponents.Length + allRelics.Length + allEvidence.Length
        };
        if (offset > totalCount || ((section is "all" or "summary") && offset != 0))
            throw new QueryProblemException("INVALID_CURSOR", "The cursor offset is outside this item section.");
        var returnedComponents = section == "components" ? allComponents.Skip(offset).Take(limit).ToArray() :
            section == "all" ? allComponents : [];
        var returnedRelics = section == "relics" ? allRelics.Skip(offset).Take(limit).ToArray() :
            section == "all" ? allRelics : [];
        var returnedEvidence = section == "recommendations" ? allEvidence.Skip(offset).Take(limit).ToArray() :
            section == "all" ? allEvidence : [];
        var count = section switch
        {
            "components" => returnedComponents.Length,
            "relics" => returnedRelics.Length,
            "recommendations" => returnedEvidence.Length,
            "summary" => 0,
            _ => totalCount
        };
        var nextOffset = offset + count;
        var next = (section is "components" or "relics" or "recommendations") && nextOffset < totalCount
            ? _cursors.Encode("get_item", snapshot.SnapshotId, query, nextOffset)
            : null;
        var dto = new ItemDto(itemId, entity, inventoryEntry?.Name ?? selectedComponent?.Name ?? item!.Name,
            item?.Category ?? inventoryEntry?.Category ?? "Unknown", item is not null,
            inventoryEntry?.Owned ?? snapshot.Inventory!.OwnedEquipment.Contains(itemId),
            inventoryEntry?.Quantity, inventoryEntry?.QuantityKnown ?? false,
            item is not null && item.IsMasteredWith(snapshot.Inventory!.Experience.GetValueOrDefault(item.UniqueName)),
            item?.Prime ?? false, item?.Vaulted ?? false, item?.MarketSlug,
            Price(snapshot, selectedComponent is null ? item?.MarketSlug : MarketIdentity(item!, selectedComponent, catalog)?.Slug),
            returnedComponents, returnedRelics, returnedEvidence);
        if (JsonSerializer.SerializeToUtf8Bytes(dto, _json).Length > MaximumSerializedItemsBytes)
            throw new QueryProblemException("RESULT_TOO_LARGE",
                "The item detail exceeds the response budget. Request summary or one paginated section.");
        return new(Meta(snapshot), dto, null, section, count, totalCount, next);
    }

    public async Task<PageResponse<CollectionDto>> ListCollectionAsync(string state, bool? prime,
        bool? vaulted, string? category, string? text, int limit, string? cursor, string? snapshotId,
        CancellationToken cancellationToken)
    {
        limit = ValidateLimit(limit);
        state = NormalizeEnum(state, "state", "all", "inProgress", "notOwned", "owned", "mastered");
        var query = QueryKey(state, prime, vaulted, category, text);
        var (snapshot, offset) = await ResolveAsync("list_collection", query, cursor, snapshotId, cancellationToken);
        RequireInventory(snapshot);
        var values = snapshot.Recommendations!.Collection.Select(value =>
        {
            var id = value.ItemId ?? snapshot.Catalog!.Items
                .Single(x => x.Name.Equals(value.ItemName, StringComparison.OrdinalIgnoreCase)).UniqueName;
            return new CollectionDto(id, value.ItemName, value.Category, value.Status, value.Owned,
                value.Mastered, value.Prime, value.Vaulted, value.Completion,
                value.OwnedComponents, value.RequiredComponents);
        }).Where(x => Match(text, x.Name, x.Category, x.Status))
          .Where(x => prime is null || x.Prime == prime)
          .Where(x => vaulted is null || x.Vaulted == vaulted)
          .Where(x => string.IsNullOrWhiteSpace(category) || x.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
          .Where(x => state switch
          {
              "inProgress" => !x.Owned && !x.Mastered && x.OwnedComponents > 0,
              "notOwned" => !x.Owned,
              "owned" => x.Owned,
              "mastered" => x.Mastered,
              _ => true
          }).OrderByDescending(x => x.Completion).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
          .ThenBy(x => x.ItemId, StringComparer.Ordinal).ToArray();
        return Page("list_collection", query, snapshot, values, offset, limit, x => new(x.Length));
    }

    public async Task<PageResponse<FarmDto>> ListFarmAsync(string? text, bool? vaulted,
        int? maximumMissingUnits, string? targetItemId, int limit, string? cursor, string? snapshotId,
        CancellationToken cancellationToken)
    {
        limit = ValidateLimit(limit);
        if (maximumMissingUnits is < 0)
            throw new QueryProblemException("INVALID_ARGUMENT", "maximumMissingUnits cannot be negative.");
        ValidateOptionalItemId(targetItemId, "targetItemId");
        var query = QueryKey(text, vaulted, maximumMissingUnits, targetItemId);
        var (snapshot, offset) = await ResolveAsync("list_farm", query, cursor, snapshotId, cancellationToken);
        RequireInventory(snapshot);
        var values = snapshot.Recommendations!.Farm.Select(value => new FarmDto(
            value.ItemId ?? snapshot.Catalog!.Items
                .Single(x => x.Name.Equals(value.ItemName, StringComparison.OrdinalIgnoreCase)).UniqueName,
            value.ItemName, value.Category, value.Vaulted, value.MissingParts, value.MissingUnits,
            value.MissingComponentNames, value.OwnedRelics, value.MissingPartsCostPlatinum,
            value.SetPurchasePricePlatinum, value.PriceCountKnown, value.PriceCountRequired,
            value.ReasonCode, value.Reason))
            .Where(x => Match(text, x.Name, x.Category, x.Explanation))
            .Where(x => vaulted is null || x.Vaulted == vaulted)
            .Where(x => maximumMissingUnits is null || x.MissingUnits <= maximumMissingUnits)
            .Where(x => string.IsNullOrWhiteSpace(targetItemId) || x.ItemId == targetItemId)
            .OrderBy(x => x.MissingUnits).ThenByDescending(x => x.OwnedUsefulRelicTypes)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ItemId, StringComparer.Ordinal).ToArray();
        return Page("list_farm", query, snapshot, values, offset, limit,
            x => new(x.Length, Platinum: CompleteSum(x.Select(y => y.MissingPartsCostPlatinum)),
                Complete: x.All(y => y.MissingPartsCostPlatinum is not null)));
    }

    public async Task<PageResponse<SaleDto>> ListSalesAsync(string action, bool? vaulted,
        bool? existingOrder, string? text, int limit, string? cursor, string? snapshotId,
        CancellationToken cancellationToken)
    {
        limit = ValidateLimit(limit);
        action = NormalizeEnum(action, "action", "all", "keep", "platinum", "ducats");
        var query = QueryKey(action, vaulted, existingOrder, text);
        var (snapshot, offset) = await ResolveAsync("list_sales", query, cursor, snapshotId, cancellationToken);
        RequireInventory(snapshot);
        var values = snapshot.Recommendations!.Sales.Select(value => new SaleDto(value.UniqueName,
            value.ItemName, Action(value.Action), value.Owned, value.Reserved, value.ReservedForCraft,
            value.ReservedForFutureSale, value.ReservedForOrders, value.Excess, value.LowestSell,
            value.TotalPlatinum, value.DucatsEach, value.TotalDucats, value.ExistingOrder,
            value.Vaulted, value.ReasonCode, value.Reason, value.AllocatedComponents))
            .Where(x => Match(text, x.Name, x.Explanation))
            .Where(x => action == "all" || x.Action == action)
            .Where(x => vaulted is null || x.Vaulted == vaulted)
            .Where(x => existingOrder is null || x.ExistingOrder == existingOrder)
            .OrderBy(x => x.Action, StringComparer.Ordinal).ThenByDescending(x => x.TotalPlatinum ?? 0)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ItemId, StringComparer.Ordinal).ToArray();
        return Page("list_sales", query, snapshot, values, offset, limit, x => new(x.Length,
            x.All(y => y.Action != "platinum" || y.TotalPlatinum is not null)
                ? x.Where(y => y.Action == "platinum").Sum(y => (long)(y.TotalPlatinum ?? 0)) : null,
            x.Where(y => y.Action == "ducats").Sum(y => (long)y.TotalDucats),
            x.Sum(y => y.AvailableToSell), snapshot.AvailabilityConfirmed));
    }

    public async Task<PageResponse<RelicDto>> ListRelicsAsync(string action, bool? vaulted,
        int? minimumOwned, string? targetItemId, string? text, int limit, string? cursor,
        string? snapshotId, CancellationToken cancellationToken)
    {
        limit = ValidateLimit(limit);
        if (minimumOwned is < 0)
            throw new QueryProblemException("INVALID_ARGUMENT", "minimumOwned cannot be negative.");
        ValidateOptionalItemId(targetItemId, "targetItemId");
        action = NormalizeEnum(action, "action", "all", "open", "sellSealed", "hold");
        var query = QueryKey(action, vaulted, minimumOwned, targetItemId, text);
        var (snapshot, offset) = await ResolveAsync("list_relics", query, cursor, snapshotId, cancellationToken);
        RequireInventory(snapshot);
        var target = string.IsNullOrWhiteSpace(targetItemId) ? null : snapshot.Catalog!.ByUniqueName.GetValueOrDefault(targetItemId);
        if (!string.IsNullOrWhiteSpace(targetItemId) && target is null)
            throw new QueryProblemException("ITEM_NOT_FOUND", "targetItemId was not found in the catalog.");
        var targetRewards = target?.Components.Select(component => component.Name.Equals("Blueprint", StringComparison.OrdinalIgnoreCase)
            ? $"{target.Name} Blueprint" : $"{target.Name} {component.Name}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var values = snapshot.Recommendations!.Relics
            .Where(value => targetRewards is null || snapshot.Catalog!.ByUniqueName[value.UniqueName].Relics
                .Any(reward => targetRewards.Contains(reward.RewardName)))
            .Select(value => new RelicDto(value.UniqueName, value.RelicName, value.Owned, value.Vaulted,
                value.Action == "Sell sealed" ? "sellSealed" : value.Action.ToLowerInvariant(),
                value.SellPriceEach, value.ExpectedOpenValueEach, value.CompleteExpectedOpenValueEach,
                value.RewardPricesKnown, value.RewardPricesRequired, "intact", "solo single relic",
                value.ReasonCode, value.Reason))
            .Where(x => Match(text, x.Name, x.Explanation))
            .Where(x => action == "all" || x.Action == action)
            .Where(x => vaulted is null || x.Vaulted == vaulted)
            .Where(x => minimumOwned is null || x.Owned >= minimumOwned)
            .OrderBy(x => x.Action, StringComparer.Ordinal).ThenByDescending(x =>
                Math.Max(x.SealedPricePlatinum ?? 0, x.CompleteExpectedOpenValuePlatinum ?? 0) * x.Owned)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ItemId, StringComparer.Ordinal).ToArray();
        return Page("list_relics", query, snapshot, values, offset, limit, x => new(x.Length,
            Platinum: CompleteSum(x.Select(y => y.CompleteExpectedOpenValuePlatinum is double value
                ? (long?)Math.Round(value * y.Owned) : null)), Quantity: x.Sum(y => y.Owned),
            Complete: x.All(y => y.CompleteExpectedOpenValuePlatinum is not null)));
    }

    public async Task<PageResponse<SurplusDto>> ListSurplusAsync(string reason, bool? hasPlatinumValue,
        string? text, int limit, string? cursor, string? snapshotId, CancellationToken cancellationToken)
    {
        limit = ValidateLimit(limit);
        reason = NormalizeEnum(reason, "reason", "all", "crafted", "mastered", "onlyOneNeeded");
        var query = QueryKey(reason, hasPlatinumValue, text);
        var (snapshot, offset) = await ResolveAsync("list_surplus", query, cursor, snapshotId, cancellationToken);
        RequireInventory(snapshot);
        var values = snapshot.Recommendations!.Surplus.Select(value => new SurplusDto(value.UniqueName,
            value.ItemName, value.ParentName, value.Category, value.Owned, value.StillNeeded,
            value.Surplus, value.Reserved, value.AllocatedToSets, value.AvailableToSell,
            value.LowestSell, value.LowestSell is null ? null : value.LowestSell * value.AvailableToSell,
            value.DucatsEach, value.Tradable ? value.DucatsEach * value.AvailableToSell : 0,
            value.Tradable, value.Reason.ToString(),
            value.ReasonCode, value.Explanation))
            .Where(x => Match(text, x.Name, x.ParentName, x.Category, x.Explanation))
            .Where(x => reason == "all" || NormalizeReason(x.Reason) == reason)
            .Where(x => hasPlatinumValue is null || (x.PlatinumEach is not null) == hasPlatinumValue)
            .OrderByDescending(x => x.TotalPlatinum ?? 0).ThenByDescending(x => x.SurplusForCollection)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ItemId, StringComparer.Ordinal).ToArray();
        return Page("list_surplus", query, snapshot, values, offset, limit, x => new(x.Length,
            Platinum: x.Where(y => y.TotalPlatinum is not null).Sum(y => (long)(y.TotalPlatinum ?? 0)),
            Ducats: x.Sum(y => (long)y.TotalDucats), Quantity: x.Sum(y => y.SurplusForCollection),
            Complete: snapshot.AvailabilityConfirmed));
    }

    private async Task<(MyFrameSnapshot Snapshot, int Offset)> ResolveAsync(string tool, string query,
        string? cursor, string? snapshotId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (await SnapshotAsync(snapshotId, cancellationToken), 0);
        var payload = _cursors.Decode(cursor, tool, query);
        if (!string.IsNullOrWhiteSpace(snapshotId) && snapshotId != payload.SnapshotId)
            throw new QueryProblemException("INVALID_CURSOR", "cursor and snapshotId refer to different snapshots.");
        try { return (await _snapshots.GetAsync(payload.SnapshotId, cancellationToken), payload.Offset); }
        catch (MyFrameSnapshotException error) when (error.Code == "SNAPSHOT_EXPIRED")
        {
            throw new QueryProblemException("CURSOR_EXPIRED", "The cursor snapshot expired. Restart from the first page.");
        }
    }

    private async Task<MyFrameSnapshot> SnapshotAsync(string? snapshotId, CancellationToken cancellationToken)
    {
        try { return await _snapshots.GetAsync(snapshotId, cancellationToken).ConfigureAwait(false); }
        catch (MyFrameSnapshotException error)
        {
            throw new QueryProblemException(error.Code, error.Message, error.Retryable);
        }
    }

    private PageResponse<T> Page<T>(string tool, string query, MyFrameSnapshot snapshot,
        T[] values, int offset, int limit, Func<T[], PageTotals> totals)
    {
        if (offset > values.Length)
            throw new QueryProblemException("INVALID_CURSOR", "The cursor offset is outside this result set.");
        var take = Math.Min(limit, values.Length - offset);
        T[] items;
        while (true)
        {
            items = values.Skip(offset).Take(take).ToArray();
            if (JsonSerializer.SerializeToUtf8Bytes(items, _json).Length <= MaximumSerializedItemsBytes) break;
            if (take <= 1) throw new QueryProblemException("RESULT_TOO_LARGE",
                "A single result exceeds the response budget. Use a narrower filter or get_item section.");
            take = Math.Max(1, take / 2);
        }
        var nextOffset = offset + items.Length;
        var next = nextOffset < values.Length ? _cursors.Encode(tool, snapshot.SnapshotId, query, nextOffset) : null;
        return new(Meta(snapshot), items.Length, values.Length, totals(values), next, items);
    }

    private ResponseMeta Meta(MyFrameSnapshot snapshot) => new("1", snapshot.Recommendations?.RulesVersion ?? "1",
        snapshot.SnapshotId, snapshot.GeneratedAt, snapshot.EvaluatedAt, _time.GetUtcNow(),
        snapshot.Inventory?.CapturedAt, snapshot.Sources.ToDictionary(x => x.Key,
            x => new SourceDto(x.Value.State, x.Value.RetrievedAt, x.Value.Fallback, x.Value.DetailCode), StringComparer.Ordinal),
        snapshot.Warnings.Select(x => new WarningDto(x.Code, x.Message)).ToArray(),
        snapshot.Sources.Values.All(x => x.State == "valid") && !snapshot.SetupRequired,
        snapshot.AvailabilityConfirmed);

    private static InventoryItemDto[] Inventory(MyFrameSnapshot snapshot)
    {
        var catalog = snapshot.Catalog!;
        var components = catalog.Items.SelectMany(parent => parent.Components.Select(component => (parent, component)))
            .GroupBy(x => x.component.UniqueName, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var values = new List<InventoryItemDto>();
        foreach (var pair in snapshot.Inventory!.Stackables)
        {
            if (catalog.ByUniqueName.TryGetValue(pair.Key, out var item))
                values.Add(new(pair.Key, EntityType(item), item.Name, item.Category, pair.Value, true,
                    pair.Value > 0, true, snapshot.Inventory.OwnedEquipment.Contains(pair.Key), true));
            else if (components.TryGetValue(pair.Key, out var value))
                values.Add(new(pair.Key, "component", value.component.Name.Equals("Blueprint", StringComparison.OrdinalIgnoreCase)
                    ? $"{value.parent.Name} Blueprint" : $"{value.parent.Name} {value.component.Name}",
                    value.parent.Category, pair.Value, true, pair.Value > 0, true, false, true));
            else values.Add(new(pair.Key, "stackable", pair.Key, "Unknown", pair.Value, true,
                    pair.Value > 0, false, false, true));
        }
        foreach (var id in snapshot.Inventory.OwnedEquipment)
        {
            catalog.ByUniqueName.TryGetValue(id, out var item);
            values.Add(new(id, item is null ? "equipment" : EntityType(item), item?.Name ?? id,
                item?.Category ?? "Unknown", null, false, true, item is not null, true, false));
        }
        return values.ToArray();
    }

    private static void RequireInventory(MyFrameSnapshot snapshot)
    {
        if (snapshot.SetupRequired) throw new QueryProblemException("SETUP_REQUIRED",
            "Open My Frame and save Settings before querying inventory.");
        if (snapshot.Inventory is null || snapshot.Catalog is null || snapshot.Recommendations is null)
            throw new QueryProblemException("SOURCE_UNAVAILABLE",
                "AlecaFrame inventory is unavailable. Open AlecaFrame or update My Frame Settings.", true);
    }

    private static int ValidateLimit(int limit)
    {
        if (limit == 0) return DefaultLimit;
        if (limit is < 1 or > MaximumLimit)
            throw new QueryProblemException("INVALID_ARGUMENT", $"limit must be between 1 and {MaximumLimit}.");
        return limit;
    }

    private static string NormalizeEnum(string? value, string name, params string[] allowed)
    {
        value = string.IsNullOrWhiteSpace(value) ? allowed[0] : value.Trim();
        var normalized = allowed.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
        return normalized ?? throw new QueryProblemException("INVALID_ARGUMENT",
            $"{name} must be one of: {string.Join(", ", allowed)}.");
    }

    private static string? NormalizeOptionalEnum(string? value, string name, params string[] allowed) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeEnum(value, name, allowed);

    private static void ValidateOptionalItemId(string? value, string name)
    {
        if (value?.Length > 512)
            throw new QueryProblemException("INVALID_ARGUMENT", $"{name} cannot exceed 512 characters.");
    }

    private static bool Match(string? query, params string?[] values) => string.IsNullOrWhiteSpace(query) ||
        values.Any(x => x?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) == true);

    private static string QueryKey(params object?[] values) => string.Join('|', values.Select(value =>
        value switch { null => "~", string text => text.Trim().ToUpperInvariant(), _ => value.ToString() }));

    private static string EntityType(CatalogItem? item) => item is null ? "item" :
        item.Category.Contains("Relic", StringComparison.OrdinalIgnoreCase) ? "relic" :
        item.Components.Count > 0 ? "equipment" : "item";

    private static string Action(RecommendationAction action) => action switch
    {
        RecommendationAction.SellForPlatinum => "platinum",
        RecommendationAction.ExchangeForDucats => "ducats",
        RecommendationAction.Keep => "keep",
        _ => "farm"
    };

    private static string NormalizeReason(string reason) => reason switch
    {
        nameof(SurplusReason.OnlyOneNeeded) => "onlyOneNeeded",
        nameof(SurplusReason.Mastered) => "mastered",
        _ => "crafted"
    };

    private static long? CompleteSum(IEnumerable<int?> values)
    {
        var materialized = values.ToArray();
        return materialized.All(x => x is not null) ? materialized.Sum(x => (long)(x ?? 0)) : null;
    }

    private static long? CompleteSum(IEnumerable<long?> values)
    {
        var materialized = values.ToArray();
        return materialized.All(x => x is not null) ? materialized.Sum(x => x ?? 0) : null;
    }

    private static MarketIdentity? MarketIdentity(CatalogItem parent, CatalogComponent component,
        CatalogSnapshot catalog)
    {
        var name = component.Name.Equals("Blueprint", StringComparison.OrdinalIgnoreCase)
            ? $"{parent.Name} Blueprint" : $"{parent.Name} {component.Name}";
        return catalog.MarketByNormalizedName.GetValueOrDefault(ItemNameNormalizer.Normalize(name));
    }

    private static PriceDto? Price(MyFrameSnapshot snapshot, string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || !snapshot.Quotes.TryGetValue(slug, out var quote)) return null;
        return new(quote.LowestSell, quote.HighestBuy, quote.RetrievedAt,
            quote.IsStale ? "stale" : "available", quote.LowestSell is > 0 ? "lowestSell" : "highestBuy");
    }
}
