namespace MyFrame.App.Tests;

public sealed class AppCompositionTests
{
    [Fact]
    public void RefactoredAppKeepsTheExpectedBoundaries()
    {
        Assert.True(Directory.Exists(AppPath("Pages")));
        Assert.True(Directory.Exists(AppPath("Components")));
        Assert.True(Directory.Exists(AppPath("Services")));
        Assert.True(Directory.Exists(AppPath("Repositories")));
        Assert.True(Directory.Exists(AppPath("ViewModels")));
        Assert.True(Directory.Exists(CorePath("Abstractions")));
        Assert.True(Directory.Exists(CorePath("Repositories")));
        Assert.True(Directory.Exists(CorePath("Services")));
        Assert.True(Directory.Exists(CorePath("Rules")));
    }

    [Fact]
    public void MainPageOnlyComposesTheSections()
    {
        var xaml = ReadAppFile(Path.Combine("Pages", "MainPage.xaml"));

        Assert.Contains("x:Class=\"MyFrame.App.MainPage\"", xaml);
        Assert.Contains("components:Sidebar", xaml);
        Assert.Contains("components:PageHeader", xaml);
        Assert.Contains("components:Dashboard", xaml);
        Assert.Contains("components:Collection", xaml);
        Assert.Contains("components:Farm", xaml);
        Assert.Contains("components:Relics", xaml);
        Assert.Contains("components:Sales", xaml);
        Assert.Contains("components:Settings", xaml);
        Assert.DoesNotContain("CollectionView", xaml);
    }

    [Fact]
    public void MainPageExposesStableAutomationIdsForTheNavigationAndRefreshSmokeFlow()
    {
        var xaml = ReadAppFile(Path.Combine("Components", "Sidebar.xaml"));
        var header = ReadAppFile(Path.Combine("Components", "PageHeader.xaml"));

        foreach (var id in new[] { "NavDashboard", "NavCollection", "NavFarm", "NavRelics", "NavSales", "NavSettings" })
            Assert.Contains($"AutomationId=\"{id}\"", xaml);
        Assert.Contains("AutomationId=\"RefreshButton\"", header);
    }

    [Fact]
    public void MauiCompositionRegistersThePageViewModelAndSettingsBoundary()
    {
        var source = ReadAppFile("MauiProgram.cs");

        Assert.Contains("AddSingleton<MainViewModel>", source);
        Assert.Contains("AddSingleton<MainPage>", source);
        Assert.Contains("AddSingleton<ISettingsStore>(preferences)", source);
        Assert.Contains("AddSingleton<IFolderPicker, MauiFolderPicker>", source);
        Assert.Contains("AddSingleton<IExternalBrowser, MauiExternalBrowser>", source);
    }

    [Fact]
    public void WindowsEntryPointInitializesVelopackBeforeStartingTheApplication()
    {
        var source = ReadAppFile(Path.Combine("Platforms", "Windows", "Program.cs"));

        var velopack = source.IndexOf("VelopackApp.Build().Run()", StringComparison.Ordinal);
        var application = source.IndexOf("Application.Start", StringComparison.Ordinal);

        Assert.True(velopack >= 0);
        Assert.True(application > velopack);
    }

    [Fact]
    public void RootFilesDoNotReintroduceThePreRefactorMonoliths()
    {
        Assert.False(File.Exists(AppPath("DashboardViewModel.cs")));
        Assert.False(File.Exists(AppPath("MainPage.xaml")));
        Assert.False(File.Exists(CorePath("DashboardService.cs")));
        Assert.False(File.Exists(CorePath("Models.cs")));
    }

    [Fact]
    public void EachPresentationComponentHasItsOwnMarkupAndCodeBehind()
    {
        foreach (var name in new[] { "Sidebar", "GlobalStatus", "PageHeader", "Dashboard", "Collection", "Farm", "Relics", "Sales", "Settings" })
        {
            var xaml = ReadAppFile(Path.Combine("Components", $"{name}.xaml"));
            Assert.Contains($"x:Class=\"MyFrame.App.Components.{name}\"", xaml);
            Assert.True(File.Exists(AppPath("Components", $"{name}.xaml.cs")), $"Missing code behind for {name}.");
        }
    }

    [Fact]
    public void EachSectionOwnsAViewModelAndTheCoordinatorDoesNotOwnSectionCollections()
    {
        foreach (var name in new[] { "Main", "DashboardSection", "Collection", "Farm", "Relics", "Sales", "Settings", "GlobalStatus" })
        {
            var source = ReadAppFile(Path.Combine("ViewModels", $"{name}ViewModel.cs"));
            Assert.Contains($"class {name}ViewModel", source);
        }

        var coordinator = ReadAppFile(Path.Combine("ViewModels", "MainViewModel.cs"));
        Assert.DoesNotContain("ObservableCollection", coordinator);
        Assert.DoesNotContain("DashboardFilters", coordinator);
    }

    private static string ReadAppFile(params string[] parts) => File.ReadAllText(AppPath(parts));

    private static string AppPath(params string[] parts) => Path.Combine(FindRoot(), "MyFrame.App", Path.Combine(parts));

    private static string CorePath(params string[] parts) => Path.Combine(FindRoot(), "MyFrame.Core", Path.Combine(parts));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "MyFrame.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
