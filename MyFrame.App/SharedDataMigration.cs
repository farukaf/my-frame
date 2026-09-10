using MyFrame.Core;

namespace MyFrame.App;

public static class SharedDataMigration
{
    public static (JsonMyFrameSettingsStore Store, MyFrameSettingsDocument Settings) Ensure()
    {
        var store = new JsonMyFrameSettingsStore(MyFrameStoragePaths.SettingsPath);
        var existing = store.LoadAsync().GetAwaiter().GetResult();
        if (existing is not null)
        {
            MigrateCaches();
            return (store, existing);
        }

        var directory = Preferences.Default.Get(AlecaFrameDirectorySettings.PreferenceKey,
            MyFrameStoragePaths.DefaultAlecaFrameDirectory);
        var ducats = Math.Clamp(Preferences.Default.Get("DucatsPerPlatinum", 10), 1, 50);
        var reserve = Preferences.Default.ContainsKey("UnvaultedPrimeSetsToReserve")
            ? Math.Clamp(Preferences.Default.Get("UnvaultedPrimeSetsToReserve", 1), 0, 10)
            : Preferences.Default.Get("ReserveUnvaultedPrimeWarframeSet", true) ? 1 : 0;
        var document = new MyFrameSettingsDocument(MyFrameSettingsDocument.CurrentStorageVersion,
            1, directory, ducats, reserve, DateTimeOffset.UtcNow);
        store.SaveAsync(document).GetAwaiter().GetResult();
        MigrateCaches();
        return (store, document);
    }

    private static void MigrateCaches()
    {
        var legacyRoot = FileSystem.Current.AppDataDirectory;
        CopyIfNeeded(Path.Combine(legacyRoot, "market-quotes.json"), MyFrameStoragePaths.PriceCachePath);
        CopyIfNeeded(Path.Combine(legacyRoot, "market-data.dat"), MyFrameStoragePaths.MarketStatePath);
        CopyIfNeeded(Path.Combine(legacyRoot, "market-items.dat"), MyFrameStoragePaths.MarketItemIndexPath);
    }

    private static void CopyIfNeeded(string source, string destination)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination),
                StringComparison.OrdinalIgnoreCase) || !File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".migration." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary, false);
            File.Move(temporary, destination, false);
        }
        catch (IOException) { }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
        }
    }
}
