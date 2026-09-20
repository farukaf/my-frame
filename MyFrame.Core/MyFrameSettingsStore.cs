using System.Text.Json;

namespace MyFrame.Core;

public sealed record MyFrameSettingsDocument(
    int StorageVersion,
    long Revision,
    string AlecaFrameDirectory,
    int DucatsPerPlatinum,
    int UnvaultedPrimeSetsToReserve,
    DateTimeOffset UpdatedAt)
{
    public const int CurrentStorageVersion = 1;

    public RecommendationSettings RecommendationSettings => new(
        Math.Clamp(DucatsPerPlatinum, 1, 50),
        Math.Clamp(UnvaultedPrimeSetsToReserve, 0, 10));
}

public static class MyFrameStoragePaths
{
    public static string RootDirectory => Environment.GetEnvironmentVariable("MYFRAME_DATA_ROOT") is { Length: > 0 } overridePath
        ? Path.GetFullPath(overridePath)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyFrame");

    public static string SettingsPath => Path.Combine(RootDirectory, "settings.v1.json");
    public static string PriceCachePath => Path.Combine(RootDirectory, "market-quotes.json");
    public static string MarketStatePath => Path.Combine(RootDirectory, "market-data.dat");
    public static string MarketItemIndexPath => Path.Combine(RootDirectory, "market-items.dat");
    public static string MarketTokenPath => Path.Combine(RootDirectory, "warframe-market.token");
    public static string DefaultAlecaFrameDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlecaFrame");
}

public sealed class JsonMyFrameSettingsStore(string path) : IMyFrameSettingsWriter
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<MyFrameSettingsDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return null;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var result = await JsonSerializer.DeserializeAsync<MyFrameSettingsDocument>(stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result is null) return null;
            if (result.StorageVersion != MyFrameSettingsDocument.CurrentStorageVersion)
                throw new NotSupportedException($"Unsupported My Frame storage version {result.StorageVersion}.");
            return result with
            {
                DucatsPerPlatinum = Math.Clamp(result.DucatsPerPlatinum, 1, 50),
                UnvaultedPrimeSetsToReserve = Math.Clamp(result.UnvaultedPrimeSetsToReserve, 0, 10)
            };
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(MyFrameSettingsDocument settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.StorageVersion != MyFrameSettingsDocument.CurrentStorageVersion)
            throw new ArgumentOutOfRangeException(nameof(settings), "Unsupported settings version.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("Settings path has no directory.");
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temporary, path, true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
            }
        }
        finally { _gate.Release(); }
    }
}
