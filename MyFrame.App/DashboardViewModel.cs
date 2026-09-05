using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MyFrame.Core;
using Microsoft.Extensions.Logging;

namespace MyFrame.App;

public partial class DashboardViewModel : ObservableObject
{
    private readonly DashboardService _service;
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly IAlecaFramePath _alecaPath;
    private readonly AlecaFrameDirectorySettings _directorySettings;
    private bool _initialized;
    private IReadOnlyList<CollectionGoal> _allCollection = [];

    public DashboardViewModel(DashboardService service, ILogger<DashboardViewModel> logger,
        IAlecaFramePath alecaPath, AlecaFrameDirectorySettings directorySettings)
    {
        _service = service;
        _logger = logger;
        _alecaPath = alecaPath;
        _directorySettings = directorySettings;
        AlecaFrameDirectory = alecaPath.DirectoryPath;
        _service.SnapshotUpdated += (_, snapshot) => MainThread.BeginInvokeOnMainThread(() => Apply(snapshot));
        ShowSection("Dashboard");
    }

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "Waiting for AlecaFrame data…";
    [ObservableProperty] public partial string LastSyncText { get; set; } = "—";
    [ObservableProperty] public partial string AccountText { get; set; } = "Warframe.Market not connected";
    [ObservableProperty] public partial string TotalPlatinum { get; set; } = "0p";
    [ObservableProperty] public partial string TotalDucats { get; set; } = "0 ducats";
    [ObservableProperty] public partial string MasteryProgress { get; set; } = "0%";
    [ObservableProperty] public partial string InventorySummary { get; set; } = "0 items";
    [ObservableProperty] public partial double DucatsPerPlatinum { get; set; } = 10;
    [ObservableProperty] public partial bool DashboardVisible { get; set; }
    [ObservableProperty] public partial bool CollectionVisible { get; set; }
    [ObservableProperty] public partial bool FarmVisible { get; set; }
    [ObservableProperty] public partial bool SalesVisible { get; set; }
    [ObservableProperty] public partial bool RelicsVisible { get; set; }
    [ObservableProperty] public partial bool SettingsVisible { get; set; }
    [ObservableProperty] public partial string SelectedCollectionFilter { get; set; } = "In progress";
    [ObservableProperty] public partial string SelectedCollectionSort { get; set; } = "Closest to completion";
    [ObservableProperty] public partial string AlecaFrameDirectory { get; set; } = "";
    [ObservableProperty] public partial string AlecaFrameDirectoryMessage { get; set; } = "Using the detected AlecaFrame folder.";
    [ObservableProperty] public partial string SelectedSalesFilter { get; set; } = "All recommendations";

    public ObservableCollection<CollectionGoal> Collection { get; } = [];
    public ObservableCollection<FarmRecommendation> Farm { get; } = [];
    public ObservableCollection<SaleRecommendation> Sales { get; } = [];
    public ObservableCollection<RelicRecommendation> Relics { get; } = [];
    public IReadOnlyList<string> CollectionFilters { get; } = ["In progress", "All", "Not owned", "Owned", "Mastered", "Prime only"];
    public IReadOnlyList<string> CollectionSorts { get; } = ["Closest to completion", "Name", "Category", "Least progress"];
    public IReadOnlyList<string> SalesFilters { get; } = ["All recommendations", "Platinum", "Ducats", "Existing orders", "Vaulted items"];
    public ISeries[] ValueSeries { get; private set; } = [];
    public ISeries[] ProgressSeries { get; private set; } = [];

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        _logger.LogInformation("Dashboard view initialized");
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Reading inventory and updating prices…";
        try { Apply(await _service.RefreshAsync(true, new RecommendationSettings(Math.Max(.1, DucatsPerPlatinum)))); }
        catch (Exception error)
        {
            _logger.LogError(error, "Dashboard refresh failed");
            StatusMessage = $"Synchronization failed: {error.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SelectAlecaFrameDirectoryAsync()
    {
        var directory = await AlecaFrameFolderPicker.PickAsync();
        if (directory is null) return;
        var error = AlecaFrameDirectorySettings.ValidationError(directory);
        if (error is not null) { AlecaFrameDirectoryMessage = error; return; }
        Preferences.Default.Set(AlecaFrameDirectorySettings.PreferenceKey, directory);
        _alecaPath.SetDirectory(directory);
        AlecaFrameDirectory = _alecaPath.DirectoryPath;
        AlecaFrameDirectoryMessage = "Folder saved. Inventory, catalogs, token, and monitoring now use this location.";
    }

    [RelayCommand]
    private void ResetAlecaFrameDirectory()
    {
        Preferences.Default.Remove(AlecaFrameDirectorySettings.PreferenceKey);
        _alecaPath.SetDirectory(_directorySettings.AutomaticDirectory);
        AlecaFrameDirectory = _alecaPath.DirectoryPath;
        AlecaFrameDirectoryMessage = "Restored automatic detection (%LOCALAPPDATA%\\AlecaFrame).";
    }

    [RelayCommand]
    private void ShowSection(string section)
    {
        _logger.LogDebug("Navigating to dashboard section {Section}", section);
        DashboardVisible = section == "Dashboard";
        CollectionVisible = section == "Collection";
        FarmVisible = section == "Farm";
        SalesVisible = section == "Sales";
        RelicsVisible = section == "Relics";
        SettingsVisible = section == "Settings";
    }

    private void Apply(DashboardSnapshot snapshot)
    {
        StatusMessage = snapshot.Status.Message;
        LastSyncText = snapshot.Status.LastSuccessfulSync?.ToString("dd/MM/yyyy HH:mm:ss") ?? "—";
        AccountText = snapshot.Account is null ? "Token missing or expired" : $"{snapshot.Account.IngameName} · {snapshot.Account.Platform}";
        TotalPlatinum = $"{snapshot.Recommendations.EstimatedPlatinum:N0}p";
        TotalDucats = $"{snapshot.Recommendations.TotalDucats:N0} ducats";
        var mastered = snapshot.Recommendations.Collection.Count(x => x.Mastered);
        var total = snapshot.Recommendations.Collection.Count;
        MasteryProgress = total == 0 ? "0%" : $"{(double)mastered / total:P0}";
        InventorySummary = $"{snapshot.Inventory.Stackables.Count:N0} stacks · {snapshot.Inventory.OwnedEquipment.Count:N0} equipment";
        _allCollection = snapshot.Recommendations.Collection;
        ApplyCollectionView();
        Replace(Farm, snapshot.Recommendations.Farm.Take(100));
        ApplySalesView(snapshot.Recommendations.Sales);
        Replace(Relics, snapshot.Recommendations.Relics.Take(200));
        ValueSeries =
        [
            new PieSeries<double> { Name = "Platinum", Values = [snapshot.Recommendations.EstimatedPlatinum] },
            new PieSeries<double> { Name = "Ducats ÷ 10", Values = [snapshot.Recommendations.TotalDucats / 10d] }
        ];
        ProgressSeries =
        [
            new ColumnSeries<double> { Name = "Mastered", Values = [mastered] },
            new ColumnSeries<double> { Name = "Pending", Values = [Math.Max(0, total - mastered)] }
        ];
        OnPropertyChanged(nameof(ValueSeries));
        OnPropertyChanged(nameof(ProgressSeries));
    }

    [RelayCommand]
    private async Task OpenWikiAsync() =>
        await Launcher.Default.OpenAsync("https://wiki.warframe.com/");

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    partial void OnSelectedCollectionFilterChanged(string value) => ApplyCollectionView();
    partial void OnSelectedCollectionSortChanged(string value) => ApplyCollectionView();
    partial void OnSelectedSalesFilterChanged(string value)
    {
        if (_service.LastSnapshot is not null) ApplySalesView(_service.LastSnapshot.Recommendations.Sales);
    }

    private void ApplySalesView(IEnumerable<SaleRecommendation> sales)
    {
        sales = SelectedSalesFilter switch
        {
            "Platinum" => sales.Where(x => x.Action == RecommendationAction.SellForPlatinum),
            "Ducats" => sales.Where(x => x.Action == RecommendationAction.ExchangeForDucats),
            "Existing orders" => sales.Where(x => x.ExistingOrder),
            "Vaulted items" => sales.Where(x => x.Vaulted),
            _ => sales
        };
        Replace(Sales, sales.Take(200));
    }

    private void ApplyCollectionView()
    {
        IEnumerable<CollectionGoal> values = SelectedCollectionFilter switch
        {
            "In progress" => _allCollection.Where(x => !x.Owned && (x.OwnedComponents > 0 || x.Mastered)),
            "Not owned" => _allCollection.Where(x => !x.Owned),
            "Owned" => _allCollection.Where(x => x.Owned),
            "Mastered" => _allCollection.Where(x => x.Mastered),
            "Prime only" => _allCollection.Where(x => x.Prime),
            _ => _allCollection
        };
        values = SelectedCollectionSort switch
        {
            "Name" => values.OrderBy(x => x.ItemName),
            "Category" => values.OrderBy(x => x.Category).ThenBy(x => x.ItemName),
            "Least progress" => values.OrderBy(x => x.Completion).ThenBy(x => x.ItemName),
            _ => values.OrderByDescending(x => x.Completion).ThenBy(x => x.ItemName)
        };
        Replace(Collection, values);
    }
}
