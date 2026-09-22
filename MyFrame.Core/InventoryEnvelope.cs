using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyFrame.Core;

public enum InventoryFieldState { Known, NotObserved, NotSupported, Invalid }

public sealed record InventoryEnvelope(
    int SchemaVersion,
    int GameId,
    string Source,
    Guid SessionId,
    Guid EventId,
    long Sequence,
    DateTimeOffset ReceivedAt,
    string? ProviderVersion,
    string Completeness,
    string PayloadJson,
    string ContentHash,
    string CaptureMode = "snapshot",
    string? ContextId = null);

public sealed record InventoryEquipmentRecord(
    string InstanceId,
    string? TypeId,
    int? Rank,
    string? ConfigJson,
    InventoryFieldState RankState,
    InventoryFieldState ConfigState,
    string RawJson);

public sealed record InventoryStackableRecord(
    string? TypeId,
    int? Quantity,
    InventoryFieldState QuantityState,
    string RawJson);

public sealed record InventoryUpgradeRecord(
    string? OwnerInstanceId,
    string SourceField,
    string? UpgradeId,
    int? Rank,
    string RawJson);

public sealed record InventoryUnknownRecord(string Kind, string RawJson, string ReasonCode);

public sealed record InventoryProjection(
    IReadOnlyList<InventoryEquipmentRecord> Equipment,
    IReadOnlyList<InventoryStackableRecord> Stackables,
    IReadOnlyList<InventoryUnknownRecord> Unknown,
    IReadOnlyDictionary<string, InventoryFieldState> Coverage,
    IReadOnlyList<InventoryUpgradeRecord>? Upgrades = null);

public static class InventoryEnvelopeParser
{
    public const int MaximumEnvelopeBytes = 16 * 1024 * 1024;
    public const int MaximumPayloadBytes = 8 * 1024 * 1024;

    public static InventoryEnvelope Parse(string envelopeJson)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson) || Encoding.UTF8.GetByteCount(envelopeJson) > MaximumEnvelopeBytes)
            throw new InvalidDataException("INVENTORY_ENVELOPE_TOO_LARGE");
        try
        {
            using var document = JsonDocument.Parse(envelopeJson, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            var schema = RequiredInt(root, "schemaVersion");
            var game = RequiredInt(root, "gameId");
            if (schema != 1 || game != 8954 || RequiredString(root, "source") != "overwolf-native" || RequiredString(root, "kind") != "inventory") throw Invalid("INVENTORY_ENVELOPE_UNSUPPORTED");
            var session = RequiredGuid(root, "sessionId");
            var eventId = RequiredGuid(root, "eventId");
            var sequence = RequiredLong(root, "sequence");
            if (sequence <= 0) throw Invalid("INVENTORY_SEQUENCE_INVALID");
            var payload = RequiredString(root, "payload");
            if (Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes) throw Invalid("INVENTORY_PAYLOAD_TOO_LARGE");
            var encoding = RequiredString(root, "encoding");
            if (encoding != "json-object" && encoding != "json-string") throw Invalid("INVENTORY_ENCODING_UNSUPPORTED");
            using var payloadDocument = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 64 });
            if (payloadDocument.RootElement.ValueKind != JsonValueKind.Object) throw Invalid("INVENTORY_PAYLOAD_ROOT_INVALID");
            var completeness = root.TryGetProperty("completeness", out var complete) && complete.ValueKind == JsonValueKind.String ? complete.GetString()! : "unverified";
            if (completeness is not ("verified" or "unverified")) throw Invalid("INVENTORY_COMPLETENESS_INVALID");
            var captureMode = root.TryGetProperty("captureMode", out var mode) && mode.ValueKind == JsonValueKind.String ? mode.GetString()! : "snapshot";
            if (captureMode is not ("snapshot" or "delta")) throw Invalid("INVENTORY_CAPTURE_MODE_INVALID");
            var received = root.TryGetProperty("receivedAt", out var timestamp) && timestamp.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(timestamp.GetString(), out var parsed) ? parsed : throw Invalid("INVENTORY_TIMESTAMP_INVALID");
            var provider = root.TryGetProperty("providerVersion", out var version) && version.ValueKind == JsonValueKind.String ? version.GetString() : null;
            var context = root.TryGetProperty("contextId", out var contextValue) && contextValue.ValueKind == JsonValueKind.String
                ? contextValue.GetString() : null;
            if (context?.Length > 200) throw Invalid("INVENTORY_CONTEXT_INVALID");
            return new(schema, game, "overwolf-native", session, eventId, sequence, received, provider, completeness, payload, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(), captureMode, context);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or OverflowException or InvalidOperationException)
        { throw Invalid("INVENTORY_ENVELOPE_INVALID"); }
    }

    private static string RequiredString(JsonElement root, string property) => root.GetProperty(property).GetString() ?? throw Invalid("INVENTORY_PROPERTY_INVALID");
    private static int RequiredInt(JsonElement root, string property) => root.GetProperty(property).GetInt32();
    private static long RequiredLong(JsonElement root, string property) => root.GetProperty(property).GetInt64();
    private static Guid RequiredGuid(JsonElement root, string property) => Guid.TryParseExact(RequiredString(root, property), "D", out var value) ? value : throw Invalid("INVENTORY_ID_INVALID");
    private static InvalidDataException Invalid(string code) => new(code);
}

public static class InventoryPayloadParser
{
    public static InventoryProjection Parse(string payloadJson, int maximumRecords = 100_000)
    {
        using var document = JsonDocument.Parse(payloadJson, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("INVENTORY_PAYLOAD_ROOT_INVALID");
        var root = document.RootElement;
        var coverage = new Dictionary<string, InventoryFieldState>(StringComparer.Ordinal);
        var equipment = ReadEquipment(root, coverage, maximumRecords);
        var stackables = ReadStackables(root, coverage, maximumRecords);
        var upgrades = ReadUpgrades(root, coverage, maximumRecords);
        return new(equipment, stackables, ReadUnknown(root, equipment.Count + stackables.Count, maximumRecords), coverage, upgrades);
    }

    private static IReadOnlyList<InventoryEquipmentRecord> ReadEquipment(JsonElement root, Dictionary<string, InventoryFieldState> coverage, int max)
    {
        if (!root.TryGetProperty("equipment", out var value)) { coverage["equipment"] = InventoryFieldState.NotObserved; return []; }
        if (value.ValueKind != JsonValueKind.Array) { coverage["equipment"] = InventoryFieldState.Invalid; return []; }
        var result = new List<InventoryEquipmentRecord>();
        foreach (var item in value.EnumerateArray())
        {
            if (result.Count >= max) throw new InvalidDataException("INVENTORY_RECORDS_TOO_LARGE");
            var raw = item.GetRawText();
            if (item.ValueKind != JsonValueKind.Object) continue;
            var instance = StringValue(item, "instanceId") ?? StringValue(item, "id");
            var type = StringValue(item, "typeId") ?? StringValue(item, "uniqueName");
            if (instance is null) { result.Add(new($"unknown:{result.Count}", type, IntValue(item, "rank"), JsonValue(item, "config"), InventoryState(item, "rank"), item.TryGetProperty("config", out _) ? InventoryFieldState.Known : InventoryFieldState.NotObserved, raw)); continue; }
            result.Add(new(instance, type, IntValue(item, "rank"), JsonValue(item, "config"), item.TryGetProperty("rank", out _) ? InventoryFieldState.Known : InventoryFieldState.NotObserved, item.TryGetProperty("config", out _) ? InventoryFieldState.Known : InventoryFieldState.NotObserved, raw));
        }
        coverage["equipment"] = InventoryFieldState.Known;
        return result;
    }

    private static IReadOnlyList<InventoryStackableRecord> ReadStackables(JsonElement root, Dictionary<string, InventoryFieldState> coverage, int max)
    {
        if (!root.TryGetProperty("stackables", out var value)) { coverage["stackables"] = InventoryFieldState.NotObserved; return []; }
        if (value.ValueKind != JsonValueKind.Array) { coverage["stackables"] = InventoryFieldState.Invalid; return []; }
        var result = new List<InventoryStackableRecord>();
        foreach (var item in value.EnumerateArray())
        {
            if (result.Count >= max) throw new InvalidDataException("INVENTORY_RECORDS_TOO_LARGE");
            var quantity = IntValue(item, "quantity") ?? IntValue(item, "count");
            result.Add(new(StringValue(item, "typeId") ?? StringValue(item, "uniqueName"), quantity, item.TryGetProperty("quantity", out _) || item.TryGetProperty("count", out _) ? InventoryFieldState.Known : InventoryFieldState.NotObserved, item.GetRawText()));
        }
        coverage["stackables"] = InventoryFieldState.Known;
        return result;
    }

    private static IReadOnlyList<InventoryUnknownRecord> ReadUnknown(JsonElement root, int knownCount, int max)
    {
        var result = new List<InventoryUnknownRecord>();
        foreach (var property in root.EnumerateObject())
            if (!property.Name.Equals("equipment", StringComparison.OrdinalIgnoreCase) &&
                !property.Name.Equals("stackables", StringComparison.OrdinalIgnoreCase) &&
                !property.Name.Equals("mods", StringComparison.OrdinalIgnoreCase) &&
                !property.Name.Equals("upgrades", StringComparison.OrdinalIgnoreCase) &&
                !property.Name.Equals("rawupgrades", StringComparison.OrdinalIgnoreCase))
                result.Add(new(property.Name, property.Value.GetRawText(), "FIELD_NOT_MAPPED"));
        return result;
    }

    private static IReadOnlyList<InventoryUpgradeRecord> ReadUpgrades(
        JsonElement root, Dictionary<string, InventoryFieldState> coverage, int max)
    {
        var result = new List<InventoryUpgradeRecord>();
        var observed = false;
        foreach (var property in root.EnumerateObject().Where(property =>
            property.Name.Equals("mods", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Equals("upgrades", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Equals("rawupgrades", StringComparison.OrdinalIgnoreCase)))
        {
            observed = true;
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                coverage[$"upgrades.{property.Name}"] = InventoryFieldState.Invalid;
                continue;
            }

            coverage[$"upgrades.{property.Name}"] = InventoryFieldState.Known;
            foreach (var item in property.Value.EnumerateArray())
            {
                if (result.Count >= max) throw new InvalidDataException("INVENTORY_RECORDS_TOO_LARGE");
                if (item.ValueKind != JsonValueKind.Object)
                {
                    result.Add(new(null, property.Name, null, null, item.GetRawText()));
                    continue;
                }

                result.Add(new(
                    StringValue(item, "ownerInstanceId") ?? StringValue(item, "instanceId"),
                    property.Name,
                    StringValue(item, "upgradeId") ?? StringValue(item, "uniqueName") ??
                        StringValue(item, "typeId") ?? StringValue(item, "id") ?? StringValue(item, "name"),
                    IntValue(item, "rank") ?? IntValue(item, "level"),
                    item.GetRawText()));
            }
        }

        if (!observed) coverage["upgrades"] = InventoryFieldState.NotObserved;
        else if (result.Count > 0 && !coverage.Keys.Any(key => key.StartsWith("upgrades.", StringComparison.Ordinal)))
            coverage["upgrades"] = InventoryFieldState.Known;
        return result;
    }

    private static string? StringValue(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? IntValue(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static string? JsonValue(JsonElement item, string property) => item.TryGetProperty(property, out var value) ? value.GetRawText() : null;
    private static InventoryFieldState InventoryState(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? InventoryFieldState.Known : InventoryFieldState.NotObserved;
}
