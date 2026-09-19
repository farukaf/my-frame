using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MyFrame.Core;

namespace MyFrame.App;

public partial class DashboardSectionViewModel : ObservableObject
{
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial string TotalPlatinum { get; set; } = "0p";
    [ObservableProperty] public partial string TotalDucats { get; set; } = "0 ducats";
    [ObservableProperty] public partial string MasteryProgress { get; set; } = "0%";
    [ObservableProperty] public partial string InventorySummary { get; set; } = "0 items";
    public ISeries[] ValueSeries { get; private set; } = [];
    public ISeries[] ProgressSeries { get; private set; } = [];
    public void Apply(DashboardSnapshot snapshot)
    {
        var result = snapshot.Recommendations;
        TotalPlatinum = $"{result.EstimatedPlatinum:N0}p";
        TotalDucats = $"{result.TotalDucats:N0} ducats";
        var mastered = result.Collection.Count(x => x.Mastered);
        var total = result.Collection.Count;
        MasteryProgress = total == 0 ? "0%" : $"{(double)mastered / total:P0}";
        InventorySummary = $"{snapshot.Inventory.Stackables.Count:N0} stacks · {snapshot.Inventory.OwnedEquipment.Count:N0} equipment";
        ValueSeries = [new PieSeries<double> { Name = "Platinum", Values = [result.EstimatedPlatinum] }, new PieSeries<double> { Name = "Ducats ÷ 10", Values = [result.TotalDucats / 10d] }];
        ProgressSeries = [new ColumnSeries<double> { Name = "Mastered", Values = [mastered] }, new ColumnSeries<double> { Name = "Pending", Values = [Math.Max(0, total - mastered)] }];
        OnPropertyChanged(nameof(ValueSeries)); OnPropertyChanged(nameof(ProgressSeries));
    }
}
