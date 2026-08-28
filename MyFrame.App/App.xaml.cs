namespace MyFrame.App;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    public App(MainPage mainPage)
    {
        StartupDiagnostics.Track("App.Begin");
        InitializeComponent();
        _mainPage = mainPage;
        StartupDiagnostics.Track("App.End");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        try
        {
            StartupDiagnostics.Track("App.CreateWindow.Begin");
            var window = new Window(_mainPage);
            StartupDiagnostics.Track("App.CreateWindow.Constructed");
            window.Title = "My Frame";
            window.Width = 1440;
            window.Height = 900;
            window.MinimumWidth = 1050;
            window.MinimumHeight = 700;
            StartupDiagnostics.Track("App.CreateWindow.End");
            return window;
        }
        catch (Exception error)
        {
            StartupDiagnostics.Track("App.CreateWindow", error);
            throw;
        }
    }
}
