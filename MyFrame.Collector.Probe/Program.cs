using System.Text.Json;
using MyFrame.Core;

if (args.Length != 2 || args[0] != "--marker")
{
    Console.Error.WriteLine("Usage: MyFrame.Collector.Probe --marker <explicit .ready.json path>");
    return 2;
}
try
{
    var result = await CollectorCaptureReader.ReadAsync(args[1]);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine("Capture rejected: invalid, incomplete, inaccessible or unsupported. No data published.");
    return 1;
}
