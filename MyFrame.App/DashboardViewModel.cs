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
    private readonly LocalSettings _localSettings;
    private bool _initialized;
    private IReadOnlyList<CollectionGoal> _allCollection = [];
    private IReadOnlyList<FarmRecommendation> _allFarm = [];
    private IReadOnlyList<SaleRecommendation> _allSales = [];
    private IReadOnlyList<RelicRecommendation> _allRelics = [];

    public DashboardViewModel(DashboardService service, ILogger<DashboardViewModel> logger,
        IAlecaFramePath alecaPath, AlecaFrameDirectorySettings directorySettings, LocalSettings localSettings)
    {
        _service = service;
        _logger = logger;
        _alecaPath = alecaPath;
        _directorySettings = directorySettings;
        _localSettings = localSettings;
        AlecaFrameDirectory = alecaPath.DirectoryPath;
        DucatsPerPlatinum = localSettings.DucatsPerPlatinum;
        UnvaultedPrimeSetsToReserve = localSettings.UnvaultedPrimeSetsToReserve;
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
    [ObservableProperty] public partial int UnvaultedPrimeSetsToReserve { get; set; } = 1;
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
    [ObservableProperty] public partial string SelectedSalesSort { get; set; } = "Name";
    [ObservableProperty] public partial string ActiveConversionValue { get; set; } = "—";
    [ObservableProperty] public partial string ActivePrimeSetReserveText { get; set; } = "—";
    [ObservableProperty] public partial bool ActivePrimeSetReserveEnabled { get; set; }
    [ObservableProperty] public partial string CollectionSearchText { get; set; } = "";
    [ObservableProperty] public partial string FarmSearchText { get; set; } = "";
    [ObservableProperty] public partial string SalesSearchText { get; set; } = "";
    [ObservableProperty] public partial string RelicsSearchText { get; set; } = "";
    [ObservableProperty] public partial string AlecaDataUpdatedText { get; set; } = "—";

    public ObservableCollection<CollectionGoal> Collection { get; } = [];
    public ObservableCollection<FarmRecommendation> Farm { get; } = [];
    public ObservableCollection<SaleRecommendation> Sales { get; } = [];
    public ObservableCollection<RelicRecommendation> Relics { get; } = [];
    public IReadOnlyList<string> CollectionFilters { get; } = ["In progress", "All", "Not owned", "Owned", "Mastered", "Prime only"];
    public IReadOnlyList<string> CollectionSorts { get; } = ["Closest to completion", "Name", "Category", "Least progress"];
    public IReadOnlyList<string> SalesFilters { get; } = ["All recommendations", "Keep", "Platinum", "Ducats", "Existing orders", "Vaulted items"];
    public IReadOnlyList<string> SalesSorts { get; } = ["Name", "Action", "Highest value"];
    public ISeries[] ValueSeries { get; private set; } = [];
    public ISeries[] ProgressSeries { get; private set; } = [];

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        _logger.LogInformation("Dashboard view initialized");
        var directoryError = AlecaFrameDirectorySettings.ValidationError(_alecaPath.DirectoryPath);
        if (directoryError is not null)
        {
            _logger.LogInformation("AlecaFrame folder is not configured; opening Settings");
            StatusMessage = "AlecaFrame data folder needs to be configured.";
            AlecaFrameDirectoryMessage = $"{directoryError} Choose the AlecaFrame data folder to continue.";
            ShowSection("Settings");
            return;
        }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Reading inventory and updating prices…";
        try { Apply(await _service.RefreshAsync(true, new RecommendationSettings(
            Math.Clamp((int)Math.Round(DucatsPerPlatinum), 1, 50), Math.Clamp(UnvaultedPrimeSetsToReserve, 0, 10)))); }
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
        StatusMessage = "AlecaFrame folder configured. Loading data…";
        await RefreshAsync();
    }

    [RelayCommand]
    private void ResetAlecaFrameDirectory()
    {
        Preferences.Default.Remove(AlecaFrameDirectorySettings.PreferenceKey);
        _alecaPath.SetDirectory(_directorySettings.AutomaticDirectory);
        AlecaFrameDirectory = _alecaPath.DirectoryPath;
        var error = AlecaFrameDirectorySettings.ValidationError(AlecaFrameDirectory);
        AlecaFrameDirectoryMessage = error is null
            ? "Restored automatic detection (%LOCALAPPDATA%\\AlecaFrame)."
            : $"Automatic location restored, but it is not ready: {error}";
        if (error is not null)
        {
            StatusMessage = "AlecaFrame data folder needs to be configured.";
            ShowSection("Settings");
        }
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
        AlecaDataUpdatedText = snapshot.Inventory.CapturedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        AccountText = snapshot.Account is null ? "Token missing or expired" : $"{snapshot.Account.IngameName} · {snapshot.Account.Platform}";
        TotalPlatinum = $"{snapshot.Recommendations.EstimatedPlatinum:N0}p";
        TotalDucats = $"{snapshot.Recommendations.TotalDucats:N0} ducats";
        var activeSettings = snapshot.Recommendations.Settings;
        ActiveConversionValue = activeSettings.DucatsPerPlatinum.ToString();
        ActivePrimeSetReserveEnabled = activeSettings.UnvaultedPrimeSetsToReserve > 0;
        ActivePrimeSetReserveText = ActivePrimeSetReserveEnabled
            ? $"{activeSettings.UnvaultedPrimeSetsToReserve} SET{(activeSettings.UnvaultedPrimeSetsToReserve == 1 ? "" : "S")}"
            : "OFF";
        var mastered = snapshot.Recommendations.Collection.Count(x => x.Mastered);
        var total = snapshot.Recommendations.Collection.Count;
        MasteryProgress = total == 0 ? "0%" : $"{(double)mastered / total:P0}";
        InventorySummary = $"{snapshot.Inventory.Stackables.Count:N0} stacks · {snapshot.Inventory.OwnedEquipment.Count:N0} equipment";
        _allCollection = snapshot.Recommendations.Collection;
        _allFarm = snapshot.Recommendations.Farm;
        _allSales = snapshot.Recommendations.Sales;
        _allRelics = snapshot.Recommendations.Relics;
        ApplyCollectionView();
        ApplyFarmView();
        ApplySalesView();
        ApplyRelicsView();
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
    partial void OnCollectionSearchTextChanged(string value) => ApplyCollectionView();
    partial void OnFarmSearchTextChanged(string value) => ApplyFarmView();
    partial void OnSalesSearchTextChanged(string value) => ApplySalesView();
    partial void OnRelicsSearchTextChanged(string value) => ApplyRelicsView();
    partial void OnDucatsPerPlatinumChanged(double value)
    {
        var integerValue = Math.Clamp((int)Math.Round(value), 1, 50);
        if (Math.Abs(value - integerValue) > double.Epsilon)
        {
            DucatsPerPlatinum = integerValue;
            return;
        }
        _localSettings.DucatsPerPlatinum = integerValue;
    }
    partial void OnUnvaultedPrimeSetsToReserveChanged(int value) =>
        _localSettings.UnvaultedPrimeSetsToReserve = value;
    partial void OnSelectedSalesFilterChanged(string value)
    {
        ApplySalesView();
    }
    partial void OnSelectedSalesSortChanged(string value) => ApplySalesView();

    private void ApplySalesView()
    {
        IEnumerable<SaleRecommendation> sales = SelectedSalesFilter switch
        {
            "Keep" => _allSales.Where(x => x.Action == RecommendationAction.Keep),
            "Platinum" => _allSales.Where(x => x.Action == RecommendationAction.SellForPlatinum),
            "Ducats" => _allSales.Where(x => x.Action == RecommendationAction.ExchangeForDucats),
            "Existing orders" => _allSales.Where(x => x.ExistingOrder),
            "Vaulted items" => _allSales.Where(x => x.Vaulted),
            _ => _allSales
        };
        if (!string.IsNullOrWhiteSpace(SalesSearchText))
            sales = sales.Where(x => Matches(SalesSearchText, x.ItemName, x.Reason, x.ActionLabel, x.VaultStatus));
        sales = SelectedSalesSort switch
        {
            "Action" => sales.OrderBy(x => x.ActionLabel).ThenBy(x => x.ItemName),
            "Highest value" => sales.OrderByDescending(x => x.TotalPlatinum).ThenBy(x => x.ItemName),
            _ => sales.OrderBy(x => x.ItemName)
        };
        Replace(Sales, sales.Take(200));
    }

    private void ApplyFarmView()
    {
        IEnumerable<FarmRecommendation> values = string.IsNullOrWhiteSpace(FarmSearchText) ? _allFarm : _allFarm.Where(x =>
            Matches(FarmSearchText, x.ItemName, x.Category, x.Reason, string.Join(' ', x.MissingComponentNames)));
        Replace(Farm, values.Take(100));
    }

    private void ApplyRelicsView()
    {
        IEnumerable<RelicRecommendation> values = string.IsNullOrWhiteSpace(RelicsSearchText) ? _allRelics : _allRelics.Where(x =>
            Matches(RelicsSearchText, x.RelicName, x.Reason, x.Action, x.VaultStatus));
        Replace(Relics, values.Take(200));
    }

    private void ApplyCollectionView()
    {
        IEnumerable<CollectionGoal> values = SelectedCollectionFilter switch
        {
            "In progress" => _allCollection.Where(x => !x.Owned && !x.Mastered && x.OwnedComponents > 0),
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
        if (!string.IsNullOrWhiteSpace(CollectionSearchText))
            values = values.Where(x => Matches(CollectionSearchText, x.ItemName, x.Category, x.Status, x.PrimeStatus));
        Replace(Collection, values);
    }

    private static bool Matches(string query, params string?[] values) => values.Any(value =>
        value?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) == true);
}
