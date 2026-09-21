using System.Text.Json;
using MyFrame.Core;
using MyFrame.Core.Sync;

var publicExport = args.Any(argument => string.Equals(argument, "--public-export", StringComparison.Ordinal));
var statusOnly = args.Any(argument => string.Equals(argument, "--status", StringComparison.Ordinal));
if (!publicExport && !statusOnly)
{
    Console.Error.WriteLine("Usage: MyFrame.Sync (--public-export | --status) [--data-root <path>]");
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

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
try
{
    var decoder = new LzmaAloneDecoder();
    var indexClient = new PublicExportIndexClient(httpClient, decoder.Decode);
    var documentClient = new PublicExportDocumentClient(httpClient, decoder);
    var entries = await indexClient.FetchIndexAsync();
    var entry = entries.FirstOrDefault(value =>
        value.RelativePath.Contains("ExportWeapons_en.json", StringComparison.OrdinalIgnoreCase))
        ?? entries.FirstOrDefault(value => value.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    if (entry is null) throw new InvalidDataException("PUBLIC_EXPORT_ENTRY_NOT_FOUND");
    var publication = await host.RunPublicExportOnceAsync("public-export", documentClient, entry);
    var status = await database.GetStatusAsync("public-export");
    var output = new
    {
        state = publication is null ? status?.LastRunState ?? "failed" : "published",
        records = publication?.RecordCount ?? 0,
        revisionId = publication?.RevisionId ?? status?.ActiveRevisionId,
        parserVersion = status?.ParserVersion,
        errorCode = publication is null ? status?.ErrorCode ?? "SYNC_FAILED" : null,
        source = entry.RelativePath,
        revisionTag = entry.RevisionTag
    };
    Console.WriteLine(JsonSerializer.Serialize(output));
    return publication is null ? 1 : 0;
}
catch (Exception error) when (error is HttpRequestException or InvalidDataException or NotSupportedException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
