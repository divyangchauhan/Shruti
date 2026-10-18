using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Shruti.App.WinUI;

public static class PopupAppearance
{
    public static readonly DependencyProperty UseOpaqueBackgroundProperty = DependencyProperty.RegisterAttached(
        "UseOpaqueBackground", typeof(bool), typeof(PopupAppearance), new PropertyMetadata(false, OnUseOpaqueBackgroundChanged));

    public static bool GetUseOpaqueBackground(DependencyObject element) => (bool)element.GetValue(UseOpaqueBackgroundProperty);
    public static void SetUseOpaqueBackground(DependencyObject element, bool value) => element.SetValue(UseOpaqueBackgroundProperty, value);

    private static void OnUseOpaqueBackgroundChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is ComboBox combo && args.NewValue is true)
        {
            // WinUI's template resources take precedence over app-level overrides.
            // Put the brush beside the control so its popup resolves the opaque brush.
            combo.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("ms-appx:///Styles/PopupResources.xaml")
            });
        }
    }

    public static MenuFlyout CreateMenuFlyout() => new()
    {
        MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["OpaqueMenuFlyoutPresenterStyle"]
    };
}
