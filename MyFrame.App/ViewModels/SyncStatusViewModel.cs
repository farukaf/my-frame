using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace MyFrame.App;

public partial class SyncStatusViewModel(SyncStatusReader reader, CollectorCaptureInboxService collectorCaptureInbox, CollectorCaptureInboxWatcher watcher, WorldStateSyncService worldStateSync, ILogger logger) : ObservableObject
{
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial bool IsLoadingSyncStatus { get; set; }
    [ObservableProperty] public partial string SyncStatusMessage { get; set; } = "Status not loaded.";
    [ObservableProperty] public partial bool AllowCollectorRawPayload { get; set; }
    [ObservableProperty] public partial bool IsImportingCollectorCaptures { get; set; }
    [ObservableProperty] public partial string CollectorCaptureMessage { get; set; } = "No capture import has been requested.";
    [ObservableProperty] public partial string CollectorCaptureNotice { get; set; } = "";
    [ObservableProperty] public partial bool CollectorCaptureNoticeVisible { get; set; }
    [ObservableProperty] public partial bool IsSyncingWorldState { get; set; }
    [ObservableProperty] public partial string WorldStateSyncMessage { get; set; } = "No World State synchronization requested.";
    public string CollectorCaptureDirectory => collectorCaptureInbox.DirectoryPath;
    public ObservableCollection<SyncSourceStatusRow> SyncSources { get; } = [];
    public ObservableCollection<SyncAttemptStatusRow> SyncAttempts { get; } = [];

    [RelayCommand]
    public async Task RefreshSyncStatusAsync()
    {
        if (IsLoadingSyncStatus) return;
        IsLoadingSyncStatus = true;
        SyncStatusMessage = "Reading the shared data store…";
        try
        {
            var rows = await reader.ReadAsync();
            SyncSources.Clear();
            foreach (var row in rows) SyncSources.Add(row);
            var attempts = await reader.ReadRecentRunsAsync();
            SyncAttempts.Clear();
            foreach (var attempt in attempts) SyncAttempts.Add(attempt);
            SyncStatusMessage = "Read-only view of the shared My Frame SQLite store.";
        }
        catch (Exception error)
        {
            logger.LogError(error, "Sync status read failed");
            SyncStatusMessage = "Unable to read synchronization status.";
        }
        finally { IsLoadingSyncStatus = false; }
    }

    [RelayCommand]
    private async Task ImportCollectorCapturesAsync()
    {
        if (IsImportingCollectorCaptures) return;
        if (!AllowCollectorRawPayload)
        {
            CollectorCaptureMessage = "Grant consent to import the private capture payload.";
            return;
        }
        IsImportingCollectorCaptures = true;
        CollectorCaptureMessage = "Importing validated captures…";
        try
        {
            var result = await collectorCaptureInbox.ImportAsync(true);
            CollectorCaptureNotice = "";
            CollectorCaptureNoticeVisible = false;
            CollectorCaptureMessage = $"Found {result.Discovered:N0}; new {result.Imported:N0}; already published {result.AlreadyPublished:N0}; rejected {result.Rejected:N0}.";
            await RefreshSyncStatusAsync();
        }
        catch (Exception error)
        {
            logger.LogError(error, "Collector capture inbox import failed");
            CollectorCaptureMessage = "Import rejected; no payload was published.";
        }
        finally
        {
            AllowCollectorRawPayload = false;
            IsImportingCollectorCaptures = false;
        }
    }

    public void StartWatcher() => watcher.Start();
    public void StopWatcher() => watcher.Dispose();
    public Task<bool> HasSynchronizedDataAsync(CancellationToken cancellationToken = default) => reader.HasSynchronizedDataAsync(cancellationToken);
    public void HandleCaptureDetected()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            CollectorCaptureNotice = "New capture detected. Review it and import with explicit consent.";
            CollectorCaptureNoticeVisible = true;
            await RefreshSyncStatusAsync();
        });
    }

    [RelayCommand]
    private async Task SyncWorldStateAsync()
    {
        if (IsSyncingWorldState) return;
        IsSyncingWorldState = true;
        WorldStateSyncMessage = "Fetching Warframe World State…";
        try
        {
            var result = await worldStateSync.RunAsync();
            WorldStateSyncMessage = result.State == "published"
                ? $"World State synchronized: {result.Records:N0} bounties; revision {result.RevisionId}."
                : $"World State synchronization failed: {result.ErrorCode ?? result.State}.";
            await RefreshSyncStatusAsync();
        }
        catch (Exception error)
        {
            logger.LogError(error, "World State synchronization failed");
            WorldStateSyncMessage = "World State synchronization failed; previous data was preserved.";
        }
        finally { IsSyncingWorldState = false; }
    }
}
