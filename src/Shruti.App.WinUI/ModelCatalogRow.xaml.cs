using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Shruti.App.WinUI;

public sealed partial class ModelCatalogRow : UserControl
{
    private readonly string _details = string.Empty;

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

    internal void AddAction(Button button) => RowActions.Children.Add(button);

    internal void SetState(bool installed, bool active)
    {
        InUse.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        Metadata.Text = installed && !active ? $"Installed  ·  {_details}" : _details;
    }
}
