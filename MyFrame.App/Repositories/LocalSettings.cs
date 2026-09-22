using MyFrame.Core;

namespace MyFrame.App;

public sealed class MauiAppPreferences : ISettingsStore
{
    public bool ContainsKey(string key) => Preferences.Default.ContainsKey(key);
    public T Get<T>(string key, T defaultValue) => Preferences.Default.Get(key, defaultValue);
    public void Set<T>(string key, T value) => Preferences.Default.Set(key, value);
    public void Remove(string key) => Preferences.Default.Remove(key);
}
