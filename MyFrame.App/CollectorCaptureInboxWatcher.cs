namespace MyFrame.App;

public sealed class CollectorCaptureInboxWatcher : IDisposable
{
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounce;

    public event EventHandler? CaptureDetected;

    public void Start()
    {
        lock (_gate)
        {
            if (_watcher is not null) return;
            var directory = MyFrame.Core.MyFrameStoragePaths.CollectorCaptureDirectory;
            Directory.CreateDirectory(directory);
            _watcher = new FileSystemWatcher(directory, "*.ready.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => ScheduleNotification();
    private void OnRenamed(object sender, RenamedEventArgs args) => ScheduleNotification();
    private void OnError(object sender, ErrorEventArgs args) => ScheduleNotification();

    private void ScheduleNotification()
    {
        CancellationToken token;
        lock (_gate)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = new CancellationTokenSource();
            token = _debounce.Token;
        }
        _ = NotifyAfterDelayAsync(token);
    }

    private async Task NotifyAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), token).ConfigureAwait(false);
            CaptureDetected?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = null;
            if (_watcher is null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnChanged;
            _watcher.Changed -= OnChanged;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
