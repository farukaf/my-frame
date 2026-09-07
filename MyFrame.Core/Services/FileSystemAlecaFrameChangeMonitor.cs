using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MyFrame.Core;

public sealed class FileSystemAlecaFrameChangeMonitor : IAlecaFrameChangeMonitor
{
    private readonly ILogger<FileSystemAlecaFrameChangeMonitor> _logger;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounce;
    private string _directory = "";

    public FileSystemAlecaFrameChangeMonitor(ILogger<FileSystemAlecaFrameChangeMonitor>? logger = null) =>
        _logger = logger ?? NullLogger<FileSystemAlecaFrameChangeMonitor>.Instance;

    public event EventHandler<AlecaFrameChange>? Changed;

    public void Watch(string directory)
    {
        lock (_gate)
        {
            _directory = directory;
            _watcher?.Dispose();
            _watcher = null;
            if (!Directory.Exists(directory)) return;
            _watcher = new FileSystemWatcher(directory)
            {
                Filter = "*.*", IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Error += OnWatcherError;
        }
    }

    public void Notify(AlecaFrameChangeKind kind) => Schedule(kind);

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.FullPath);
        var kind = name.Equals("lastData.dat", StringComparison.OrdinalIgnoreCase)
            ? AlecaFrameChangeKind.Inventory
            : name.Equals("WFMarketToken.tk", StringComparison.OrdinalIgnoreCase)
                ? AlecaFrameChangeKind.Token
                : name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? AlecaFrameChangeKind.Catalog : (AlecaFrameChangeKind?)null;
        if (kind is not null) Schedule(kind.Value);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "AlecaFrame file watcher failed; recreating it");
        Watch(_directory);
        Schedule(AlecaFrameChangeKind.WatcherError);
    }

    private void Schedule(AlecaFrameChangeKind kind)
    {
        CancellationToken token;
        lock (_gate)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = new CancellationTokenSource();
            token = _debounce.Token;
        }
        _ = PublishAfterDelayAsync(kind, token);
    }

    private async Task PublishAfterDelayAsync(AlecaFrameChangeKind kind, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(750, cancellationToken).ConfigureAwait(false);
            Changed?.Invoke(this, new AlecaFrameChange(kind, _directory));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { _logger.LogWarning(error, "AlecaFrame change notification failed"); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _watcher?.Dispose();
            _watcher = null;
            _debounce?.Cancel();
            _debounce?.Dispose();
        }
    }
}
