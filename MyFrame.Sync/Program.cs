using System.Text.Json;
using MyFrame.Core;
using MyFrame.Core.Sync;

var publicExport = args.Any(argument => string.Equals(argument, "--public-export", StringComparison.Ordinal));
var worldState = args.Any(argument => string.Equals(argument, "--world-state", StringComparison.Ordinal));
var statusOnly = args.Any(argument => string.Equals(argument, "--status", StringComparison.Ordinal));
var allSources = args.Any(argument => string.Equals(argument, "--all", StringComparison.Ordinal));
if ((publicExport ? 1 : 0) + (worldState ? 1 : 0) + (statusOnly ? 1 : 0) + (allSources ? 1 : 0) != 1)
{
    Console.Error.WriteLine("Usage: MyFrame.Sync (--public-export | --world-state | --all | --status) [--data-root <path>]");
    return 2;
}

var dataRootIndex = Array.FindIndex(args, argument => string.Equals(argument, "--data-root", StringComparison.Ordinal));
if (dataRootIndex >= 0)
{
    if (dataRootIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[dataRootIndex + 1]))
    {
        Console.Error.WriteLine("--data-root requires a path.");
        return 2;
    }
    Environment.SetEnvironmentVariable("MYFRAME_DATA_ROOT", args[dataRootIndex + 1]);
}

await using var database = new SyncDatabase(MyFrameStoragePaths.DataDatabasePath);
await using var host = new SyncHost(database);
if (statusOnly)
{
    var statuses = await Task.WhenAll(new[] { "public-export", "worldstate-pc", "overwolf-inventory" }
        .Select(async source => new { source, status = await database.GetStatusAsync(source) }));
    Console.WriteLine(JsonSerializer.Serialize(new { dataRoot = MyFrameStoragePaths.RootDirectory, statuses }));
    return 0;
}

if (worldState)
{
    using var worldStateClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var result = await new WorldStateSyncRunner().RunAsync(database, host, worldStateClient);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        state = result.State,
        records = result.Records,
        revisionId = result.RevisionId,
        parserVersion = result.ParserVersion,
        errorCode = result.ErrorCode,
        source = "worldstate-pc"
    }));
    return result.State == "published" ? 0 : 1;
}

if (allSources)
{
    using var worldStateClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    using var publicExportClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    var world = await new WorldStateSyncRunner().RunAsync(database, host, worldStateClient);
    var catalog = await new PublicExportSyncRunner().RunAsync(database, host, publicExportClient);
    var success = world.State == "published" && catalog.State == "published";
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        state = success ? "published" : "partial-failure",
        sources = new object[]
        {
            new { source = world.Source, state = world.State, records = world.Records, revisionId = world.RevisionId, parserVersion = world.ParserVersion, errorCode = world.ErrorCode },
            new { source = "public-export", state = catalog.State, records = catalog.Records, revisionId = catalog.RevisionId, parserVersion = catalog.ParserVersion, errorCode = catalog.ErrorCode, path = catalog.RelativePath, revisionTag = catalog.RevisionTag }
        }
    }));
    return success ? 0 : 1;
}

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
try
{
    var result = await new PublicExportSyncRunner().RunAsync(database, host, httpClient);
    var output = new
    {
        state = result.State,
        records = result.Records,
        revisionId = result.RevisionId,
        parserVersion = result.ParserVersion,
        errorCode = result.ErrorCode,
        source = result.RelativePath,
        revisionTag = result.RevisionTag
    };
    Console.WriteLine(JsonSerializer.Serialize(output));
    return result.State == "published" ? 0 : 1;
}
catch (Exception error) when (error is HttpRequestException or InvalidDataException or NotSupportedException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
