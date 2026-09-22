namespace MyFrame.App;

public interface IFolderPicker
{
    Task<string?> PickAsync();
}

public sealed class MauiFolderPicker : IFolderPicker
{
    public Task<string?> PickAsync() => AlecaFrameFolderPicker.PickAsync();
}

public interface IExternalBrowser
{
    Task<bool> OpenAsync(string uri);
}

public sealed class MauiExternalBrowser : IExternalBrowser
{
    public Task<bool> OpenAsync(string uri) => Launcher.Default.OpenAsync(uri);
}
