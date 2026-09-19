using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFrame.Core;

namespace MyFrame.App;

public partial class SalesViewModel : ObservableObject
{
    private readonly DashboardSettingsState _settings;
    private IReadOnlyList<SaleRecommendation> _all = [];
    public SalesViewModel(DashboardSettingsState settings) { _settings = settings; _settings.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(DashboardSettingsState.DucatsPerPlatinum)) OnPropertyChanged(nameof(DucatsPerPlatinum)); }; }
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial string SelectedFilter { get; set; } = "All recommendations";
    [ObservableProperty] public partial string SelectedSort { get; set; } = "Name";
    [ObservableProperty] public partial bool IncludeVaultedParts { get; set; } = true;
    [ObservableProperty] public partial string FilteredDucatsEstimate { get; set; } = "0";
    public double DucatsPerPlatinum { get => _settings.DucatsPerPlatinum; set => _settings.DucatsPerPlatinum = value; }
    public ObservableCollection<SaleRecommendation> Items { get; } = [];
    public IReadOnlyList<string> Filters { get; } = ["All recommendations", "Keep", "Platinum", "Ducats", "Existing orders", "Vaulted items"];
    public IReadOnlyList<string> Sorts { get; } = ["Name", "Action", "Highest value"];
    public void Apply(DashboardSnapshot snapshot) { _all = snapshot.Recommendations.Sales; ApplyFilter(); }
    partial void OnSearchTextChanged(string value) => ApplyFilter(); partial void OnSelectedFilterChanged(string value) => ApplyFilter(); partial void OnSelectedSortChanged(string value) => ApplyFilter(); partial void OnIncludeVaultedPartsChanged(bool value) => ApplyFilter();
    [RelayCommand] private void ToggleVaultedParts() => IncludeVaultedParts = !IncludeVaultedParts;
    private void ApplyFilter() { var view = DashboardFilters.FilterSales(_all, SelectedFilter, SelectedSort, SearchText, IncludeVaultedParts); FilteredDucatsEstimate = view.DucatsEstimate; Replace(Items, view.Items); }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
}
