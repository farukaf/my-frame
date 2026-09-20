using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MyFrame.Core;
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
builder.Services.AddSingleton<IMyFrameSettingsStore>(_ =>
    new JsonMyFrameSettingsStore(MyFrameStoragePaths.SettingsPath));
builder.Services.AddSingleton<JsonPriceCache>(_ => new(MyFrameStoragePaths.PriceCachePath));
builder.Services.AddSingleton<IReadOnlyPriceCache>(provider => provider.GetRequiredService<JsonPriceCache>());
builder.Services.AddSingleton<IMarketStateStore>(_ => new MarketStateStore(MyFrameStoragePaths.MarketStatePath));
builder.Services.AddSingleton<IMarketItemIndexStore>(_ => new MarketItemIndexStore(MyFrameStoragePaths.MarketItemIndexPath));
builder.Services.AddSingleton<IMyFrameSnapshotProvider, MyFrameSnapshotProvider>();
builder.Services.AddSingleton<CursorCodec>();
builder.Services.AddSingleton<MyFrameQueryService>();
builder.Services.AddSingleton<QueryExecutionGate>();
builder.Services.AddSingleton<PlatformStatusService>();
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
