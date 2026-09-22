using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SyncDiagnosticAttempt = MyFrame.Core.Sync.SyncDiagnosticAttempt;
using SyncDiagnosticSource = MyFrame.Core.Sync.SyncDiagnosticSource;
using SyncDiagnosticsSerializer = MyFrame.Core.Sync.SyncDiagnosticsSerializer;

namespace MyFrame.App;

public partial class SyncStatusViewModel(SyncStatusReader reader, CollectorCaptureInboxService collectorCaptureInbox, CollectorCaptureInboxWatcher watcher, WorldStateSyncService worldStateSync, PublicExportSyncService publicExportSync, ILogger logger) : ObservableObject
{
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial bool IsLoadingSyncStatus { get; set; }
    [ObservableProperty] public partial string SyncStatusMessage { get; set; } = "Status not loaded.";
    [ObservableProperty] public partial bool AllowCollectorRawPayload { get; set; }
    [ObservableProperty] public partial bool IsImportingCollectorCaptures { get; set; }
    [ObservableProperty] public partial string CollectorCaptureMessage { get; set; } = "No capture import has been requested.";
    [ObservableProperty] public partial string CollectorCaptureStatusText { get; set; } = "Collector status not loaded.";
    [ObservableProperty] public partial string CollectorCaptureNotice { get; set; } = "";
    [ObservableProperty] public partial bool CollectorCaptureNoticeVisible { get; set; }
    [ObservableProperty] public partial bool IsSyncingWorldState { get; set; }
    [ObservableProperty] public partial string WorldStateSyncMessage { get; set; } = "No World State synchronization requested.";
    [ObservableProperty] public partial bool IsSyncingPublicExport { get; set; }
    [ObservableProperty] public partial string PublicExportSyncMessage { get; set; } = "No Public Export synchronization requested.";
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
    private async Task CopySyncDiagnosticsAsync()
    {
        if (IsLoadingSyncStatus) return;
        try
        {
            var rows = await reader.ReadAsync();
            var attempts = await reader.ReadRecentRunsAsync();
            var sources = rows.Select(row => new SyncDiagnosticSource(row.SourceId, row.State, row.Detail,
                row.Revision, row.LastRun, row.ParserVersion, row.Coverage)).ToArray();
            var diagnosticAttempts = attempts.Select(attempt => new SyncDiagnosticAttempt(attempt.SourceId,
                attempt.State, attempt.StartedAt, attempt.Detail)).ToArray();
            await Clipboard.Default.SetTextAsync(SyncDiagnosticsSerializer.Serialize(sources, diagnosticAttempts,
                DateTimeOffset.UtcNow));
            SyncStatusMessage = "Sanitized diagnostics copied. Payloads, tokens, and local paths were omitted.";
        }
        catch (Exception error)
        {
            logger.LogError(error, "Sync diagnostics export failed");
            SyncStatusMessage = "Unable to copy sanitized diagnostics.";
        }
    }

    [RelayCommand]
    public async Task RefreshCollectorCaptureStatusAsync()
    {
        try
        {
            var status = await collectorCaptureInbox.ReadStatusAsync();
            var eventSummary = status.EventCounts is { Count: > 0 }
                ? string.Join(", ", status.EventCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value:N0}"))
                : "none";
            var featureSummary = status.SupportedFeatures is { Count: > 0 }
                ? string.Join(", ", status.SupportedFeatures)
                : "none";
            CollectorCaptureStatusText = $"Collector: {status.State}; Overwolf {status.OverwolfRunning}; Warframe {status.WarframeRunning}; " +
                $"heartbeat {status.HeartbeatState ?? "missing"} (fresh {status.HeartbeatFresh}); markers {status.ReadyMarkers:N0} " +
                $"({status.ValidMarkers:N0} valid, {status.InvalidMarkers:N0} invalid); collector {status.CollectorState ?? "unknown"}; " +
                $"features {featureSummary}; events {eventSummary}; last {status.LastEventFeature ?? "none"} @ {status.LastEventAt?.ToLocalTime():HH:mm:ss}.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning(error, "Collector capture status read failed");
            CollectorCaptureStatusText = "Collector status unavailable.";
        }
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
            await RefreshCollectorCaptureStatusAsync();
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
            await RefreshCollectorCaptureStatusAsync();
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
                ? $"World State synchronized: {result.Records:N0} bounties; revision {result.RevisionId}; parser {result.ParserVersion ?? "unknown"}."
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

    [RelayCommand]
    private async Task SyncPublicExportAsync()
    {
        if (IsSyncingPublicExport) return;
        IsSyncingPublicExport = true;
        PublicExportSyncMessage = "Fetching official Warframe Public Export…";
        try
        {
            var result = await publicExportSync.RunAsync();
            PublicExportSyncMessage = result.State == "published"
                ? $"Public Export synchronized: {result.Records:N0} records; revision {result.RevisionId}; parser {result.ParserVersion ?? "unknown"}."
                : $"Public Export synchronization failed: {result.ErrorCode ?? result.State}. Previous catalog preserved.";
            await RefreshSyncStatusAsync();
        }
        catch (Exception error)
        {
            logger.LogError(error, "Public Export synchronization failed");
            PublicExportSyncMessage = "Public Export synchronization failed; previous catalog was preserved.";
        }
        finally { IsSyncingPublicExport = false; }
    }
}
