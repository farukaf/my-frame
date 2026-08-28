using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace MyFrame.App;

internal static class AppLogging
{
    private static readonly object Gate = new();
    private static bool _configured;

    internal static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyFrame", "logs");

    internal static void Configure()
    {
        lock (Gate)
        {
            if (_configured) return;
            Directory.CreateDirectory(DirectoryPath);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "MyFrame")
                .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    Path.Combine(DirectoryPath, "my-frame-.json"),
                    rollingInterval: RollingInterval.Hour,
                    retainedFileCountLimit: 168,
                    fileSizeLimitBytes: 25 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
                .CreateLogger();
            _configured = true;
            Log.Information("Structured logging initialized; directory {LogDirectory}", DirectoryPath);
        }
    }
}
