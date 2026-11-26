using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Shruti.App.WinUI;

public sealed partial class ModelCatalogRow : UserControl
{
    private readonly string _details = string.Empty;

    private (string Name, string Format, string Supported, string Detected) _processors;

    public ModelCatalogRow() => InitializeComponent();

    internal ModelCatalogRow(string title, string metadata, bool divider, Uri? license = null) : this()
    {
        ModelTitle.Text = title;
        _details = metadata;
        Metadata.Text = metadata;
        Separator.BorderThickness = new Thickness(0, divider ? 1 : 0, 0, 0);
        if (license is not null)
        {
            License.NavigateUri = license;
            License.Visibility = Visibility.Visible;
            AutomationProperties.SetName(License, $"{title} model license");
        }
    }

    private void Layout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool stack = args.NewSize.Width < 400;
        Grid.SetColumnSpan(Description, stack ? 2 : 1);
        Grid.SetRow(RowActions, stack ? 1 : 0);
        Grid.SetColumn(RowActions, stack ? 0 : 1);
        Grid.SetColumnSpan(RowActions, stack ? 2 : 1);
        RowActions.HorizontalAlignment = stack ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        Layout.RowSpacing = stack ? 8 : 0;
    }

    internal void SetProcessorDetails(string modelName, string format, string supported, string detected)
    {
        _processors = (modelName, format, supported, detected);
        AutomationProperties.SetName(CompatibilityButton, $"{modelName} compatibility and format details");
        CompatibilityButton.Visibility = Visibility.Visible;
    }

    private async void ShowProcessorDetails_Click(object sender, RoutedEventArgs args)
    {
        var fields = new StackPanel { Spacing = 4 };
        if (!string.IsNullOrEmpty(_processors.Format))
            fields.Children.Add(new TextBlock { Text = _processors.Format, TextWrapping = TextWrapping.Wrap });

        void AddProcessors(string label, string value)
        {
            fields.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 8, 0, 0),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            fields.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
        }
        AddProcessors("Supported processors", _processors.Supported);
        AddProcessors("Detected on this PC", _processors.Detected);
        var dialog = new ContentDialog
        {
            Title = _processors.Name,
            Content = fields,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme
        };
        await dialog.ShowAsync();
    }

    internal void AddAction(Button button) => RowActions.Children.Add(button);

    internal void SetState(bool installed, bool active)
    {
        InUse.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        Metadata.Text = installed && !active ? $"Installed  ·  {_details}" : _details;
    }
}
