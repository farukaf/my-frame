using LiveChartsCore.SkiaSharpView.Maui;
using Microsoft.Extensions.Logging;
using MyFrame.Core;
using MyFrame.Core.Sync;
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
        builder.Services.AddSingleton<SqliteSettingsStore>(_ => new SqliteSettingsStore(
            MyFrameStoragePaths.DataDatabasePath, MyFrameStoragePaths.SettingsPath));
        builder.Services.AddSingleton<IMyFrameSettingsWriter>(p => p.GetRequiredService<SqliteSettingsStore>());
        builder.Services.AddSingleton<IMyFrameSettingsStore>(p => p.GetRequiredService<SqliteSettingsStore>());
        builder.Services.AddSingleton<IAlecaFramePath>(new AlecaFramePath(alecaDirectory));
        builder.Services.AddSingleton(new AlecaFrameDirectorySettings(automaticAlecaDirectory));
        builder.Services.AddSingleton<LocalSettings>();
        builder.Services.AddSingleton<SyncStatusReader>();
        builder.Services.AddSingleton<WorldStateSyncService>();
        builder.Services.AddSingleton<PublicExportSyncService>();
        builder.Services.AddSingleton<CollectorCaptureInboxService>();
        builder.Services.AddSingleton<CollectorCaptureInboxWatcher>();
        builder.Services.AddSingleton<WindowPlacementService>();
        builder.Services.AddSingleton<IAlecaFrameReader, AlecaFrameReader>();
        builder.Services.AddSingleton<IAlecaCatalogReader, AlecaCatalogReader>();
        builder.Services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
        builder.Services.AddSingleton(_ => new SqliteMarketStore(MyFrameStoragePaths.DataDatabasePath,
            MyFrameStoragePaths.PriceCachePath, MyFrameStoragePaths.MarketStatePath,
            MyFrameStoragePaths.MarketItemIndexPath));
        builder.Services.AddSingleton<IPriceCache>(provider => provider.GetRequiredService<SqliteMarketStore>());
        builder.Services.AddSingleton<IReadOnlyPriceCache>(provider => provider.GetRequiredService<SqliteMarketStore>());
        builder.Services.AddSingleton<IMarketStateStore>(provider => provider.GetRequiredService<SqliteMarketStore>());
        builder.Services.AddSingleton<IMarketItemIndexStore>(provider => provider.GetRequiredService<SqliteMarketStore>());
        builder.Services.AddSingleton<ProtectedFileMarketTokenStore>(_ => new ProtectedFileMarketTokenStore(MyFrameStoragePaths.MarketTokenPath));
        builder.Services.AddSingleton<MarketCredentialService>(p => new MarketCredentialService(
            p.GetRequiredService<ProtectedFileMarketTokenStore>()));
        builder.Services.AddSingleton<IWarframeMarketClient>(p => new WarframeMarketClient(
            new HttpClient(), p.GetRequiredService<ProtectedFileMarketTokenStore>(),
            p.GetRequiredService<ILogger<WarframeMarketClient>>()));
        builder.Services.AddSingleton<IMyFrameSnapshotProvider, MyFrameSnapshotProvider>();
        builder.Services.AddSingleton<ISynchronizedDataReader>(_ =>
            new SqliteSynchronizedDataReader(MyFrameStoragePaths.DataDatabasePath));
        builder.Services.AddSingleton(p => new DashboardService(p.GetRequiredService<IAlecaFramePath>(),
            p.GetRequiredService<IAlecaFrameReader>(), p.GetRequiredService<IAlecaCatalogReader>(),
            p.GetRequiredService<IWarframeMarketClient>(), p.GetRequiredService<IPriceCache>(),
            p.GetRequiredService<IMarketStateStore>(), p.GetRequiredService<IMarketItemIndexStore>(),
            p.GetRequiredService<IRecommendationEngine>(),
            p.GetRequiredService<ILogger<DashboardService>>(),
            p.GetRequiredService<IMyFrameSnapshotProvider>()));
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<MainPage>();
        var app = builder.Build();
        StartupDiagnostics.Track("MauiProgram.End");
        return app;
    }
}
