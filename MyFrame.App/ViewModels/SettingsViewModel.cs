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
    private readonly MarketCredentialService _marketCredentials;
    public SettingsViewModel(IAlecaFramePath alecaPath, AlecaFrameDirectorySettings directorySettings,
        ISettingsStore preferences, LocalSettings localSettings, IFolderPicker folderPicker, DashboardSettingsState settings,
        Func<Task> refresh, Action<string>? setStatus = null, MarketCredentialService? marketCredentials = null)
    {
        _alecaPath = alecaPath; _directorySettings = directorySettings; _preferences = preferences;
        _localSettings = localSettings;
        _folderPicker = folderPicker; _settings = settings; _refresh = refresh; _setStatus = setStatus;
        _marketCredentials = marketCredentials ?? throw new ArgumentNullException(nameof(marketCredentials));
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
    [ObservableProperty] public partial string AlecaFrameDirectoryMessage { get; set; } = "Optional legacy import. Synchronized SQLite data is preferred.";
    [ObservableProperty] public partial string McpExecutablePath { get; set; } = "";
    [ObservableProperty] public partial string CodexMcpCommand { get; set; } = "";
    [ObservableProperty] public partial string ClaudeMcpCommand { get; set; } = "";
    [ObservableProperty] public partial string McpCopyMessage { get; set; } = "";
    [ObservableProperty] public partial string MarketCredentialTokenInput { get; set; } = "";
    [ObservableProperty] public partial string MarketCredentialStatusText { get; set; } = "Checking credential…";
    [ObservableProperty] public partial bool IsSavingMarketCredential { get; set; }
    public double DucatsPerPlatinum { get => _settings.DucatsPerPlatinum; set => _settings.DucatsPerPlatinum = value; }
    public int UnvaultedPrimeSetsToReserve { get => _settings.UnvaultedPrimeSetsToReserve; set => _settings.UnvaultedPrimeSetsToReserve = value; }

    [RelayCommand]
    private async Task RefreshMarketCredentialStatusAsync()
    {
        var status = await _marketCredentials.GetStatusAsync();
        MarketCredentialStatusText = status.State switch
        {
            MarketCredentialState.Valid => $"Credential valid until {status.ExpiresAt:yyyy-MM-dd HH:mm} UTC.",
            MarketCredentialState.Expired => "Credential expired. Private orders are disabled.",
            MarketCredentialState.Invalid => "Credential is invalid. Paste a current token to replace it.",
            _ => "No private credential configured. Public prices remain available."
        };
    }

    [RelayCommand]
    private async Task SaveMarketCredentialAsync()
    {
        if (IsSavingMarketCredential) return;
        IsSavingMarketCredential = true;
        try
        {
            var status = await _marketCredentials.SaveAsync(MarketCredentialTokenInput);
            MarketCredentialTokenInput = "";
            MarketCredentialStatusText = $"Credential saved until {status.ExpiresAt:yyyy-MM-dd HH:mm} UTC.";
            _setStatus?.Invoke("Market credential saved securely.");
        }
        catch (ArgumentException)
        {
            MarketCredentialTokenInput = "";
            MarketCredentialStatusText = "Invalid or expired token; no secret was saved.";
        }
        finally { IsSavingMarketCredential = false; }
    }

    [RelayCommand]
    private async Task RevokeMarketCredentialAsync()
    {
        await _marketCredentials.RevokeAsync();
        MarketCredentialTokenInput = "";
        MarketCredentialStatusText = "Private credential revoked. Public prices remain available.";
        _setStatus?.Invoke("Market credential revoked.");
    }

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
        AlecaFrameDirectoryMessage = "Legacy folder saved. SQLite synchronized data remains the primary source.";
        _setStatus?.Invoke("AlecaFrame folder configured. Loading data…");
        await _refresh();
    }

    [RelayCommand]
    private void ResetAlecaFrameDirectory()
    {
        _preferences.Remove(AlecaFrameDirectorySettings.PreferenceKey);
        _localSettings.AlecaFrameDirectory = string.Empty;
        _alecaPath.SetDirectory(string.Empty);
        AlecaFrameDirectory = _alecaPath.DirectoryPath;
        var error = AlecaFrameDirectorySettings.ValidationError(AlecaFrameDirectory);
        AlecaFrameDirectoryMessage = error is null ? "Legacy folder cleared; SQLite synchronized data remains primary." : $"Legacy folder cleared. SQLite data remains usable; optional import is not ready: {error}";
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
