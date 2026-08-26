using LiveChartsCore.SkiaSharpView.Maui;
using Microsoft.Extensions.Logging;
using MyFrame.Core;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace MyFrame.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().UseSkiaSharp().UseLiveCharts().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });
#if DEBUG
        builder.Logging.AddDebug();
#endif
        var alecaDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlecaFrame");
        builder.Services.AddSingleton<IAlecaFrameReader, AlecaFrameReader>();
        builder.Services.AddSingleton<IAlecaCatalogReader, AlecaCatalogReader>();
        builder.Services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
        builder.Services.AddSingleton<IPriceCache>(_ => new JsonPriceCache(Path.Combine(FileSystem.Current.AppDataDirectory, "market-quotes.json")));
        builder.Services.AddSingleton<IWarframeMarketClient>(_ => new WarframeMarketClient(new HttpClient(), Path.Combine(alecaDirectory, "WFMarketToken.tk")));
        builder.Services.AddSingleton(p => new DashboardService(alecaDirectory, p.GetRequiredService<IAlecaFrameReader>(), p.GetRequiredService<IAlecaCatalogReader>(), p.GetRequiredService<IWarframeMarketClient>(), p.GetRequiredService<IPriceCache>(), p.GetRequiredService<IRecommendationEngine>()));
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
