using MyFrame.Core;

namespace MyFrame.App;

public sealed class LocalSettings
{
    private readonly IMyFrameSettingsWriter _store;
    private readonly object _gate = new();
    private MyFrameSettingsDocument _document;

    public LocalSettings(IMyFrameSettingsWriter store)
    {
        _store = store;
        _document = store.LoadAsync().GetAwaiter().GetResult() ?? new MyFrameSettingsDocument(
            MyFrameSettingsDocument.CurrentStorageVersion, 1,
            string.Empty, 10, 1, DateTimeOffset.UtcNow);
    }

    public string AlecaFrameDirectory
    {
        get { lock (_gate) return _document.AlecaFrameDirectory; }
        set => Update(document => document with { AlecaFrameDirectory = value });
    }

    public int DucatsPerPlatinum
    {
        get { lock (_gate) return Math.Clamp(_document.DucatsPerPlatinum, 1, 50); }
        set => Update(document => document with { DucatsPerPlatinum = Math.Clamp(value, 1, 50) });
    }

    public int UnvaultedPrimeSetsToReserve
    {
        get { lock (_gate) return Math.Clamp(_document.UnvaultedPrimeSetsToReserve, 0, 10); }
        set => Update(document => document with { UnvaultedPrimeSetsToReserve = Math.Clamp(value, 0, 10) });
    }

    private void Update(Func<MyFrameSettingsDocument, MyFrameSettingsDocument> update)
    {
        lock (_gate)
        {
            var next = update(_document) with
            {
                StorageVersion = MyFrameSettingsDocument.CurrentStorageVersion,
                Revision = _document.Revision + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            // Keep revision order and disk publication in the same critical section. Otherwise
            // two slider/folder updates could finish their asynchronous file moves out of order.
            _store.SaveAsync(next).GetAwaiter().GetResult();
            _document = next;
        }
    }
}
