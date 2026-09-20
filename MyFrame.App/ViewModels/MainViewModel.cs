using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyFrame.Core;

namespace MyFrame.App;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDashboardService _service;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IAlecaFramePath _alecaPath;
    private bool _initialized;
    private CancellationTokenSource? _settingsDebounce;

    public MainViewModel(IDashboardService service, ILogger<MainViewModel> logger,
        IAlecaFramePath alecaPath, AlecaFrameDirectorySettings directorySettings,
        LocalSettings localSettings, ISettingsStore preferences, IFolderPicker folderPicker,
        IExternalBrowser externalBrowser, SyncStatusReader syncStatusReader,
        CollectorCaptureInboxService collectorCaptureInbox, CollectorCaptureInboxWatcher collectorCaptureWatcher)
    {
        _service = service; _logger = logger; _alecaPath = alecaPath;
        Dashboard = new(); Collection = new(); Farm = new(); Relics = new(); Surplus = new();
        var settings = new DashboardSettingsState(localSettings);
        GlobalStatus = new(); ExternalBrowser = externalBrowser;
        Sales = new(settings);
        SyncStatus = new(syncStatusReader, collectorCaptureInbox, collectorCaptureWatcher, logger);
        collectorCaptureWatcher.CaptureDetected += (_, _) => SyncStatus.HandleCaptureDetected();
        Settings = new(alecaPath, directorySettings, preferences, localSettings, folderPicker, settings, RefreshCoreAsync,
            message => GlobalStatus.StatusMessage = message);
        settings.PropertyChanged += (_, _) => ScheduleRescore();
        _service.SnapshotUpdated += OnSnapshotUpdated;
        _service.SyncProgressChanged += (_, status) => MainThread.BeginInvokeOnMainThread(() => GlobalStatus.ApplySyncStatus(status));
        ShowSection("Dashboard");
    }

    public DashboardSectionViewModel Dashboard { get; }
    public CollectionViewModel Collection { get; }
    public FarmViewModel Farm { get; }
    public RelicsViewModel Relics { get; }
    public SalesViewModel Sales { get; }
    public SurplusViewModel Surplus { get; }
    public SettingsViewModel Settings { get; }
    public SyncStatusViewModel SyncStatus { get; }
    public GlobalStatusViewModel GlobalStatus { get; }
    private IExternalBrowser ExternalBrowser { get; }
    [ObservableProperty] public partial string CurrentSection { get; set; } = "Dashboard";

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        SyncStatus.StartWatcher();
        _logger.LogInformation("Dashboard view initialized");
        await SyncStatus.RefreshSyncStatusAsync();
        var directoryError = AlecaFrameDirectorySettings.ValidationError(_alecaPath.DirectoryPath);
        var hasSynchronizedData = false;
        try
        {
            hasSynchronizedData = await SyncStatus.HasSynchronizedDataAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogWarning(error, "Synchronized SQLite data could not be inspected during startup");
        }
        if (directoryError is not null && !hasSynchronizedData)
        {
            GlobalStatus.StatusMessage = "No synchronized Warframe data is available yet.";
            Settings.AlecaFrameDirectoryMessage = $"{directoryError} Choose the AlecaFrame data folder to continue.";
            ShowSection("Settings");
            return;
        }
        if (directoryError is not null)
            GlobalStatus.StatusMessage = "Using synchronized My Frame data; legacy AlecaFrame import is optional.";
        await RefreshCoreAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync();

    private async Task RefreshCoreAsync()
    {
        if (GlobalStatus.IsBusy) return;
        GlobalStatus.IsBusy = true;
        GlobalStatus.StatusMessage = "Reading inventory and updating prices…";
        try { Apply(await _service.RefreshAsync(true, CurrentSettings())); }
        catch (Exception error)
        {
            _logger.LogError(error, "Dashboard refresh failed");
            GlobalStatus.StatusMessage = $"Synchronization failed: {error.Message}";
        }
        finally { GlobalStatus.IsBusy = false; }
    }

    [RelayCommand]
    private void ShowSection(string section)
    {
        CurrentSection = section;
        Dashboard.IsVisible = section == "Dashboard";
        Collection.IsVisible = section == "Collection";
        Farm.IsVisible = section == "Farm";
        Relics.IsVisible = section == "Relics";
        Sales.IsVisible = section == "Sales";
        Surplus.IsVisible = section == "Surplus";
        Settings.IsVisible = section == "Settings";
        SyncStatus.IsVisible = section == "SyncStatus";
    }

    [RelayCommand]
    private Task OpenWikiAsync() => ExternalBrowser.OpenAsync("https://wiki.warframe.com/");

    private RecommendationSettings CurrentSettings() => new((int)Sales.DucatsPerPlatinum, Settings.UnvaultedPrimeSetsToReserve);
    private void OnSnapshotUpdated(object? sender, DashboardSnapshot snapshot) => MainThread.BeginInvokeOnMainThread(() => Apply(snapshot));
    private void Apply(DashboardSnapshot snapshot)
    {
        GlobalStatus.Apply(snapshot); Dashboard.Apply(snapshot); Collection.Apply(snapshot);
        Farm.Apply(snapshot); Relics.Apply(snapshot); Sales.Apply(snapshot); Surplus.Apply(snapshot);
    }
    private void ScheduleRescore()
    {
        _settingsDebounce?.Cancel(); _settingsDebounce?.Dispose();
        _settingsDebounce = new CancellationTokenSource();
        var token = _settingsDebounce.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(120, token); _service.Reapply(CurrentSettings()); }
            catch (OperationCanceledException) { }
            catch (Exception error) { _logger.LogWarning(error, "Rescoring after a settings change failed"); }
        }, token);
    }
    public void Dispose()
    {
        _service.SnapshotUpdated -= OnSnapshotUpdated;
        SyncStatus.StopWatcher();
        _settingsDebounce?.Cancel(); _settingsDebounce?.Dispose();
    }
}
