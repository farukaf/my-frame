using MyFrame.Core;

namespace MyFrame.App;

public sealed class MauiAppPreferences : ISettingsStore
{
    public bool ContainsKey(string key) => Preferences.Default.ContainsKey(key);
    public T Get<T>(string key, T defaultValue) => Preferences.Default.Get(key, defaultValue);
    public void Set<T>(string key, T value) => Preferences.Default.Set(key, value);
    public void Remove(string key) => Preferences.Default.Remove(key);
}

public sealed class LocalSettings
{
    private readonly LocalSettingsValues _values;

    public LocalSettings(ISettingsStore preferences) => _values = new LocalSettingsValues(preferences);

    public int DucatsPerPlatinum
    {
        get => _values.DucatsPerPlatinum;
        set => _values.DucatsPerPlatinum = value;
    }

    public int UnvaultedPrimeSetsToReserve
    {
        get
        {
            return _values.UnvaultedPrimeSetsToReserve;
        }
        set => _values.UnvaultedPrimeSetsToReserve = value;
    }
}
