using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MyFrame.Core;

namespace MyFrame.App;

public partial class RelicsViewModel : ObservableObject
{
    private IReadOnlyList<RelicRecommendation> _all = [];
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    public ObservableCollection<RelicRecommendation> Items { get; } = [];
    public void Apply(DashboardSnapshot snapshot) { _all = snapshot.Recommendations.Relics; ApplyFilter(); }
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    private void ApplyFilter() => Replace(Items, DashboardFilters.FilterRelics(_all, SearchText));
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
}
