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
        var automaticAlecaDirectory = MyFrameStoragePaths.DefaultAlecaFrameDirectory;
        var migration = SharedDataMigration.Ensure();
        var alecaDirectory = migration.Settings.AlecaFrameDirectory;
        builder.Services.AddSingleton<IMyFrameSettingsWriter>(migration.Store);
        builder.Services.AddSingleton<IMyFrameSettingsStore>(migration.Store);
        builder.Services.AddSingleton<IAlecaFramePath>(new AlecaFramePath(alecaDirectory));
        builder.Services.AddSingleton<IAlecaFrameChangeMonitor, FileSystemAlecaFrameChangeMonitor>();
        builder.Services.AddSingleton(new AlecaFrameDirectorySettings(automaticAlecaDirectory));
        builder.Services.AddSingleton<LocalSettings>();
        builder.Services.AddSingleton<WindowPlacementService>();
        builder.Services.AddSingleton<IFolderPicker, MauiFolderPicker>();
        builder.Services.AddSingleton<IExternalBrowser, MauiExternalBrowser>();
        builder.Services.AddSingleton<IAlecaFrameReader, AlecaFrameReader>();
        builder.Services.AddSingleton<IAlecaCatalogReader, AlecaCatalogReader>();
        builder.Services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
        builder.Services.AddSingleton(_ => new JsonPriceCache(MyFrameStoragePaths.PriceCachePath));
        builder.Services.AddSingleton<IPriceCache>(provider => provider.GetRequiredService<JsonPriceCache>());
        builder.Services.AddSingleton<IReadOnlyPriceCache>(provider => provider.GetRequiredService<JsonPriceCache>());
        builder.Services.AddSingleton<IMarketStateStore>(_ => new MarketStateStore(MyFrameStoragePaths.MarketStatePath));
        builder.Services.AddSingleton<IMarketItemIndexStore>(_ => new MarketItemIndexStore(MyFrameStoragePaths.MarketItemIndexPath));
        builder.Services.AddSingleton<IWarframeMarketClient>(p => new WarframeMarketClient(
            new HttpClient(), p.GetRequiredService<IAlecaFramePath>(),
            p.GetRequiredService<ILogger<WarframeMarketClient>>()));
        builder.Services.AddSingleton<IMyFrameSnapshotProvider, MyFrameSnapshotProvider>();
        builder.Services.AddSingleton(p => new DashboardService(p.GetRequiredService<IAlecaFramePath>(),
            p.GetRequiredService<IAlecaFrameReader>(), p.GetRequiredService<IAlecaCatalogReader>(),
            p.GetRequiredService<IWarframeMarketClient>(), p.GetRequiredService<IPriceCache>(),
            p.GetRequiredService<IMarketStateStore>(), p.GetRequiredService<IMarketItemIndexStore>(),
            p.GetRequiredService<IRecommendationEngine>(),
            p.GetRequiredService<ILogger<DashboardService>>(),
            p.GetRequiredService<IMyFrameSnapshotProvider>()));
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<IDashboardService>(p => p.GetRequiredService<DashboardService>());
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();
        var app = builder.Build();
        StartupDiagnostics.Track("MauiProgram.End");
        return app;
    }
}
