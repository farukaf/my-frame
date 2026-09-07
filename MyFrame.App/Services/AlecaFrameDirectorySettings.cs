namespace MyFrame.App;

public sealed class AlecaFrameDirectorySettings(string automaticDirectory)
{
    public const string PreferenceKey = "AlecaFrameDirectory";
    public string AutomaticDirectory { get; } = automaticDirectory;

    public static string? ValidationError(string directory)
    {
        if (!Directory.Exists(directory)) return "The selected folder does not exist.";
        if (!File.Exists(Path.Combine(directory, "lastData.dat"))) return "This folder does not contain lastData.dat.";
        var jsonDirectory = Path.Combine(directory, "cachedData", "json");
        if (!Directory.Exists(jsonDirectory) || !Directory.EnumerateFiles(jsonDirectory, "*.json").Any())
            return "This folder does not contain cachedData/json catalog files.";
        return null;
    }
}
