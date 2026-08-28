namespace MyFrame.App;

using Serilog;

internal static class StartupDiagnostics
{
    internal static void Track(string stage, Exception? error = null)
    {
        AppLogging.Configure();
        if (error is null) Log.Debug("Startup stage {StartupStage}", stage);
        else Log.Error(error, "Startup failed at stage {StartupStage}", stage);
    }
}
