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
        var sourceTimestamp = Date(root, "timestamp");
        var bounties = ReadBounties(root, maximumBounties);
        var cycles = ReadCycles(root);
        var coverage = new Dictionary<string, InventoryFieldState>(StringComparer.Ordinal)
        {
            ["syndicateMissions"] = root.TryGetProperty("syndicateMissions", out var missions) && missions.ValueKind == JsonValueKind.Array ? InventoryFieldState.Known : InventoryFieldState.NotObserved,
            ["bountyRewards"] = bounties.Any(bounty => bounty.Jobs.Any(job => job.Rewards.Count > 0)) ? InventoryFieldState.Known : InventoryFieldState.NotObserved,
            ["motherTokens"] = InventoryFieldState.NotObserved
        };
        return new(retrievedAt, sourceTimestamp, String(root, "buildLabel"), bounties, cycles, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(), sourceTimestamp is null || sourceTimestamp <= retrievedAt.AddMinutes(5), coverage);
    }

    public static IReadOnlyList<WorldStateBounty> CurrentBounties(WorldStateSnapshot snapshot, DateTimeOffset now) =>
        snapshot.Bounties.Where(bounty => bounty.Expiry is null || bounty.Expiry > now).Where(bounty => bounty.Activation is null || bounty.Activation <= now).ToArray();

    private static IReadOnlyList<WorldStateBounty> ReadBounties(JsonElement root, int max)
    {
        if (!root.TryGetProperty("syndicateMissions", out var missions) || missions.ValueKind != JsonValueKind.Array) return [];
        var result = new List<WorldStateBounty>();
        foreach (var mission in missions.EnumerateArray())
        {
            if (result.Count >= max) throw new InvalidDataException("WORLDSTATE_BOUNTIES_TOO_LARGE");
            if (mission.ValueKind != JsonValueKind.Object) continue;
            var jobs = new List<WorldStateJob>();
            if (mission.TryGetProperty("jobs", out var jobArray) && jobArray.ValueKind == JsonValueKind.Array)
                foreach (var job in jobArray.EnumerateArray()) if (job.ValueKind == JsonValueKind.Object) jobs.Add(ReadJob(job));
            result.Add(new(String(mission, "id") ?? $"unknown:{result.Count}", String(mission, "syndicate"), Date(mission, "activation"), Date(mission, "expiry"), jobs));
        }
        return result;
    }

    private static WorldStateJob ReadJob(JsonElement job)
    {
        var stages = job.TryGetProperty("standingStages", out var stageArray) && stageArray.ValueKind == JsonValueKind.Array
            ? stageArray.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _)).Select(value => value.GetInt32()).ToArray() : [];
        var rewards = new List<WorldStateReward>();
        if (job.TryGetProperty("rewardPoolDrops", out var rewardArray) && rewardArray.ValueKind == JsonValueKind.Array)
            foreach (var reward in rewardArray.EnumerateArray())
            {
                if (reward.ValueKind != JsonValueKind.Object || String(reward, "item") is not { } item) continue;
                rewards.Add(new(item, Decimal(reward, "chance"), Int(reward, "count"), String(reward, "rarity")));
            }
        return new(String(job, "id") ?? "unknown", String(job, "type"), String(job, "uniqueName"), Int(job, "minMR"), stages, rewards);
    }

    private static IReadOnlyList<WorldStateCycle> ReadCycles(JsonElement root) => CycleNames.Where(name => root.TryGetProperty(name, out _)).Select(name =>
    {
        var cycle = root.GetProperty(name);
        return new WorldStateCycle(name, String(cycle, "state"), Date(cycle, "activation"), Date(cycle, "expiry"));
    }).ToArray();

    private static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? Int(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;
    private static decimal? Decimal(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result) ? result : null;
    private static DateTimeOffset? Date(JsonElement element, string name) => String(element, name) is { } value && DateTimeOffset.TryParse(value, out var result) ? result : null;
}

public sealed class WorldStateClient(HttpClient httpClient)
{
    public const string DefaultUrl = "https://api.warframestat.us/pc";
    public async Task<(WorldStateSnapshot Snapshot, SyncBatch Batch)> FetchAsync(Uri? uri = null, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(uri ?? new Uri(DefaultUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"WORLDSTATE_HTTP_{(int)response.StatusCode}");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0 || bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("WORLDSTATE_TOO_LARGE");
        var json = new UTF8Encoding(false, true).GetString(bytes);
        var retrieved = DateTimeOffset.UtcNow;
        var snapshot = WorldStateParser.Parse(json, retrieved);
        return (snapshot, new SyncBatch("worldstate-pc", snapshot.ContentHash, json, snapshot.Bounties.Count, "worldstate-1"));
    }
}
