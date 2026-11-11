namespace BillMate.App.Controls;

public partial class EmptyStateView : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(EmptyStateView), "No records yet",
        propertyChanged: (b, _, v) => ((EmptyStateView)b).TitleLabel.Text = v?.ToString());

    public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(nameof(Description), typeof(string), typeof(EmptyStateView), "Items you create will appear here.",
        propertyChanged: (b, _, v) => ((EmptyStateView)b).DescriptionLabel.Text = v?.ToString());

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(nameof(ActionText), typeof(string), typeof(EmptyStateView), "Get Started",
        propertyChanged: (b, _, v) => ((EmptyStateView)b).ActionButton.Text = v?.ToString());

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }

    public EmptyStateView() => InitializeComponent();
}
