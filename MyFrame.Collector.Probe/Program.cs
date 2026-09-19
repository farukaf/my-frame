using System.Text.Json;
using MyFrame.Core;
using MyFrame.Core.Sync;

var isProbe = args.Length == 2 && args[0] == "--marker";
var isImport = args.Length == 6 && args[0] == "--import" && args[1] == "--marker" &&
               args[3] == "--database" && args[5] == "--allow-raw";
var isImportDirectory = args.Length == 6 && args[0] == "--import-directory" && args[1] == "--directory" &&
                        args[3] == "--database" && args[5] == "--allow-raw";
if (!isProbe && !isImport && !isImportDirectory)
{
    Console.Error.WriteLine("Usage: MyFrame.Collector.Probe --marker <explicit .ready.json path>");
    Console.Error.WriteLine("   or: MyFrame.Collector.Probe --import --marker <path> --database <data.db> --allow-raw");
    Console.Error.WriteLine("   or: MyFrame.Collector.Probe --import-directory --directory <folder> --database <data.db> --allow-raw");
    return 2;
}
try
{
    object result;
    if (isImport)
    {
        await using var database = new SyncDatabase(args[4]);
        result = await CollectorCaptureImporter.ImportAsync(args[2], database, allowRawPayload: true);
    }
    else if (isImportDirectory)
    {
        await using var database = new SyncDatabase(args[4]);
        result = await CollectorCaptureInbox.ImportAsync(args[2], database, allowRawPayload: true);
    }
    else
    {
        result = await CollectorCaptureReader.ReadAsync(args[1]);
    }
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine("Capture rejected: invalid, incomplete, inaccessible or unsupported. No data published.");
    return 1;
}
