namespace MyFrame.App.Components;

public partial class RecommendationCard : ContentView
{
    public static readonly BindableProperty ImageUrlProperty = Property(nameof(ImageUrl), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty TitleProperty = Property(nameof(Title), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty BadgeProperty = Property(nameof(Badge), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty MetadataProperty = Property(nameof(Metadata), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty ActionProperty = Property(nameof(Action), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty DescriptionProperty = Property(nameof(Description), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty ProgressProperty = Property(nameof(Progress), typeof(double), typeof(RecommendationCard), 0d);
    public static readonly BindableProperty ShowProgressProperty = Property(nameof(ShowProgress), typeof(bool), typeof(RecommendationCard), false);
    public static readonly BindableProperty IsMasteredProperty = Property(nameof(IsMastered), typeof(bool), typeof(RecommendationCard), false);
    public static readonly BindableProperty DetailsProperty = Property(nameof(Details), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty ShowDetailsButtonProperty = Property(nameof(ShowDetailsButton), typeof(bool), typeof(RecommendationCard), false);
    public static readonly BindableProperty MarketSlugProperty = Property(nameof(MarketSlug), typeof(string), typeof(RecommendationCard), "");

    public RecommendationCard() => InitializeComponent();

    public string ImageUrl { get => (string)GetValue(ImageUrlProperty); set => SetValue(ImageUrlProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Badge { get => (string)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public string Metadata { get => (string)GetValue(MetadataProperty); set => SetValue(MetadataProperty, value); }
    public string Action { get => (string)GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool ShowProgress { get => (bool)GetValue(ShowProgressProperty); set => SetValue(ShowProgressProperty, value); }
    public bool IsMastered { get => (bool)GetValue(IsMasteredProperty); set => SetValue(IsMasteredProperty, value); }
    public string Details { get => (string)GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
    public bool ShowDetailsButton { get => (bool)GetValue(ShowDetailsButtonProperty); set => SetValue(ShowDetailsButtonProperty, value); }
    public string MarketSlug { get => (string)GetValue(MarketSlugProperty); set => SetValue(MarketSlugProperty, value); }

    private async void OpenDetails(object? sender, EventArgs e)
    {
        var page = new ContentPage
        {
            Title = Title,
            BackgroundColor = Color.FromArgb("#0B0E14")
        };
        var close = new Button { Text = "Close", BackgroundColor = Color.FromArgb("#20283A"), TextColor = Colors.White };
        close.Clicked += async (_, _) => await page.Navigation.PopModalAsync();
        var wiki = new Button { Text = "Open Wiki ↗", BackgroundColor = Color.FromArgb("#167C70"), TextColor = Colors.White };
        wiki.Clicked += async (_, _) => await Browser.Default.OpenAsync(WikiUrl(Title), BrowserLaunchMode.SystemPreferred);
        var market = new Button { Text = "Open Warframe.Market ↗", BackgroundColor = Color.FromArgb("#7D3CFF"), TextColor = Colors.White,
            IsVisible = !string.IsNullOrWhiteSpace(MarketSlug) };
        market.Clicked += async (_, _) => await Browser.Default.OpenAsync($"https://warframe.market/items/{Uri.EscapeDataString(MarketSlug)}", BrowserLaunchMode.SystemPreferred);

        page.Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(28), Spacing = 14, MaximumWidthRequest = 720,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = Title, TextColor = Color.FromArgb("#F4F7FB"), FontSize = 26, FontAttributes = FontAttributes.Bold },
                    new Label { Text = Badge, TextColor = Color.FromArgb("#F3C969") },
                    Section("Summary", Metadata),
                    Section("Recommendation", Action),
                    Section("Why", Description),
                    Section("Inventory and mastery", string.IsNullOrWhiteSpace(Details) ? "No additional inventory or mastery information is available for this entry." : Details),
                    new HorizontalStackLayout { Spacing = 10, Children = { wiki, market, close } }
                }
            }
        };
        await Navigation.PushModalAsync(page);
    }

    private static Label Section(string heading, string value) => new()
    {
        Text = $"{heading}\n{(string.IsNullOrWhiteSpace(value) ? "—" : value)}",
        TextColor = Color.FromArgb("#AEB8C8"), LineBreakMode = LineBreakMode.WordWrap
    };

    private static string WikiUrl(string title) =>
        $"https://wiki.warframe.com/w/{Uri.EscapeDataString(title.Replace(' ', '_'))}";

    private static BindableProperty Property(string name, Type type, Type owner, object defaultValue) =>
        BindableProperty.Create(name, type, owner, defaultValue);
}
