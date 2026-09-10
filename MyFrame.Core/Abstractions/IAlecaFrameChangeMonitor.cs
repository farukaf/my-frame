namespace MyFrame.Core;

public interface IAlecaFrameChangeMonitor : IDisposable
{
    event EventHandler<AlecaFrameChange>? Changed;
    void Watch(string directory);
    void Notify(AlecaFrameChangeKind kind);
}
