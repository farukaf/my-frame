using LiveChartsCore.SkiaSharpView.Maui;
using Microsoft.Extensions.Logging;
using MyFrame.Core;
using Serilog;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace MyFrame.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        StartupDiagnostics.Track("MauiProgram.Begin");
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().UseSkiaSharp().UseLiveCharts().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });
#if DEBUG
        builder.Logging.AddDebug();
#endif
        builder.Logging.AddSerilog(Log.Logger, dispose: true);
        var automaticAlecaDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlecaFrame");
        var alecaDirectory = Preferences.Default.Get(AlecaFrameDirectorySettings.PreferenceKey, automaticAlecaDirectory);
        builder.Services.AddSingleton<IAlecaFramePath>(new AlecaFramePath(alecaDirectory));
        builder.Services.AddSingleton(new AlecaFrameDirectorySettings(automaticAlecaDirectory));
        builder.Services.AddSingleton<LocalSettings>();
        builder.Services.AddSingleton<IAlecaFrameReader, AlecaFrameReader>();
        builder.Services.AddSingleton<IAlecaCatalogReader, AlecaCatalogReader>();
        builder.Services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
        builder.Services.AddSingleton<IPriceCache>(_ => new JsonPriceCache(Path.Combine(FileSystem.Current.AppDataDirectory, "market-quotes.json")));
        builder.Services.AddSingleton<IWarframeMarketClient>(p => new WarframeMarketClient(
            new HttpClient(), p.GetRequiredService<IAlecaFramePath>(),
            p.GetRequiredService<ILogger<WarframeMarketClient>>()));
        builder.Services.AddSingleton(p => new DashboardService(p.GetRequiredService<IAlecaFramePath>(),
            p.GetRequiredService<IAlecaFrameReader>(), p.GetRequiredService<IAlecaCatalogReader>(),
            p.GetRequiredService<IWarframeMarketClient>(), p.GetRequiredService<IPriceCache>(),
            p.GetRequiredService<IRecommendationEngine>(), p.GetRequiredService<ILogger<DashboardService>>()));
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<MainPage>();
        var app = builder.Build();
        StartupDiagnostics.Track("MauiProgram.End");
        return app;
    }
}
