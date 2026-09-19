using CommunityToolkit.Mvvm.ComponentModel;
using MyFrame.Core;

namespace MyFrame.App;

public partial class DashboardSettingsState : ObservableObject
{
    private readonly LocalSettings _localSettings;
    private double _ducatsPerPlatinum;
    private int _unvaultedPrimeSetsToReserve;
    public DashboardSettingsState(LocalSettings localSettings)
    {
        _localSettings = localSettings;
        _ducatsPerPlatinum = localSettings.DucatsPerPlatinum;
        _unvaultedPrimeSetsToReserve = localSettings.UnvaultedPrimeSetsToReserve;
    }
    public double DucatsPerPlatinum { get => _ducatsPerPlatinum; set { var normalized = Math.Clamp((int)Math.Round(value), 1, 50); if (normalized == _ducatsPerPlatinum) return; _ducatsPerPlatinum = normalized; _localSettings.DucatsPerPlatinum = normalized; OnPropertyChanged(); } }
    public int UnvaultedPrimeSetsToReserve { get => _unvaultedPrimeSetsToReserve; set { var normalized = Math.Clamp(value, 0, 10); if (normalized == _unvaultedPrimeSetsToReserve) return; _unvaultedPrimeSetsToReserve = normalized; _localSettings.UnvaultedPrimeSetsToReserve = normalized; OnPropertyChanged(); } }
    public RecommendationSettings Current() => new((int)DucatsPerPlatinum, UnvaultedPrimeSetsToReserve);
}
