namespace MyFrame.App;

public static class AlecaFrameFolderPicker
{
    public static async Task<string?> PickAsync()
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView;
        if (window is null) return null;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        return (await picker.PickSingleFolderAsync())?.Path;
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
