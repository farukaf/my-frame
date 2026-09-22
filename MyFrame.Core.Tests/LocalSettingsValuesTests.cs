using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class LocalSettingsValuesTests
{
    [Fact]
    public void DefaultsAreReturnedWithoutWritingPreferences()
    {
        var store = new MemoryStore();
        var settings = new LocalSettingsValues(store);

        Assert.Equal(10, settings.DucatsPerPlatinum);
        Assert.Equal(1, settings.UnvaultedPrimeSetsToReserve);
        Assert.Equal(["UnvaultedPrimeSetsToReserve"], store.Writes);
    }

    [Fact]
    public void ValuesAreClampedBeforeTheyAreStored()
    {
        var store = new MemoryStore();
        var settings = new LocalSettingsValues(store);

        settings.DucatsPerPlatinum = 999;
        settings.UnvaultedPrimeSetsToReserve = -5;

        Assert.Equal(50, store.Get("DucatsPerPlatinum", 0));
        Assert.Equal(0, store.Get("UnvaultedPrimeSetsToReserve", 99));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void LegacyReserveSettingIsMigrated(bool legacyValue, int expected)
    {
        var store = new MemoryStore();
        store.Set("ReserveUnvaultedPrimeWarframeSet", legacyValue);
        var settings = new LocalSettingsValues(store);

        Assert.Equal(expected, settings.UnvaultedPrimeSetsToReserve);
        Assert.Equal(expected, store.Get("UnvaultedPrimeSetsToReserve", -1));
    }

    [Fact]
    public void ExistingReserveSettingTakesPrecedenceOverLegacyValue()
    {
        var store = new MemoryStore();
        store.Set("ReserveUnvaultedPrimeWarframeSet", false);
        store.Set("UnvaultedPrimeSetsToReserve", 8);
        store.Writes.Clear();
        var settings = new LocalSettingsValues(store);

        Assert.Equal(8, settings.UnvaultedPrimeSetsToReserve);
        Assert.Empty(store.Writes);
    }

    private sealed class MemoryStore : ISettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        public List<string> Writes { get; } = [];

        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public T Get<T>(string key, T defaultValue) => _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;
        public void Set<T>(string key, T value)
        {
            _values[key] = value!;
            Writes.Add(key);
        }
        public void Remove(string key) => _values.Remove(key);
    }
}
