namespace MyFrame.App;

public partial class MainPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    public MainPage(DashboardViewModel viewModel)
    {
        StartupDiagnostics.Track("MainPage.Begin");
        try
        {
            InitializeComponent();
        }
        catch (Exception error)
        {
            WriteStartupError("MainPage.InitializeComponent", error);
            throw;
        }
        BindingContext = _viewModel = viewModel;
        Loaded += OnLoaded;
        StartupDiagnostics.Track("MainPage.End");
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private static void WriteStartupError(string stage, Exception error)
    {
        StartupDiagnostics.Track(stage, error);
    }
}
