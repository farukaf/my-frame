namespace MyFrame.Core;

public interface IReadOnlyPriceCache
{
    Task<IReadOnlyDictionary<string, MarketQuote>> LoadAllAsync(
        CancellationToken cancellationToken = default);
}

public interface IMyFrameSettingsStore
{
    Task<MyFrameSettingsDocument?> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IMyFrameSettingsWriter : IMyFrameSettingsStore
{
    Task SaveAsync(MyFrameSettingsDocument settings, CancellationToken cancellationToken = default);
}
