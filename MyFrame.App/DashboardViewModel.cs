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
    private readonly SyncStatusReader _syncStatusReader;
    private readonly WorldStateSyncService _worldStateSync;
    private readonly CollectorCaptureInboxService _collectorCaptureInbox;
    private readonly CollectorCaptureInboxWatcher _collectorCaptureWatcher;
    private bool _initialized;
    private CancellationTokenSource? _settingsDebounce;
    private IReadOnlyList<CollectionGoal> _allCollection = [];
    private IReadOnlyList<FarmRecommendation> _allFarm = [];
    private IReadOnlyList<SaleRecommendation> _allSales = [];
    private IReadOnlyList<RelicRecommendation> _allRelics = [];
    private IReadOnlyList<SurplusRecommendation> _allSurplus = [];

    public DashboardViewModel(DashboardService service, ILogger<DashboardViewModel> logger,
        IAlecaFramePath alecaPath, AlecaFrameDirectorySettings directorySettings, LocalSettings localSettings,
        SyncStatusReader syncStatusReader, CollectorCaptureInboxService collectorCaptureInbox,
        CollectorCaptureInboxWatcher collectorCaptureWatcher, WorldStateSyncService worldStateSync)
    {
        _service = service;
        _logger = logger;
        _alecaPath = alecaPath;
        _directorySettings = directorySettings;
        _localSettings = localSettings;
        _syncStatusReader = syncStatusReader;
        _worldStateSync = worldStateSync;
        _collectorCaptureInbox = collectorCaptureInbox;
        _collectorCaptureWatcher = collectorCaptureWatcher;
        _collectorCaptureWatcher.CaptureDetected += OnCollectorCaptureDetected;
        AlecaFrameDirectory = alecaPath.DirectoryPath;
        DucatsPerPlatinum = localSettings.DucatsPerPlatinum;
        UnvaultedPrimeSetsToReserve = localSettings.UnvaultedPrimeSetsToReserve;
        _service.SnapshotUpdated += (_, snapshot) => MainThread.BeginInvokeOnMainThread(() => Apply(snapshot));
        _service.SyncProgressChanged += (_, status) => MainThread.BeginInvokeOnMainThread(() => ApplySyncStatus(status));
        ShowSection("Dashboard");
        McpExecutablePath = ResolveMcpExecutablePath(AppContext.BaseDirectory);
        CollectorCaptureDirectory = _collectorCaptureInbox.DirectoryPath;
        var quoted = $"\"{McpExecutablePath.Replace("\"", "\\\"")}\"";
        CodexMcpCommand = $"codex mcp add my-frame -- {quoted}";
        ClaudeMcpCommand = $"claude mcp add --transport stdio --scope user my-frame -- {quoted}";
        if (!File.Exists(McpExecutablePath))
            McpCopyMessage = "MCP server executable not found. Publish or rebuild My Frame first.";
    }

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "Waiting for synchronized Warframe data…";
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
    [ObservableProperty] public partial bool SurplusVisible { get; set; }
    [ObservableProperty] public partial bool SettingsVisible { get; set; }
    [ObservableProperty] public partial bool SyncStatusVisible { get; set; }
    [ObservableProperty] public partial bool IsLoadingSyncStatus { get; set; }
    [ObservableProperty] public partial string SyncStatusMessage { get; set; } = "Status not loaded.";
    [ObservableProperty] public partial bool IsSyncingWorldState { get; set; }
    [ObservableProperty] public partial string WorldStateSyncMessage { get; set; } = "No World State synchronization requested.";
    [ObservableProperty] public partial bool AllowCollectorRawPayload { get; set; }
    [ObservableProperty] public partial bool IsImportingCollectorCaptures { get; set; }
    [ObservableProperty] public partial string CollectorCaptureDirectory { get; set; } = "";
    [ObservableProperty] public partial string CollectorCaptureMessage { get; set; } = "No capture import has been requested.";
    [ObservableProperty] public partial string CollectorCaptureNotice { get; set; } = "";
    [ObservableProperty] public partial bool CollectorCaptureNoticeVisible { get; set; }
    [ObservableProperty] public partial string SelectedCollectionFilter { get; set; } = "In progress";
    [ObservableProperty] public partial string SelectedCollectionSort { get; set; } = "Closest to completion";
    [ObservableProperty] public partial string AlecaFrameDirectory { get; set; } = "";
    [ObservableProperty] public partial string AlecaFrameDirectoryMessage { get; set; } = "Using the detected AlecaFrame folder.";
    [ObservableProperty] public partial string SelectedSalesFilter { get; set; } = "All recommendations";
    [ObservableProperty] public partial string SelectedSalesSort { get; set; } = "Name";
    [ObservableProperty] public partial string ActivePrimeSetReserveText { get; set; } = "—";
    [ObservableProperty] public partial bool ActivePrimeSetReserveEnabled { get; set; }
    [ObservableProperty] public partial string CollectionSearchText { get; set; } = "";
    [ObservableProperty] public partial string FarmSearchText { get; set; } = "";
    [ObservableProperty] public partial string SalesSearchText { get; set; } = "";
    [ObservableProperty] public partial string RelicsSearchText { get; set; } = "";
    [ObservableProperty] public partial string SurplusSearchText { get; set; } = "";
    [ObservableProperty] public partial bool SurplusShowMastered { get; set; } = true;
    [ObservableProperty] public partial bool SurplusShowCrafted { get; set; } = true;
    [ObservableProperty] public partial bool SurplusShowOnlyOneNeeded { get; set; } = true;
    [ObservableProperty] public partial string SurplusPlatinum { get; set; } = "Any";
    [ObservableProperty] public partial string SurplusSummary { get; set; } = "0 spare";
    [ObservableProperty] public partial string SurplusEmptyMessage { get; set; } =
        "Nothing spare. Every part you hold still has something to build.";
    [ObservableProperty] public partial string AlecaDataUpdatedText { get; set; } = "—";
    [ObservableProperty] public partial string FilteredDucatsEstimate { get; set; } = "0";
    [ObservableProperty] public partial bool IncludeVaultedParts { get; set; } = true;
    [ObservableProperty] public partial bool IsSyncingPrices { get; set; }
    [ObservableProperty] public partial double SyncProgress { get; set; }
    [ObservableProperty] public partial string SyncProgressText { get; set; } = "";
    [ObservableProperty] public partial bool PricesStale { get; set; }
    [ObservableProperty] public partial string StaleWarningText { get; set; } = "";
    [ObservableProperty] public partial string McpExecutablePath { get; set; } = "";
    [ObservableProperty] public partial string CodexMcpCommand { get; set; } = "";
    [ObservableProperty] public partial string ClaudeMcpCommand { get; set; } = "";
    [ObservableProperty] public partial string McpCopyMessage { get; set; } = "";

    public ObservableCollection<CollectionGoal> Collection { get; } = [];
    public ObservableCollection<FarmRecommendation> Farm { get; } = [];
    public ObservableCollection<SaleRecommendation> Sales { get; } = [];
    public ObservableCollection<RelicRecommendation> Relics { get; } = [];
    public ObservableCollection<SurplusRecommendation> Surplus { get; } = [];
    public ObservableCollection<SyncSourceStatusRow> SyncSources { get; } = [];
    public ObservableCollection<SyncAttemptStatusRow> SyncAttempts { get; } = [];
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
        _collectorCaptureWatcher.Start();
        _logger.LogInformation("Dashboard view initialized");
        await RefreshSyncStatusAsync();
        var directoryError = AlecaFrameDirectorySettings.ValidationError(_alecaPath.DirectoryPath);
        var hasSynchronizedData = false;
        try
        {
            hasSynchronizedData = await _syncStatusReader.HasSynchronizedDataAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogWarning(error, "Synchronized SQLite data could not be inspected during startup");
        }
        if (directoryError is not null && !hasSynchronizedData)
        {
            _logger.LogInformation("No synchronized SQLite data is available; opening optional legacy Settings");
            StatusMessage = "No synchronized Warframe data is available yet.";
            AlecaFrameDirectoryMessage = $"{directoryError} Choose the AlecaFrame data folder to continue.";
            ShowSection("Settings");
            return;
        }
        if (directoryError is not null)
            StatusMessage = "Using synchronized My Frame data; legacy AlecaFrame import is optional.";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshSyncStatusAsync()
    {
        if (IsLoadingSyncStatus) return;
        IsLoadingSyncStatus = true;
        SyncStatusMessage = "Reading the shared data store…";
        try
        {
            var rows = await _syncStatusReader.ReadAsync();
            SyncSources.Clear();
            foreach (var row in rows) SyncSources.Add(row);
            var attempts = await _syncStatusReader.ReadRecentRunsAsync();
            SyncAttempts.Clear();
            foreach (var attempt in attempts) SyncAttempts.Add(attempt);
            SyncStatusMessage = "Read-only view of the shared My Frame SQLite store.";
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Sync status read failed");
            SyncStatusMessage = "Unable to read synchronization status.";
        }
        finally { IsLoadingSyncStatus = false; }
    }

    [RelayCommand]
    private async Task SyncWorldStateAsync()
    {
        if (IsSyncingWorldState) return;
        IsSyncingWorldState = true;
        WorldStateSyncMessage = "Fetching Warframe World State…";
        try
        {
            var result = await _worldStateSync.RunAsync();
            WorldStateSyncMessage = result.State == "published"
                ? $"World State synchronized: {result.Records:N0} bounties; revision {result.RevisionId}."
                : $"World State synchronization failed: {result.ErrorCode ?? result.State}.";
            await RefreshSyncStatusAsync();
        }
        catch (Exception error)
        {
            _logger.LogError(error, "World State synchronization failed");
            WorldStateSyncMessage = "World State synchronization failed; previous data was preserved.";
        }
        finally { IsSyncingWorldState = false; }
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
            var result = await _collectorCaptureInbox.ImportAsync(true);
            CollectorCaptureNotice = "";
            CollectorCaptureNoticeVisible = false;
            CollectorCaptureMessage = $"Encontradas {result.Discovered:N0}; novas {result.Imported:N0}; já publicadas {result.AlreadyPublished:N0}; rejeitadas {result.Rejected:N0}.";
            await RefreshSyncStatusAsync();
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Collector capture inbox import failed");
            CollectorCaptureMessage = "Importação rejeitada; nenhum payload foi publicado.";
        }
        finally
        {
            AllowCollectorRawPayload = false;
            IsImportingCollectorCaptures = false;
        }
    }

    private void OnCollectorCaptureDetected(object? sender, EventArgs args)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            CollectorCaptureNotice = "Nova captura detectada. Revise e importe com consentimento explícito.";
            CollectorCaptureNoticeVisible = true;
            await RefreshSyncStatusAsync();
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Reading inventory and updating prices…";
        try { Apply(await _service.RefreshAsync(true, CurrentSettings())); }
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
        _localSettings.AlecaFrameDirectory = directory;
        _alecaPath.SetDirectory(directory);
        AlecaFrameDirectory = _alecaPath.DirectoryPath;
        AlecaFrameDirectoryMessage = "Folder saved. Legacy inventory and catalog import use this location; market credentials stay in My Frame storage.";
        StatusMessage = "AlecaFrame folder configured. Loading data…";
        await RefreshAsync();
    }

    [RelayCommand]
    private void ResetAlecaFrameDirectory()
    {
        _localSettings.AlecaFrameDirectory = _directorySettings.AutomaticDirectory;
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
        SurplusVisible = section == "Surplus";
        SettingsVisible = section == "Settings";
        SyncStatusVisible = section == "SyncStatus";
    }

    // Runs both for a published snapshot and for the cheap progress ticks in between, so the
    // status area keeps moving while the price pass is still running.
    private void ApplySyncStatus(SyncStatus status)
    {
        StatusMessage = status.Message;
        IsSyncingPrices = status.IsLoading;
        SyncProgress = status.PriceProgress;
        SyncProgressText = status.PriceProgressText;
        StaleWarningText = status.Error ?? status.StaleWarning;
        PricesStale = !status.IsLoading && StaleWarningText.Length > 0;
    }

    private void Apply(DashboardSnapshot snapshot)
    {
        ApplySyncStatus(snapshot.Status);
        LastSyncText = snapshot.Status.LastSuccessfulSync?.ToString("dd/MM/yyyy HH:mm:ss") ?? "—";
        AlecaDataUpdatedText = snapshot.Inventory.CapturedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        AccountText = snapshot.Account is null ? "Token missing or expired" : $"{snapshot.Account.IngameName} · {snapshot.Account.Platform}";
        TotalPlatinum = $"{snapshot.Recommendations.EstimatedPlatinum:N0}p";
        TotalDucats = $"{snapshot.Recommendations.TotalDucats:N0} ducats";
        var activeSettings = snapshot.Recommendations.Settings;
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
        _allSurplus = snapshot.Recommendations.Surplus;
        ApplyCollectionView();
        ApplyFarmView();
        ApplySalesView();
        ApplyRelicsView();
        ApplySurplusView();
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

    [RelayCommand]
    private async Task CopyMcpCommandAsync(string client)
    {
        if (!File.Exists(McpExecutablePath))
        {
            McpCopyMessage = "MCP server executable not found. Publish or rebuild My Frame first.";
            return;
        }

        var command = client.Equals("Claude", StringComparison.OrdinalIgnoreCase)
            ? ClaudeMcpCommand : CodexMcpCommand;
        await Clipboard.Default.SetTextAsync(command);
        McpCopyMessage = $"{client} command copied.";
    }

    internal static string ResolveMcpExecutablePath(string appBaseDirectory)
    {
        var bundled = Path.Combine(appBaseDirectory, "MyFrame.Mcp.exe");
        if (File.Exists(bundled)) return bundled;

        // Development builds keep each executable in its own project output. Derive that
        // sibling path from ...\MyFrame.App\bin\<Configuration>\<TFM>\<RID> without
        // leaking a repository-specific absolute path into the application.
        var runtimeDirectory = new DirectoryInfo(Path.TrimEndingDirectorySeparator(appBaseDirectory));
        var frameworkDirectory = runtimeDirectory.Parent;
        var configurationDirectory = frameworkDirectory?.Parent;
        var binDirectory = configurationDirectory?.Parent;
        var appProjectDirectory = binDirectory?.Parent;
        var repositoryDirectory = appProjectDirectory?.Parent;
        if (runtimeDirectory.Name.StartsWith("win-", StringComparison.OrdinalIgnoreCase) &&
            frameworkDirectory?.Name.StartsWith("net", StringComparison.OrdinalIgnoreCase) == true &&
            string.Equals(binDirectory?.Name, "bin", StringComparison.OrdinalIgnoreCase) &&
            repositoryDirectory is not null)
        {
            var development = Path.Combine(repositoryDirectory.FullName, "MyFrame.Mcp", "bin",
                configurationDirectory!.Name, "net10.0", runtimeDirectory.Name, "MyFrame.Mcp.exe");
            if (File.Exists(development)) return development;
        }

        return bundled;
    }

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
    partial void OnIncludeVaultedPartsChanged(bool value) => ApplySalesView();

    // The label is part of the hit area, so tapping either half flips the box.
    [RelayCommand]
    private void ToggleVaultedParts() => IncludeVaultedParts = !IncludeVaultedParts;
    partial void OnRelicsSearchTextChanged(string value) => ApplyRelicsView();
    partial void OnSurplusSearchTextChanged(string value) => ApplySurplusView();
    partial void OnSurplusShowMasteredChanged(bool value) => ApplySurplusView();
    partial void OnSurplusShowCraftedChanged(bool value) => ApplySurplusView();
    partial void OnSurplusShowOnlyOneNeededChanged(bool value) => ApplySurplusView();
    partial void OnSurplusPlatinumChanged(string value) => ApplySurplusView();

    // Each label is part of its box's hit area, so tapping either half flips the tick.
    [RelayCommand]
    private void ToggleSurplusFilter(string filter)
    {
        switch (filter)
        {
            case "Mastered": SurplusShowMastered = !SurplusShowMastered; break;
            case "Crafted": SurplusShowCrafted = !SurplusShowCrafted; break;
            case "OnlyOneNeeded": SurplusShowOnlyOneNeeded = !SurplusShowOnlyOneNeeded; break;
        }
    }

    // Platinum value is one property, so it gets one control. It still needs three positions: a
    // plain tick could only ever mean "all" or "priced ones", never "the ones worth no platinum".
    [RelayCommand]
    private void SetSurplusPlatinum(string value) => SurplusPlatinum = value;

    [RelayCommand]
    private void ResetSurplusFilters() => (SurplusShowMastered, SurplusShowCrafted,
        SurplusShowOnlyOneNeeded, SurplusPlatinum) = (true, true, true, "Any");
    partial void OnDucatsPerPlatinumChanged(double value)
    {
        var integerValue = Math.Clamp((int)Math.Round(value), 1, 50);
        if (Math.Abs(value - integerValue) > double.Epsilon)
        {
            DucatsPerPlatinum = integerValue;
            return;
        }
        _localSettings.DucatsPerPlatinum = integerValue;
        RescoreSoon();
    }

    partial void OnUnvaultedPrimeSetsToReserveChanged(int value)
    {
        _localSettings.UnvaultedPrimeSetsToReserve = value;
        RescoreSoon();
    }

    private RecommendationSettings CurrentSettings() => new(
        Math.Clamp((int)Math.Round(DucatsPerPlatinum), 1, 50),
        Math.Clamp(UnvaultedPrimeSetsToReserve, 0, 10));

    // Dragging the slider raises a change per pixel, so the rescore waits for the drag to settle
    // rather than running the engine dozens of times on the way.
    private void RescoreSoon()
    {
        _settingsDebounce?.Cancel();
        _settingsDebounce?.Dispose();
        _settingsDebounce = new CancellationTokenSource();
        var token = _settingsDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, token);
                _service.Reapply(CurrentSettings());
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { _logger.LogWarning(error, "Rescoring after a settings change failed"); }
        }, token);
    }
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
        if (!IncludeVaultedParts) sales = sales.Where(x => !x.Vaulted);
        if (!string.IsNullOrWhiteSpace(SalesSearchText))
            sales = sales.Where(x => Matches(SalesSearchText, x.ItemName, x.Reason, x.ActionLabel, x.VaultStatus));
        sales = SelectedSalesSort switch
        {
            "Action" => sales.OrderBy(x => x.ActionLabel).ThenBy(x => x.ItemName),
            "Highest value" => sales.OrderByDescending(x => x.TotalPlatinum).ThenBy(x => x.ItemName),
            _ => sales.OrderBy(x => x.ItemName)
        };
        var listed = sales.ToArray();
        // Only the rows actually being recommended for ducats count. Summing every listed row
        // instead would report the same total at any ratio, since a piece is worth the same in
        // ducats whether or not selling it for platinum currently wins.
        FilteredDucatsEstimate = $"{listed
            .Where(x => x.Action == RecommendationAction.ExchangeForDucats)
            .Sum(x => (long)x.TotalDucats):N0}";
        Replace(Sales, listed.Take(200));
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

    // The two controls answer different questions and are combined, not merged: a reason has to be
    // ticked AND the platinum position has to admit the row. That is what makes the useful crossings
    // expressible, such as parts for a mastered item that nobody will pay platinum for.
    private void ApplySurplusView()
    {
        var values = _allSurplus.Where(x => ReasonIsTicked(x.Reason) && PlatinumAdmits(x));
        if (!string.IsNullOrWhiteSpace(SurplusSearchText))
            values = values.Where(x => Matches(SurplusSearchText, x.ItemName, x.ParentName, x.Category,
                x.ReasonBadge, x.Explanation));
        var listed = values.ToArray();
        var platinum = listed.Sum(x => (long)(x.TotalPlatinum ?? 0));
        SurplusSummary = $"{listed.Sum(x => (long)x.Surplus):N0} spare" +
            (platinum > 0 ? $" · ~{platinum:N0}p" : "");
        SurplusEmptyMessage = !SurplusShowMastered && !SurplusShowCrafted && !SurplusShowOnlyOneNeeded
            ? "No reason is ticked, so nothing can match."
            : _allSurplus.Count == 0
                ? "Nothing spare. Every part you hold still has something to build."
                : "No spare part matches the current filters.";
        Replace(Surplus, listed.Take(200));
    }

    private bool ReasonIsTicked(SurplusReason reason) => reason switch
    {
        SurplusReason.Mastered => SurplusShowMastered,
        SurplusReason.Crafted => SurplusShowCrafted,
        _ => SurplusShowOnlyOneNeeded
    };

    private bool PlatinumAdmits(SurplusRecommendation row) => SurplusPlatinum switch
    {
        "Has value" => row.SellableForPlatinum,
        "No value" => !row.SellableForPlatinum,
        _ => true
    };

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
