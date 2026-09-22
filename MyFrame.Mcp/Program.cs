using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MyFrame.Core;
using MyFrame.Core.Sync;
using MyFrame.Mcp;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = false,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};
builder.Services.AddSingleton(json);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(MyFrameLocalDataOptions.Shared);
builder.Services.AddSingleton<IAlecaFrameReader, AlecaFrameReader>();
builder.Services.AddSingleton<IAlecaCatalogReader, AlecaCatalogReader>();
builder.Services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
builder.Services.AddSingleton<SqliteSettingsStore>(_ => new SqliteSettingsStore(
    MyFrameStoragePaths.DataDatabasePath, MyFrameStoragePaths.SettingsPath));
builder.Services.AddSingleton<IMyFrameSettingsStore>(provider =>
    provider.GetRequiredService<SqliteSettingsStore>());
builder.Services.AddSingleton(_ => new SqliteMarketStore(MyFrameStoragePaths.DataDatabasePath,
    MyFrameStoragePaths.PriceCachePath, MyFrameStoragePaths.MarketStatePath,
    MyFrameStoragePaths.MarketItemIndexPath));
builder.Services.AddSingleton<IReadOnlyPriceCache>(provider => provider.GetRequiredService<SqliteMarketStore>());
builder.Services.AddSingleton<IMarketStateStore>(provider => provider.GetRequiredService<SqliteMarketStore>());
builder.Services.AddSingleton<IMarketItemIndexStore>(provider => provider.GetRequiredService<SqliteMarketStore>());
builder.Services.AddSingleton<IMarketTokenStore>(_ =>
    OperatingSystem.IsWindows()
        ? new ProtectedFileMarketTokenStore(MyFrameStoragePaths.MarketTokenPath)
        : new FileMarketTokenStore(MyFrameStoragePaths.MarketTokenPath));
builder.Services.AddSingleton<IMyFrameSnapshotProvider, MyFrameSnapshotProvider>();
builder.Services.AddSingleton<ISynchronizedDataReader>(_ =>
    new SqliteSynchronizedDataReader(MyFrameStoragePaths.DataDatabasePath));
builder.Services.AddSingleton<CursorCodec>();
builder.Services.AddSingleton<MyFrameQueryService>();
builder.Services.AddSingleton<QueryExecutionGate>();
builder.Services.AddSingleton<PlatformStatusService>(provider =>
    new PlatformStatusService(provider.GetRequiredService<IMarketTokenStore>()));
builder.Services.AddTransient<MyFrameTools>();

var strictTools = StrictToolRegistration.Create(json);

builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "my-frame", Version = "1.0.0" };
        options.ServerInstructions = MyFrameResources.Instructions;
    })
    .WithStdioServerTransport()
    .WithTools(strictTools)
    .WithResources<MyFrameResources>()
    .WithPrompts<MyFramePrompts>();

await builder.Build().RunAsync();
