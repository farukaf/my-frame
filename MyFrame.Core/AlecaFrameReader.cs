using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MyFrame.Core;

public sealed class AlecaFrameReader : IAlecaFrameReader
{
    private readonly ILogger<AlecaFrameReader> _logger;
    private const string LastDataFile = "lastData.dat";
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("LEO-ALEC\tEO-ALEC");
    private static readonly byte[] Iv = [49, 50, 70, 71, 66, 51, 54, 45, 76, 69, 51, 45, 113, 61, 57, 0];

    private static readonly string[] StackableCollections =
    [
        "Recipes", "MiscItems", "RawUpgrades", "Consumables", "SpecialItems",
        "FlavourItems", "FusionTreasures", "CrewShipRawSalvage", "CrewShipAmmo"
    ];

    private static readonly string[] EquipmentCollections =
    [
        "Suits", "LongGuns", "Pistols", "Melee", "Sentinels", "SentinelWeapons",
        "SpaceSuits", "SpaceMelee", "SpaceGuns", "KubrowPets", "OperatorAmps",
        "MechSuits", "Ships", "Scoops", "DrifterMelee", "OperatorSuits"
    ];

    public AlecaFrameReader(ILogger<AlecaFrameReader>? logger = null) =>
        _logger = logger ?? NullLogger<AlecaFrameReader>.Instance;

    public async Task<InventorySnapshot> ReadAsync(
        string alecaDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alecaDirectory);
        var path = Path.Combine(alecaDirectory, LastDataFile);
        _logger.LogInformation("Reading AlecaFrame inventory snapshot");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("O lastData.dat do AlecaFrame não foi encontrado.", path);
        }

        Exception? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await ReadOnceAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or CryptographicException or JsonException)
            {
                _logger.LogWarning(error, "Transient snapshot read failure on attempt {Attempt}", attempt + 1);
                lastError = error;
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        _logger.LogError(lastError, "Inventory snapshot could not be read after retries");
        throw new InvalidDataException("O AlecaFrame ainda estava escrevendo o snapshot.", lastError);
    }

    private async Task<InventorySnapshot> ReadOnceAsync(string path, CancellationToken cancellationToken)
    {
        byte[] encrypted;
        await using (var stream = new FileStream(
                         path,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite | FileShare.Delete,
                         64 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (stream.Length <= 0 || stream.Length > 100 * 1024 * 1024)
            {
                throw new InvalidDataException("Tamanho inválido para lastData.dat.");
            }

            encrypted = new byte[stream.Length];
            await stream.ReadExactlyAsync(encrypted, cancellationToken).ConfigureAwait(false);
        }

        byte[] plain;
        if (encrypted[0] is (byte)'{' or (byte)'[')
        {
            plain = encrypted;
        }
        else
        {
            if (encrypted.Length % 16 != 0)
            {
                throw new CryptographicException("Snapshot AES incompleto.");
            }

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Key;
            aes.IV = Iv;
            plain = aes.CreateDecryptor().TransformFinalBlock(encrypted, 0, encrypted.Length);
            CryptographicOperations.ZeroMemory(encrypted);
        }

        try
        {
            using var outer = JsonDocument.Parse(plain);
            JsonDocument? nested = null;
            var root = outer.RootElement;
            if (root.TryGetProperty("InventoryJson", out var inventoryJson) &&
                inventoryJson.ValueKind == JsonValueKind.String)
            {
                nested = JsonDocument.Parse(inventoryJson.GetString()!);
                root = nested.RootElement;
            }

            try
            {
                var snapshot = BuildSnapshot(root, path);
                _logger.LogInformation(
                    "Inventory snapshot loaded with {StackCount} stack types and {EquipmentCount} equipment entries",
                    snapshot.Stackables.Count, snapshot.OwnedEquipment.Count);
                return snapshot;
            }
            finally
            {
                nested?.Dispose();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static InventorySnapshot BuildSnapshot(JsonElement root, string path)
    {
        var stackables = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var collectionName in StackableCollections)
        {
            if (!root.TryGetProperty(collectionName, out var collection) ||
                collection.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in collection.EnumerateArray())
            {
                var itemType = GetString(entry, "ItemType");
                if (string.IsNullOrWhiteSpace(itemType)) continue;
                var count = Math.Max(0, GetInt(entry, "ItemCount", 1));
                stackables[itemType] = stackables.GetValueOrDefault(itemType) + count;
            }
        }

        var ownedEquipment = new HashSet<string>(StringComparer.Ordinal);
        foreach (var collectionName in EquipmentCollections)
        {
            if (!root.TryGetProperty(collectionName, out var collection) ||
                collection.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in collection.EnumerateArray())
            {
                var itemType = GetString(entry, "ItemType");
                if (!string.IsNullOrWhiteSpace(itemType)) ownedEquipment.Add(itemType);
            }
        }

        var experience = new Dictionary<string, long>(StringComparer.Ordinal);
        if (root.TryGetProperty("XPInfo", out var xpInfo) && xpInfo.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in xpInfo.EnumerateArray())
            {
                var itemType = GetString(entry, "ItemType");
                if (string.IsNullOrWhiteSpace(itemType)) continue;
                var xp = GetLong(entry, "XP");
                experience[itemType] = Math.Max(experience.GetValueOrDefault(itemType), xp);
            }
        }

        return new InventorySnapshot(
            File.GetLastWriteTimeUtc(path),
            stackables,
            ownedEquipment,
            experience,
            GetInt(root, "PlayerLevel"),
            GetInt(root, "TradesRemaining"),
            path);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name, int defaultValue = 0) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : defaultValue;

    private static long GetLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0;
}
