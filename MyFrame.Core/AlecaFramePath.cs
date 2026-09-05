namespace MyFrame.Core;

public sealed class AlecaFramePath(string directoryPath) : IAlecaFramePath
{
    private string _directoryPath = Normalize(directoryPath);

    public string DirectoryPath => _directoryPath;
    public event EventHandler<string>? Changed;

    public void SetDirectory(string directoryPath)
    {
        var normalized = Normalize(directoryPath);
        if (string.Equals(_directoryPath, normalized, StringComparison.OrdinalIgnoreCase)) return;
        _directoryPath = normalized;
        Changed?.Invoke(this, normalized);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("AlecaFrame directory is required.", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
