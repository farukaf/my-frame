using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace MyFrame.App;

public partial class SyncStatusViewModel(SyncStatusReader reader, CollectorCaptureInboxService collectorCaptureInbox, ILogger logger) : ObservableObject
{
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial bool IsLoadingSyncStatus { get; set; }
    [ObservableProperty] public partial string SyncStatusMessage { get; set; } = "Status not loaded.";
    [ObservableProperty] public partial bool AllowCollectorRawPayload { get; set; }
    [ObservableProperty] public partial bool IsImportingCollectorCaptures { get; set; }
    [ObservableProperty] public partial string CollectorCaptureMessage { get; set; } = "No capture import has been requested.";
    public string CollectorCaptureDirectory => collectorCaptureInbox.DirectoryPath;
    public ObservableCollection<SyncSourceStatusRow> SyncSources { get; } = [];

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
            CollectorCaptureMessage = "Marque o consentimento para importar o payload privado da captura.";
            return;
        }
        IsImportingCollectorCaptures = true;
        CollectorCaptureMessage = "Importando capturas validadas…";
        try
        {
            var result = await collectorCaptureInbox.ImportAsync(true);
            CollectorCaptureMessage = $"Encontradas {result.Discovered:N0}; novas {result.Imported:N0}; já publicadas {result.AlreadyPublished:N0}; rejeitadas {result.Rejected:N0}.";
            await RefreshSyncStatusAsync();
        }
        catch (Exception error)
        {
            logger.LogError(error, "Collector capture inbox import failed");
            CollectorCaptureMessage = "Importação rejeitada; nenhum payload foi publicado.";
        }
        finally
        {
            AllowCollectorRawPayload = false;
            IsImportingCollectorCaptures = false;
        }
    }
}
