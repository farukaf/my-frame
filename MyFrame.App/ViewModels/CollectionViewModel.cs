using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MyFrame.Core;

namespace MyFrame.App;

public partial class CollectionViewModel : ObservableObject
{
    private IReadOnlyList<CollectionGoal> _all = [];
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial string SelectedFilter { get; set; } = "In progress";
    [ObservableProperty] public partial string SelectedSort { get; set; } = "Closest to completion";
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    public ObservableCollection<CollectionGoal> Items { get; } = [];
    public IReadOnlyList<string> Filters { get; } = ["In progress", "All", "Not owned", "Owned", "Mastered", "Prime only"];
    public IReadOnlyList<string> Sorts { get; } = ["Closest to completion", "Name", "Category", "Least progress"];
    public void Apply(DashboardSnapshot snapshot) { _all = snapshot.Recommendations.Collection; ApplyFilter(); }
    partial void OnSelectedFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedSortChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    private void ApplyFilter() => Replace(Items, DashboardFilters.FilterCollection(_all, SelectedFilter, SelectedSort, SearchText));
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
}
