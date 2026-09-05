namespace MyFrame.App;

public sealed class LocalSettings
{
    private const string DucatsPerPlatinumKey = "DucatsPerPlatinum";
    private const string UnvaultedPrimeSetsToReserveKey = "UnvaultedPrimeSetsToReserve";
    private const string LegacyReserveKey = "ReserveUnvaultedPrimeWarframeSet";

    public int DucatsPerPlatinum
    {
        get => Math.Clamp(Preferences.Default.Get(DucatsPerPlatinumKey, 10), 1, 50);
        set => Preferences.Default.Set(DucatsPerPlatinumKey, Math.Clamp(value, 1, 50));
    }

    public int UnvaultedPrimeSetsToReserve
    {
        get
        {
            if (Preferences.Default.ContainsKey(UnvaultedPrimeSetsToReserveKey))
                return Math.Clamp(Preferences.Default.Get(UnvaultedPrimeSetsToReserveKey, 1), 0, 10);
            var legacy = Preferences.Default.Get(LegacyReserveKey, true);
            var migrated = legacy ? 1 : 0;
            Preferences.Default.Set(UnvaultedPrimeSetsToReserveKey, migrated);
            return migrated;
        }
        set => Preferences.Default.Set(UnvaultedPrimeSetsToReserveKey, Math.Clamp(value, 0, 10));
    }
}
