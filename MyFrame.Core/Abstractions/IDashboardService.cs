namespace MyFrame.Core;

public interface IDashboardService : IDisposable
{
    event EventHandler<DashboardSnapshot>? SnapshotUpdated;
    event EventHandler<SyncStatus>? SyncProgressChanged;
    DashboardSnapshot? LastSnapshot { get; }
    Task<DashboardSnapshot> RefreshAsync(bool refreshPrices = true,
        RecommendationSettings? settings = null, CancellationToken cancellationToken = default);
    DashboardSnapshot? Reapply(RecommendationSettings settings);
}
