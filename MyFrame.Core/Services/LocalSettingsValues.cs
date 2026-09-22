namespace MyFrame.Core;

public interface ISettingsStore
{
    bool ContainsKey(string key);
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
    void Remove(string key);
}

/// <summary>
/// Preference-backed dashboard values with platform-independent clamping and migration rules.
/// The app supplies the platform store; tests can use an in-memory implementation.
/// </summary>
public sealed class LocalSettingsValues
{
    private const string DucatsPerPlatinumKey = "DucatsPerPlatinum";
    private const string UnvaultedPrimeSetsToReserveKey = "UnvaultedPrimeSetsToReserve";
    private const string LegacyReserveKey = "ReserveUnvaultedPrimeWarframeSet";
    private readonly ISettingsStore _store;

    public LocalSettingsValues(ISettingsStore store) => _store = store;

    public int DucatsPerPlatinum
    {
        get => Math.Clamp(_store.Get(DucatsPerPlatinumKey, 10), 1, 50);
        set => _store.Set(DucatsPerPlatinumKey, Math.Clamp(value, 1, 50));
    }

    public int UnvaultedPrimeSetsToReserve
    {
        get
        {
            if (_store.ContainsKey(UnvaultedPrimeSetsToReserveKey))
                return Math.Clamp(_store.Get(UnvaultedPrimeSetsToReserveKey, 1), 0, 10);
            var legacy = _store.Get(LegacyReserveKey, true);
            var migrated = legacy ? 1 : 0;
            _store.Set(UnvaultedPrimeSetsToReserveKey, migrated);
            return migrated;
        }
        set => _store.Set(UnvaultedPrimeSetsToReserveKey, Math.Clamp(value, 0, 10));
    }
}
