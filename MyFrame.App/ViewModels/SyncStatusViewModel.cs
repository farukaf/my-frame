using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace MyFrame.App;

public partial class SyncStatusViewModel(SyncStatusReader reader, ILogger logger) : ObservableObject
{
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial bool IsLoadingSyncStatus { get; set; }
    [ObservableProperty] public partial string SyncStatusMessage { get; set; } = "Status not loaded.";
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
}
