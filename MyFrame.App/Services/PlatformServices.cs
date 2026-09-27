namespace MyFrame.App;

public interface IFolderPicker
{
    Task<string?> PickAsync();
}

public sealed class MauiFolderPicker : IFolderPicker
{
    public Task<string?> PickAsync() => AlecaFrameFolderPicker.PickAsync();
}
