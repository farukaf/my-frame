using CommunityToolkit.Mvvm.ComponentModel;
using MyFrame.Core;

namespace MyFrame.App;

public partial class GlobalStatusViewModel : ObservableObject
{
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "Waiting for AlecaFrame data…";
    [ObservableProperty] public partial string LastSyncText { get; set; } = "—";
    [ObservableProperty] public partial string AccountText { get; set; } = "Warframe.Market not connected";
    [ObservableProperty] public partial string AlecaDataUpdatedText { get; set; } = "—";
    [ObservableProperty] public partial bool IsSyncingPrices { get; set; }
    [ObservableProperty] public partial double SyncProgress { get; set; }
    [ObservableProperty] public partial string SyncProgressText { get; set; } = "";
    [ObservableProperty] public partial bool PricesStale { get; set; }
    [ObservableProperty] public partial string StaleWarningText { get; set; } = "";
    [ObservableProperty] public partial string FilteredDucatsEstimate { get; set; } = "0";
    [ObservableProperty] public partial string ActivePrimeSetReserveText { get; set; } = "—";
    [ObservableProperty] public partial bool ActivePrimeSetReserveEnabled { get; set; }

    public void Apply(DashboardSnapshot snapshot)
    {
        ApplySyncStatus(snapshot.Status);
        LastSyncText = snapshot.Status.LastSuccessfulSync?.ToString("dd/MM/yyyy HH:mm:ss") ?? "—";
        AlecaDataUpdatedText = snapshot.Inventory.CapturedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        AccountText = snapshot.Account is null ? "Token missing or expired" : $"{snapshot.Account.IngameName} · {snapshot.Account.Platform}";
        var settings = snapshot.Recommendations.Settings;
        ActivePrimeSetReserveEnabled = settings.UnvaultedPrimeSetsToReserve > 0;
        ActivePrimeSetReserveText = ActivePrimeSetReserveEnabled ? $"{settings.UnvaultedPrimeSetsToReserve} SET{(settings.UnvaultedPrimeSetsToReserve == 1 ? "" : "S")}" : "OFF";
    }
    public void ApplySyncStatus(SyncStatus status)
    {
        StatusMessage = status.Message;
        IsSyncingPrices = status.IsLoading;
        SyncProgress = status.PriceProgress;
        SyncProgressText = status.PriceProgressText;
        StaleWarningText = status.Error ?? status.StaleWarning;
        PricesStale = !status.IsLoading && StaleWarningText.Length > 0;
    }
}
