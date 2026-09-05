using Microsoft.Maui.Controls.Shapes;
using MyFrame.Core;

namespace MyFrame.App.Components;

public partial class RecommendationCard : ContentView
{
    public static readonly BindableProperty ImageUrlProperty = Property(nameof(ImageUrl), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty TitleProperty = Property(nameof(Title), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty BadgeProperty = Property(nameof(Badge), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty OwnedBadgeProperty = Property(nameof(OwnedBadge), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty MetadataProperty = Property(nameof(Metadata), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty ActionProperty = Property(nameof(Action), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty DescriptionProperty = Property(nameof(Description), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty ProgressProperty = Property(nameof(Progress), typeof(double), typeof(RecommendationCard), 0d);
    public static readonly BindableProperty ShowProgressProperty = Property(nameof(ShowProgress), typeof(bool), typeof(RecommendationCard), false);
    public static readonly BindableProperty IsMasteredProperty = Property(nameof(IsMastered), typeof(bool), typeof(RecommendationCard), false);
    public static readonly BindableProperty DetailsProperty = Property(nameof(Details), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty ShowDetailsButtonProperty = Property(nameof(ShowDetailsButton), typeof(bool), typeof(RecommendationCard), false);
    public static readonly BindableProperty MarketSlugProperty = Property(nameof(MarketSlug), typeof(string), typeof(RecommendationCard), "");
    public static readonly BindableProperty ComponentsProperty = Property(nameof(Components), typeof(IReadOnlyList<CollectionComponentDetail>), typeof(RecommendationCard), null!);
    public static readonly BindableProperty IsOwnedProperty = Property(nameof(IsOwned), typeof(bool), typeof(RecommendationCard), false);

    public RecommendationCard() => InitializeComponent();

    public string ImageUrl { get => (string)GetValue(ImageUrlProperty); set => SetValue(ImageUrlProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Badge { get => (string)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public string OwnedBadge { get => (string)GetValue(OwnedBadgeProperty); set => SetValue(OwnedBadgeProperty, value); }
    public string Metadata { get => (string)GetValue(MetadataProperty); set => SetValue(MetadataProperty, value); }
    public string Action { get => (string)GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool ShowProgress { get => (bool)GetValue(ShowProgressProperty); set => SetValue(ShowProgressProperty, value); }
    public bool IsMastered { get => (bool)GetValue(IsMasteredProperty); set => SetValue(IsMasteredProperty, value); }
    public string Details { get => (string)GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
    public bool ShowDetailsButton { get => (bool)GetValue(ShowDetailsButtonProperty); set => SetValue(ShowDetailsButtonProperty, value); }
    public string MarketSlug { get => (string)GetValue(MarketSlugProperty); set => SetValue(MarketSlugProperty, value); }
    public IReadOnlyList<CollectionComponentDetail>? Components { get => (IReadOnlyList<CollectionComponentDetail>?)GetValue(ComponentsProperty); set => SetValue(ComponentsProperty, value); }
    public bool IsOwned { get => (bool)GetValue(IsOwnedProperty); set => SetValue(IsOwnedProperty, value); }

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

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(28), Spacing = 14, MaximumWidthRequest = 760,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                BuildHero(),
                Section("Recommendation", Action),
                Section("Why", Description),
                Section("Inventory and mastery", string.IsNullOrWhiteSpace(Details) ? "No additional inventory or mastery information is available for this entry." : Details)
            }
        };
        if (Components is { Count: > 0 })
        {
            content.Children.Add(new Label { Text = "Components", TextColor = Color.FromArgb("#F4F7FB"), FontSize = 18, FontAttributes = FontAttributes.Bold });
            content.Children.Add(BuildComponents(Components));
        }
        content.Children.Add(new HorizontalStackLayout { Spacing = 10, Children = { wiki, market, close } });
        page.Content = new ScrollView { Content = content };
        await Navigation.PushModalAsync(page);
    }

    private Grid BuildHero()
    {
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(new GridLength(180)), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 20 };
        var imageFrame = new Border
        {
            WidthRequest = 170, HeightRequest = 170, Padding = 12, BackgroundColor = Color.FromArgb("#101722"),
            Stroke = Color.FromArgb("#303B50"), StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new Image { Source = ImageUrl, Aspect = Aspect.AspectFit }
        };
        var info = new VerticalStackLayout
        {
            Spacing = 7, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = Title, TextColor = Color.FromArgb("#F4F7FB"), FontSize = 26, FontAttributes = FontAttributes.Bold },
                new Label { Text = Badge, TextColor = Color.FromArgb("#F3C969") },
                new Label
                {
                    Text = string.IsNullOrWhiteSpace(OwnedBadge) ? Metadata : $"{OwnedBadge} · {Metadata}",
                    TextColor = Color.FromArgb("#AEB8C8")
                },
                new Label
                {
                    Text = Components is { Count: > 0 }
                        ? $"{(IsOwned ? "✓ Owned" : "Not currently owned")} · {(IsMastered ? "✦ Mastered" : "Mastery pending")}" : "",
                    TextColor = IsOwned || IsMastered ? Color.FromArgb("#7DE2D1") : Color.FromArgb("#8995A8")
                }
            }
        };
        grid.Children.Add(imageFrame);
        grid.Children.Add(info);
        Grid.SetColumn(info, 1);
        return grid;
    }

    private static Grid BuildComponents(IReadOnlyList<CollectionComponentDetail> components)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (var column = 0; column < 4; column++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            var card = new Border
            {
                Padding = 9, BackgroundColor = Color.FromArgb("#101722"),
                Stroke = Color.FromArgb(component.Complete ? "#2E8B57" : "#303B50"),
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new VerticalStackLayout
                {
                    Spacing = 4, HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Image { Source = component.ImageUrl, WidthRequest = 54, HeightRequest = 54, Aspect = Aspect.AspectFit },
                        new Label { Text = component.Name, TextColor = Color.FromArgb("#D6DCE6"), FontSize = 11, HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = $"{component.Owned:N0}/{component.Required:N0}", TextColor = Color.FromArgb(component.Complete ? "#7DE2D1" : "#F3C969"), FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }
                    }
                }
            };
            grid.Children.Add(card);
            Grid.SetColumn(card, index % 4);
            Grid.SetRow(card, index / 4);
        }
        return grid;
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
