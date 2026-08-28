using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class AlecaFrameReaderTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("LEO-ALEC\tEO-ALEC");
    private static readonly byte[] Iv = [49, 50, 70, 71, 66, 51, 54, 45, 76, 69, 51, 45, 113, 61, 57, 0];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsEncryptedCurrentAndLegacySnapshots(bool legacy)
    {
        using var directory = new TemporaryDirectory();
        const string inventory = """
            {"Recipes":[{"ItemType":"/Lotus/Prime/Part","ItemCount":3}],
             "Suits":[{"ItemType":"/Lotus/Warframe/Test"}],
             "XPInfo":[{"ItemType":"/Lotus/Warframe/Test","XP":900000}],
             "PlayerLevel":24,"TradesRemaining":12}
            """;
        var payload = legacy ? JsonSerializer.Serialize(new { InventoryJson = inventory }) : inventory;
        await File.WriteAllBytesAsync(System.IO.Path.Combine(directory.Path, "lastData.dat"), Encrypt(payload));

        var result = await new AlecaFrameReader().ReadAsync(directory.Path);

        Assert.Equal(3, result.Stackables["/Lotus/Prime/Part"]);
        Assert.Contains("/Lotus/Warframe/Test", result.OwnedEquipment);
        Assert.Equal(900_000, result.Experience["/Lotus/Warframe/Test"]);
        Assert.Equal(24, result.PlayerLevel);
        Assert.Equal(12, result.TradesRemaining);
    }

    [Fact]
    public async Task RejectsTruncatedEncryptedSnapshot()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllBytesAsync(System.IO.Path.Combine(directory.Path, "lastData.dat"), [1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new AlecaFrameReader().ReadAsync(directory.Path));
    }

    private static byte[] Encrypt(string value)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = Key;
        aes.IV = Iv;
        var bytes = Encoding.UTF8.GetBytes(value);
        return aes.CreateEncryptor().TransformFinalBlock(bytes, 0, bytes.Length);
    }
}
