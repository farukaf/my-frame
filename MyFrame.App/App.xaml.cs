namespace MyFrame.App;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    public App(MainPage mainPage) { InitializeComponent(); _mainPage = mainPage; }

    protected override Window CreateWindow(IActivationState? activationState) => new(_mainPage)
    {
        Title = "My Frame", Width = 1440, Height = 900, MinimumWidth = 1050, MinimumHeight = 700
    };
}
