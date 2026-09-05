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

    public RecommendationCard() => InitializeComponent();

    public string ImageUrl { get => (string)GetValue(ImageUrlProperty); set => SetValue(ImageUrlProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Badge { get => (string)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public string Metadata { get => (string)GetValue(MetadataProperty); set => SetValue(MetadataProperty, value); }
    public string Action { get => (string)GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool ShowProgress { get => (bool)GetValue(ShowProgressProperty); set => SetValue(ShowProgressProperty, value); }

    private static BindableProperty Property(string name, Type type, Type owner, object defaultValue) =>
        BindableProperty.Create(name, type, owner, defaultValue);
}
