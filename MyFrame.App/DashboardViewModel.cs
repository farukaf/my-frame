using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MyFrame.Core;

namespace MyFrame.App;

public partial class DashboardViewModel : ObservableObject
{
    private readonly DashboardService _service;
    private bool _initialized;

    public DashboardViewModel(DashboardService service)
    {
        _service = service;
        _service.SnapshotUpdated += (_, snapshot) => MainThread.BeginInvokeOnMainThread(() => Apply(snapshot));
        ShowSection("Dashboard");
    }

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Aguardando leitura do AlecaFrame…";
    [ObservableProperty] private string lastSyncText = "—";
    [ObservableProperty] private string accountText = "Warframe.Market não conectado";
    [ObservableProperty] private string totalPlatinum = "0p";
    [ObservableProperty] private string totalDucats = "0 ducats";
    [ObservableProperty] private string masteryProgress = "0%";
    [ObservableProperty] private string inventorySummary = "0 itens";
    [ObservableProperty] private double ducatsPerPlatinum = 10;
    [ObservableProperty] private bool dashboardVisible;
    [ObservableProperty] private bool collectionVisible;
    [ObservableProperty] private bool farmVisible;
    [ObservableProperty] private bool salesVisible;
    [ObservableProperty] private bool settingsVisible;

    public ObservableCollection<CollectionGoal> Collection { get; } = [];
    public ObservableCollection<FarmRecommendation> Farm { get; } = [];
    public ObservableCollection<SaleRecommendation> Sales { get; } = [];
    public ISeries[] ValueSeries { get; private set; } = [];
    public ISeries[] ProgressSeries { get; private set; } = [];

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Lendo snapshot e atualizando cotações…";
        try { Apply(await _service.RefreshAsync(true, new RecommendationSettings(Math.Max(.1, DucatsPerPlatinum)))); }
        catch (Exception error) { StatusMessage = $"Falha na sincronização: {error.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ShowSection(string section)
    {
        DashboardVisible = section == "Dashboard";
        CollectionVisible = section == "Collection";
        FarmVisible = section == "Farm";
        SalesVisible = section == "Sales";
        SettingsVisible = section == "Settings";
    }

    private void Apply(DashboardSnapshot snapshot)
    {
        StatusMessage = snapshot.Status.Message;
        LastSyncText = snapshot.Status.LastSuccessfulSync?.ToString("dd/MM/yyyy HH:mm:ss") ?? "—";
        AccountText = snapshot.Account is null ? "Token ausente/expirado" : $"{snapshot.Account.IngameName} · {snapshot.Account.Platform}";
        TotalPlatinum = $"{snapshot.Recommendations.EstimatedPlatinum:N0}p";
        TotalDucats = $"{snapshot.Recommendations.TotalDucats:N0} ducats";
        var mastered = snapshot.Recommendations.Collection.Count(x => x.Mastered);
        var total = snapshot.Recommendations.Collection.Count;
        MasteryProgress = total == 0 ? "0%" : $"{(double)mastered / total:P0}";
        InventorySummary = $"{snapshot.Inventory.Stackables.Count:N0} pilhas · {snapshot.Inventory.OwnedEquipment.Count:N0} equipamentos";
        Replace(Collection, snapshot.Recommendations.Collection);
        Replace(Farm, snapshot.Recommendations.Farm.Take(100));
        Replace(Sales, snapshot.Recommendations.Sales.Take(200));
        ValueSeries =
        [
            new PieSeries<double> { Name = "Platinum", Values = [snapshot.Recommendations.EstimatedPlatinum] },
            new PieSeries<double> { Name = "Ducats ÷ 10", Values = [snapshot.Recommendations.TotalDucats / 10d] }
        ];
        ProgressSeries =
        [
            new ColumnSeries<double> { Name = "Dominados", Values = [mastered] },
            new ColumnSeries<double> { Name = "Pendentes", Values = [Math.Max(0, total - mastered)] }
        ];
        OnPropertyChanged(nameof(ValueSeries));
        OnPropertyChanged(nameof(ProgressSeries));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }
}
