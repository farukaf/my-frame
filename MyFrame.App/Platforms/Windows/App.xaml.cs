using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MyFrame.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		global::MyFrame.App.AppLogging.Configure();
		UnhandledException += OnUnhandledException;
		global::MyFrame.App.StartupDiagnostics.Track("WinUI.App.Begin");
		this.InitializeComponent();
		global::MyFrame.App.StartupDiagnostics.Track("WinUI.App.End");
	}

	private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
	{
		try
		{
			global::MyFrame.App.StartupDiagnostics.Track("WinUI.UnhandledException", args.Exception);
		}
		catch
		{
			// The original startup exception must remain the primary failure.
		}
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

