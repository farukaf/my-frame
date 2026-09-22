namespace MyFrame.Core;

public enum AlecaFrameChangeKind
{
    Inventory,
    Token,
    Catalog,
    WatcherError,
    Directory
}

public sealed record AlecaFrameChange(AlecaFrameChangeKind Kind, string DirectoryPath);
