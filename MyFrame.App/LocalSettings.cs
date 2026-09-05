namespace MyFrame.App;

public sealed class LocalSettings
{
    private const string DucatsPerPlatinumKey = "DucatsPerPlatinum";
    private const string ReserveUnvaultedPrimeWarframeSetKey = "ReserveUnvaultedPrimeWarframeSet";

    public double DucatsPerPlatinum
    {
        get => Math.Clamp(Preferences.Default.Get(DucatsPerPlatinumKey, 10d), 1d, 50d);
        set => Preferences.Default.Set(DucatsPerPlatinumKey, Math.Clamp(value, 1d, 50d));
    }

    public bool ReserveUnvaultedPrimeWarframeSet
    {
        get => Preferences.Default.Get(ReserveUnvaultedPrimeWarframeSetKey, true);
        set => Preferences.Default.Set(ReserveUnvaultedPrimeWarframeSetKey, value);
    }
}
