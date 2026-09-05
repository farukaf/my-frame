namespace MyFrame.App;

public sealed class LocalSettings
{
    private const string DucatsPerPlatinumKey = "DucatsPerPlatinum";
    private const string ReserveUnvaultedPrimeWarframeSetKey = "ReserveUnvaultedPrimeWarframeSet";

    public int DucatsPerPlatinum
    {
        get => Math.Clamp(Preferences.Default.Get(DucatsPerPlatinumKey, 10), 1, 50);
        set => Preferences.Default.Set(DucatsPerPlatinumKey, Math.Clamp(value, 1, 50));
    }

    public bool ReserveUnvaultedPrimeWarframeSet
    {
        get => Preferences.Default.Get(ReserveUnvaultedPrimeWarframeSetKey, true);
        set => Preferences.Default.Set(ReserveUnvaultedPrimeWarframeSetKey, value);
    }
}
