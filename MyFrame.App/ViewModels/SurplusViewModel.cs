using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFrame.Core;

namespace MyFrame.App;

public partial class SurplusViewModel : ObservableObject
{
    private IReadOnlyList<SurplusRecommendation> _all = [];

    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool ShowMastered { get; set; } = true;
    [ObservableProperty] public partial bool ShowCrafted { get; set; } = true;
    [ObservableProperty] public partial bool ShowOnlyOneNeeded { get; set; } = true;
    [ObservableProperty] public partial string Platinum { get; set; } = "Any";
    [ObservableProperty] public partial string Summary { get; set; } = "0 spare";
    [ObservableProperty] public partial string EmptyMessage { get; set; } =
        "Nothing spare. Every part you hold still has something to build.";

    public ObservableCollection<SurplusRecommendation> Items { get; } = [];

    public void Apply(DashboardSnapshot snapshot)
    {
        _all = snapshot.Recommendations.Surplus;
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowMasteredChanged(bool value) => ApplyFilter();
    partial void OnShowCraftedChanged(bool value) => ApplyFilter();
    partial void OnShowOnlyOneNeededChanged(bool value) => ApplyFilter();
    partial void OnPlatinumChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void ToggleFilter(string filter)
    {
        switch (filter)
        {
            case "Mastered": ShowMastered = !ShowMastered; break;
            case "Crafted": ShowCrafted = !ShowCrafted; break;
            case "OnlyOneNeeded": ShowOnlyOneNeeded = !ShowOnlyOneNeeded; break;
        }
    }

    [RelayCommand]
    private void SetPlatinum(string value) => Platinum = value;

    [RelayCommand]
    private void ResetFilters() => (ShowMastered, ShowCrafted, ShowOnlyOneNeeded, Platinum) =
        (true, true, true, "Any");

    private void ApplyFilter()
    {
        var values = _all.Where(x => ReasonIsTicked(x.Reason) && PlatinumAdmits(x));
        if (!string.IsNullOrWhiteSpace(SearchText))
            values = values.Where(x => Matches(SearchText, x.ItemName, x.ParentName, x.Category,
                x.ReasonBadge, x.Explanation));

        var listed = values.ToArray();
        var platinum = listed.Sum(x => (long)(x.TotalPlatinum ?? 0));
        Summary = $"{listed.Sum(x => (long)x.Surplus):N0} spare" +
            (platinum > 0 ? $" · ~{platinum:N0}p" : "");
        EmptyMessage = !ShowMastered && !ShowCrafted && !ShowOnlyOneNeeded
            ? "No reason is ticked, so nothing can match."
            : _all.Count == 0
                ? "Nothing spare. Every part you hold still has something to build."
                : "No spare part matches the current filters.";
        Replace(Items, listed.Take(200));
    }

    private bool ReasonIsTicked(SurplusReason reason) => reason switch
    {
        SurplusReason.Mastered => ShowMastered,
        SurplusReason.Crafted => ShowCrafted,
        _ => ShowOnlyOneNeeded
    };

    private bool PlatinumAdmits(SurplusRecommendation row) => Platinum switch
    {
        "Has value" => row.SellableForPlatinum,
        "No value" => !row.SellableForPlatinum,
        _ => true
    };

    private static bool Matches(string search, params string[] values) =>
        values.Any(x => x.Contains(search, StringComparison.OrdinalIgnoreCase));

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }
}
