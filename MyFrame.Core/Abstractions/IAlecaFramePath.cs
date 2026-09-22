namespace MyFrame.Core;

public interface IAlecaFramePath
{
    string DirectoryPath { get; }
    event EventHandler<string>? Changed;
    void SetDirectory(string directoryPath);
}
