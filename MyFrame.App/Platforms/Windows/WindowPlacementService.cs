using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace MyFrame.App;

public sealed class WindowPlacementService
{
    private const string XKey = "Window.X";
    private const string YKey = "Window.Y";
    private const string WidthKey = "Window.Width";
    private const string HeightKey = "Window.Height";
    private const string MaximizedKey = "Window.Maximized";
    private const int DefaultWidth = 1440;
    private const int DefaultHeight = 900;
    private const int MinimumWidth = 1050;
    private const int MinimumHeight = 700;

    private AppWindow? _appWindow;
    private RectInt32 _restoredBounds;

    public void Attach(Microsoft.Maui.Controls.Window window)
    {
        if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow) return;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        var display = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = display.WorkArea;
        _restoredBounds = Preferences.Default.ContainsKey(WidthKey)
            ? RestoreSavedBounds(workArea)
            : CenteredBounds(workArea);
        _appWindow.MoveAndResize(_restoredBounds);
        _appWindow.Changed += OnAppWindowChanged;

        if (Preferences.Default.Get(MaximizedKey, false) &&
            _appWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();
    }

    public void Save()
    {
        if (_appWindow is null) return;
        var maximized = _appWindow.Presenter is OverlappedPresenter
            { State: OverlappedPresenterState.Maximized };
        Preferences.Default.Set(MaximizedKey, maximized);
        Preferences.Default.Set(XKey, _restoredBounds.X);
        Preferences.Default.Set(YKey, _restoredBounds.Y);
        Preferences.Default.Set(WidthKey, _restoredBounds.Width);
        Preferences.Default.Set(HeightKey, _restoredBounds.Height);
        _appWindow.Changed -= OnAppWindowChanged;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored } &&
            (args.DidPositionChange || args.DidSizeChange))
            _restoredBounds = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
    }

    private static RectInt32 CenteredBounds(RectInt32 workArea)
    {
        var width = Math.Min(DefaultWidth, workArea.Width);
        var height = Math.Min(DefaultHeight, workArea.Height);
        return new RectInt32(workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2, width, height);
    }

    private static RectInt32 RestoreSavedBounds(RectInt32 workArea)
    {
        var width = Math.Clamp(Preferences.Default.Get(WidthKey, DefaultWidth),
            Math.Min(MinimumWidth, workArea.Width), workArea.Width);
        var height = Math.Clamp(Preferences.Default.Get(HeightKey, DefaultHeight),
            Math.Min(MinimumHeight, workArea.Height), workArea.Height);
        var x = Math.Clamp(Preferences.Default.Get(XKey, workArea.X), workArea.X,
            workArea.X + workArea.Width - width);
        var y = Math.Clamp(Preferences.Default.Get(YKey, workArea.Y), workArea.Y,
            workArea.Y + workArea.Height - height);
        return new RectInt32(x, y, width, height);
    }
}
