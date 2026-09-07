using Microsoft.Extensions.Logging;
using MyFrame.Core;

namespace MyFrame.App;

/// Presentation root; DashboardViewModel remains the compatibility-backed state owner during extraction.
public sealed class MainViewModel : DashboardViewModel
{
    public MainViewModel(IDashboardService service, ILogger<DashboardViewModel> logger,
        IAlecaFramePath alecaPath, AlecaFrameDirectorySettings directorySettings,
        LocalSettings localSettings, ISettingsStore preferences)
        : base(service, logger, alecaPath, directorySettings, localSettings, preferences) { }
}
