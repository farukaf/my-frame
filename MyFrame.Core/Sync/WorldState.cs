using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyFrame.Core.Sync;

public sealed record WorldStateReward(string Item, decimal? Chance, int? Count, string? Rarity);

public sealed record WorldStateJob(
    string Id,
    string? Type,
    string? UniqueName,
    int? MinimumMasteryRank,
    IReadOnlyList<int> StandingStages,
    IReadOnlyList<WorldStateReward> Rewards);

public sealed record WorldStateBounty(
    string Id,
    string? Syndicate,
    DateTimeOffset? Activation,
    DateTimeOffset? Expiry,
    IReadOnlyList<WorldStateJob> Jobs);

public sealed record WorldStateCycle(string Name, string? State, DateTimeOffset? Activation, DateTimeOffset? Expiry);

public sealed record WorldStateSnapshot(
    DateTimeOffset RetrievedAt,
    DateTimeOffset? SourceTimestamp,
    string? BuildLabel,
    IReadOnlyList<WorldStateBounty> Bounties,
    IReadOnlyList<WorldStateCycle> Cycles,
    string ContentHash,
    bool IsCurrent,
    IReadOnlyDictionary<string, InventoryFieldState> Coverage);

public static class WorldStateParser
{
    private static readonly string[] CycleNames = ["cambionCycle", "cetusCycle", "vallisCycle", "duviriCycle", "earthCycle", "zarimanCycle"];

    public static WorldStateSnapshot Parse(string json, DateTimeOffset retrievedAt, int maximumBounties = 500)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > 32 * 1024 * 1024) throw new InvalidDataException("WORLDSTATE_TOO_LARGE");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("WORLDSTATE_ROOT_INVALID");
        var root = document.RootElement;
        var sourceTimestamp = Date(root, "timestamp", "Timestamp");
        var bounties = ReadBounties(root, maximumBounties);
        var cycles = ReadCycles(root);
        var coverage = new Dictionary<string, InventoryFieldState>(StringComparer.Ordinal)
        {
            ["syndicateMissions"] = Property(root, out var missions, "syndicateMissions", "SyndicateMissions") && missions.ValueKind == JsonValueKind.Array ? InventoryFieldState.Known : InventoryFieldState.NotObserved,
            ["bountyRewards"] = bounties.Any(bounty => bounty.Jobs.Any(job => job.Rewards.Count > 0)) ? InventoryFieldState.Known : InventoryFieldState.NotObserved,
            ["motherTokens"] = bounties.Any(bounty => bounty.Jobs.Any(job => job.Rewards.Any(reward =>
                reward.Item.Contains("mother token", StringComparison.OrdinalIgnoreCase))))
                ? InventoryFieldState.Known
                : InventoryFieldState.NotObserved
        };
        return new(retrievedAt, sourceTimestamp, String(root, "buildLabel", "BuildLabel"), bounties, cycles, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(), sourceTimestamp is null || sourceTimestamp <= retrievedAt.AddMinutes(5), coverage);
    }

    public static IReadOnlyList<WorldStateBounty> CurrentBounties(WorldStateSnapshot snapshot, DateTimeOffset now) =>
        snapshot.Bounties.Where(bounty => bounty.Expiry is null || bounty.Expiry > now).Where(bounty => bounty.Activation is null || bounty.Activation <= now).ToArray();

    private static IReadOnlyList<WorldStateBounty> ReadBounties(JsonElement root, int max)
    {
        if (!Property(root, out var missions, "syndicateMissions", "SyndicateMissions") || missions.ValueKind != JsonValueKind.Array) return [];
        var result = new List<WorldStateBounty>();
        foreach (var mission in missions.EnumerateArray())
        {
            if (result.Count >= max) throw new InvalidDataException("WORLDSTATE_BOUNTIES_TOO_LARGE");
            if (mission.ValueKind != JsonValueKind.Object) continue;
            var jobs = new List<WorldStateJob>();
            if (Property(mission, out var jobArray, "jobs", "Jobs") && jobArray.ValueKind == JsonValueKind.Array)
                foreach (var job in jobArray.EnumerateArray()) if (job.ValueKind == JsonValueKind.Object) jobs.Add(ReadJob(job));
            var tag = String(mission, "syndicate", "Syndicate", "Tag");
            result.Add(new(String(mission, "id", "Id") ?? $"{tag ?? "unknown"}:{result.Count}", SyndicateName(tag), Date(mission, "activation", "Activation"), Date(mission, "expiry", "Expiry"), jobs));
        }
        return result;
    }

    private static WorldStateJob ReadJob(JsonElement job)
    {
        var stages = Property(job, out var stageArray, "standingStages", "StandingStages", "xpAmounts") && stageArray.ValueKind == JsonValueKind.Array
            ? stageArray.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _)).Select(value => value.GetInt32()).ToArray() : [];
        var rewards = new List<WorldStateReward>();
        if (Property(job, out var rewardArray, "rewardPoolDrops", "RewardPoolDrops") && rewardArray.ValueKind == JsonValueKind.Array)
            foreach (var reward in rewardArray.EnumerateArray())
            {
                if (reward.ValueKind != JsonValueKind.Object || String(reward, "item", "Item") is not { } item) continue;
                rewards.Add(new(item, Decimal(reward, "chance"), Int(reward, "count"), String(reward, "rarity")));
            }
        return new(String(job, "id", "Id", "JobCurrentVersion") ?? "unknown", String(job, "type", "Type", "jobType"), String(job, "uniqueName", "UniqueName", "rewards"), Int(job, "minMR", "MinMR", "masteryReq"), stages, rewards);
    }

    private static IReadOnlyList<WorldStateCycle> ReadCycles(JsonElement root) => CycleNames.Where(name => Property(root, out _, name, char.ToUpperInvariant(name[0]) + name[1..])).Select(name =>
    {
        Property(root, out var cycle, name, char.ToUpperInvariant(name[0]) + name[1..]);
        return new WorldStateCycle(name, String(cycle, "state", "State"), Date(cycle, "activation", "Activation"), Date(cycle, "expiry", "Expiry"));
    }).ToArray();

    private static string? String(JsonElement element, params string[] names) => names.Select(name => Property(element, out var value, name) && value.ValueKind == JsonValueKind.String ? value.GetString() : null).FirstOrDefault(value => value is not null);
    private static int? Int(JsonElement element, params string[] names) => names.Select(name => Property(element, out var value, name) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : (int?)null).FirstOrDefault(value => value is not null);
    private static decimal? Decimal(JsonElement element, params string[] names) => names.Select(name => Property(element, out var value, name) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result) ? result : (decimal?)null).FirstOrDefault(value => value is not null);
    private static DateTimeOffset? Date(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (Property(element, out var value, name))
            {
                if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed)) return parsed;
                if (value.ValueKind == JsonValueKind.Object && Property(value, out var date, "$date") && date.ValueKind == JsonValueKind.Object &&
                    Property(date, out var number, "$numberLong") && long.TryParse(number.GetString(), out var milliseconds))
                    return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            }
        return null;
    }

    private static bool Property(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names) if (element.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }

    private static string? SyndicateName(string? tag) => tag switch
    {
        null => null,
        var value when value.Contains("Deimos", StringComparison.OrdinalIgnoreCase) => "Entrati",
        var value when value.Contains("Cetus", StringComparison.OrdinalIgnoreCase) => "Ostrons",
        var value when value.Contains("Venus", StringComparison.OrdinalIgnoreCase) => "Solaris United",
        _ => tag
    };
}

public sealed class WorldStateClient(HttpClient httpClient, bool allowCommunityFallback = false)
{
    public const string DefaultUrl = "https://content.warframe.com/dynamic/worldState.php";
    public const string CommunityFallbackUrl = "https://api.warframestat.us/pc";
    public async Task<(WorldStateSnapshot Snapshot, SyncBatch Batch)> FetchAsync(Uri? uri = null, CancellationToken cancellationToken = default)
    {
        var endpoint = uri ?? new Uri(DefaultUrl);
        try
        {
            return await FetchEndpointAsync(endpoint, cancellationToken);
        }
        catch (Exception error) when (allowCommunityFallback && uri is null && IsTransportFailure(error, cancellationToken))
        {
            return await FetchEndpointAsync(new Uri(CommunityFallbackUrl), cancellationToken);
        }
    }

    private async Task<(WorldStateSnapshot Snapshot, SyncBatch Batch)> FetchEndpointAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var response = await SyncHttp.GetAsync(httpClient, endpoint, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"WORLDSTATE_HTTP_{(int)response.StatusCode}");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0 || bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("WORLDSTATE_TOO_LARGE");
        var json = new UTF8Encoding(false, true).GetString(bytes);
        var retrieved = DateTimeOffset.UtcNow;
        var snapshot = WorldStateParser.Parse(json, retrieved);
        return (snapshot, new SyncBatch("worldstate-pc", snapshot.ContentHash, json, snapshot.Bounties.Count, ParserVersion(endpoint)));
    }

    private static bool IsTransportFailure(Exception error, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && error is HttpRequestException or IOException or TaskCanceledException;

    private static string ParserVersion(Uri endpoint) => endpoint.Host.ToLowerInvariant() switch
    {
        "content.warframe.com" => "worldstate-official-1",
        "api.warframestat.us" => "worldstate-community-1",
        _ => "worldstate-1"
    };
}
