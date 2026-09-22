using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class FileSystemAlecaFrameChangeMonitorTests
{
    [Fact]
    public async Task RelevantFileChangesAreDebouncedAndClassified()
    {
        using var directory = new TemporaryDirectory();
        using var monitor = new FileSystemAlecaFrameChangeMonitor();
        var changes = new List<AlecaFrameChange>();
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, change) =>
        {
            lock (changes) changes.Add(change);
            signal.TrySetResult();
        };
        monitor.Watch(directory.Path);

        await File.WriteAllTextAsync(Path.Combine(directory.Path, "catalog.json"), "{}");
        await File.AppendAllTextAsync(Path.Combine(directory.Path, "catalog.json"), " ");
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.Single(changes);
        Assert.Equal(AlecaFrameChangeKind.Catalog, changes[0].Kind);
    }

    [Fact]
    public async Task NotifyUsesTheSameDebouncePathAsFileEvents()
    {
        using var directory = new TemporaryDirectory();
        using var monitor = new FileSystemAlecaFrameChangeMonitor();
        var signal = new TaskCompletionSource<AlecaFrameChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, change) => signal.TrySetResult(change);
        monitor.Watch(directory.Path);

        monitor.Notify(AlecaFrameChangeKind.Directory);

        var change = await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AlecaFrameChangeKind.Directory, change.Kind);
        Assert.Equal(directory.Path, change.DirectoryPath);
    }
}
