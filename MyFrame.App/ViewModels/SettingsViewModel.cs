using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFrame.Core;

namespace MyFrame.App;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAlecaFramePath _alecaPath;
    private readonly AlecaFrameDirectorySettings _directorySettings;
    private readonly ISettingsStore _preferences;
    private readonly IFolderPicker _folderPicker;
    private readonly DashboardSettingsState _settings;
    private readonly Func<Task> _refresh;
    public SettingsViewModel(IAlecaFramePath alecaPath, AlecaFrameDirectorySettings directorySettings,
        ISettingsStore preferences, IFolderPicker folderPicker, DashboardSettingsState settings, Func<Task> refresh)
    {
        _alecaPath = alecaPath; _directorySettings = directorySettings; _preferences = preferences;
        _folderPicker = folderPicker; _settings = settings; _refresh = refresh;
        AlecaFrameDirectory = alecaPath.DirectoryPath;
        _settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DashboardSettingsState.DucatsPerPlatinum)) OnPropertyChanged(nameof(DucatsPerPlatinum));
            if (args.PropertyName == nameof(DashboardSettingsState.UnvaultedPrimeSetsToReserve)) OnPropertyChanged(nameof(UnvaultedPrimeSetsToReserve));
        };
    }
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial string AlecaFrameDirectory { get; set; } = "";
    [ObservableProperty] public partial string AlecaFrameDirectoryMessage { get; set; } = "Using the detected AlecaFrame folder.";
    public double DucatsPerPlatinum { get => _settings.DucatsPerPlatinum; set => _settings.DucatsPerPlatinum = value; }
    public int UnvaultedPrimeSetsToReserve { get => _settings.UnvaultedPrimeSetsToReserve; set => _settings.UnvaultedPrimeSetsToReserve = value; }

    [RelayCommand]
    private async Task SelectAlecaFrameDirectoryAsync()
    {
        var directory = await _folderPicker.PickAsync();
        if (directory is null) return;
        var error = AlecaFrameDirectorySettings.ValidationError(directory);
        if (error is not null) { AlecaFrameDirectoryMessage = error; return; }
        _preferences.Set(AlecaFrameDirectorySettings.PreferenceKey, directory);
        _alecaPath.SetDirectory(directory);
        AlecaFrameDirectory = directory;
        AlecaFrameDirectoryMessage = "Folder saved. Inventory, catalogs, token, and monitoring now use this location.";
        await _refresh();
    }

    [RelayCommand]
    private void ResetAlecaFrameDirectory()
    {
        _preferences.Remove(AlecaFrameDirectorySettings.PreferenceKey);
        _alecaPath.SetDirectory(_directorySettings.AutomaticDirectory);
        AlecaFrameDirectory = _alecaPath.DirectoryPath;
        var error = AlecaFrameDirectorySettings.ValidationError(AlecaFrameDirectory);
        AlecaFrameDirectoryMessage = error is null ? "Restored automatic detection (%LOCALAPPDATA%\\AlecaFrame)." : $"Automatic location restored, but it is not ready: {error}";
    }
}
