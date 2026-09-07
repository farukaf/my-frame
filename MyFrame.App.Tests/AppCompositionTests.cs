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
    public void MainPageComposesTheDashboardAndKeepsTheExistingSections()
    {
        var xaml = ReadAppFile(Path.Combine("Pages", "MainPage.xaml"));

        Assert.Contains("x:Class=\"MyFrame.App.MainPage\"", xaml);
        Assert.Contains("DashboardVisible", xaml);
        Assert.Contains("CollectionVisible", xaml);
        Assert.Contains("FarmVisible", xaml);
        Assert.Contains("SalesVisible", xaml);
        Assert.Contains("RelicsVisible", xaml);
        Assert.Contains("SettingsVisible", xaml);
        Assert.Contains("components:RecommendationCard", xaml);
    }

    [Fact]
    public void MainPageExposesStableAutomationIdsForTheNavigationAndRefreshSmokeFlow()
    {
        var xaml = ReadAppFile(Path.Combine("Pages", "MainPage.xaml"));

        foreach (var id in new[] { "NavDashboard", "NavCollection", "NavFarm", "NavRelics", "NavSales", "NavSettings", "RefreshButton" })
            Assert.Contains($"AutomationId=\"{id}\"", xaml);
    }

    [Fact]
    public void MauiCompositionRegistersThePageViewModelAndSettingsBoundary()
    {
        var source = ReadAppFile("MauiProgram.cs");

        Assert.Contains("AddSingleton<MainViewModel>", source);
        Assert.Contains("AddSingleton<MainPage>", source);
        Assert.Contains("AddSingleton<ISettingsStore, MauiAppPreferences>", source);
    }

    [Fact]
    public void RootFilesDoNotReintroduceThePreRefactorMonoliths()
    {
        Assert.False(File.Exists(AppPath("DashboardViewModel.cs")));
        Assert.False(File.Exists(AppPath("MainPage.xaml")));
        Assert.False(File.Exists(CorePath("DashboardService.cs")));
        Assert.False(File.Exists(CorePath("Models.cs")));
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
