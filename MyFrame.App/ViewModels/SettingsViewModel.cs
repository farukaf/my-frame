using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFrame.Core;

namespace MyFrame.App;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAlecaFramePath _alecaPath;
    private readonly AlecaFrameDirectorySettings _directorySettings;
    private readonly ISettingsStore _preferences;
    private readonly LocalSettings _localSettings;
    private readonly IFolderPicker _folderPicker;
    private readonly DashboardSettingsState _settings;
    private readonly Func<Task> _refresh;
    private readonly Action<string>? _setStatus;
    public SettingsViewModel(IAlecaFramePath alecaPath, AlecaFrameDirectorySettings directorySettings,
        ISettingsStore preferences, LocalSettings localSettings, IFolderPicker folderPicker, DashboardSettingsState settings,
        Func<Task> refresh, Action<string>? setStatus = null)
    {
        _alecaPath = alecaPath; _directorySettings = directorySettings; _preferences = preferences;
        _localSettings = localSettings;
        _folderPicker = folderPicker; _settings = settings; _refresh = refresh; _setStatus = setStatus;
        AlecaFrameDirectory = alecaPath.DirectoryPath;
        McpExecutablePath = ResolveMcpExecutablePath(AppContext.BaseDirectory);
        var quoted = $"\"{McpExecutablePath.Replace("\"", "\\\"")}\"";
        CodexMcpCommand = $"codex mcp add my-frame -- {quoted}";
        ClaudeMcpCommand = $"claude mcp add --transport stdio --scope user my-frame -- {quoted}";
        if (!File.Exists(McpExecutablePath))
            McpCopyMessage = "MCP server executable not found. Publish or rebuild My Frame first.";
        _settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DashboardSettingsState.DucatsPerPlatinum)) OnPropertyChanged(nameof(DucatsPerPlatinum));
            if (args.PropertyName == nameof(DashboardSettingsState.UnvaultedPrimeSetsToReserve)) OnPropertyChanged(nameof(UnvaultedPrimeSetsToReserve));
        };
    }
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial string AlecaFrameDirectory { get; set; } = "";
    [ObservableProperty] public partial string AlecaFrameDirectoryMessage { get; set; } = "Using the detected AlecaFrame folder.";
    [ObservableProperty] public partial string McpExecutablePath { get; set; } = "";
    [ObservableProperty] public partial string CodexMcpCommand { get; set; } = "";
    [ObservableProperty] public partial string ClaudeMcpCommand { get; set; } = "";
    [ObservableProperty] public partial string McpCopyMessage { get; set; } = "";
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
        _localSettings.AlecaFrameDirectory = directory;
        _alecaPath.SetDirectory(directory);
        AlecaFrameDirectory = directory;
        AlecaFrameDirectoryMessage = "Folder saved. Legacy inventory and catalog import use this location; market credentials stay in My Frame storage.";
        _setStatus?.Invoke("AlecaFrame folder configured. Loading data…");
        await _refresh();
    }

    [RelayCommand]
    private void ResetAlecaFrameDirectory()
    {
        _preferences.Remove(AlecaFrameDirectorySettings.PreferenceKey);
        _localSettings.AlecaFrameDirectory = _directorySettings.AutomaticDirectory;
        _alecaPath.SetDirectory(_directorySettings.AutomaticDirectory);
        AlecaFrameDirectory = _alecaPath.DirectoryPath;
        var error = AlecaFrameDirectorySettings.ValidationError(AlecaFrameDirectory);
        AlecaFrameDirectoryMessage = error is null ? "Restored automatic detection (%LOCALAPPDATA%\\AlecaFrame)." : $"Automatic location restored, but it is not ready: {error}";
        if (error is not null) _setStatus?.Invoke("AlecaFrame data folder needs to be configured.");
    }

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
}
