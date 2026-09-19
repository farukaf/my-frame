using MyFrame.Core;
using MyFrame.Core.Sync;

namespace MyFrame.App;

public sealed class CollectorCaptureInboxService
{
    public string DirectoryPath => MyFrameStoragePaths.CollectorCaptureDirectory;

    public async Task<CollectorCaptureInboxResult> ImportAsync(
        bool allowRawPayload,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DirectoryPath);
        await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
        return await CollectorCaptureInbox.ImportAsync(DirectoryPath, database, allowRawPayload, cancellationToken);
    }
}
