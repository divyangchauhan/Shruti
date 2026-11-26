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
            combo.DropDownOpened -= ApplyPopupCorners;
            combo.DropDownOpened += ApplyPopupCorners;
        }
        else if (element is ComboBox previous)
        {
            previous.DropDownOpened -= ApplyPopupCorners;
        }
    }

    private static void ApplyPopupCorners(object? sender, object args)
    {
        if (sender is not ComboBox combo) return;
        combo.DispatcherQueue.TryEnqueue(() =>
        {
            if (combo.IsDropDownOpen && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(combo) > 0 &&
                Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(combo, 0) is FrameworkElement template &&
                template.FindName("PopupBorder") is Border border)
                border.CornerRadius = new CornerRadius(6);
        });
    }

    public static MenuFlyout CreateMenuFlyout() => new()
    {
        MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["OpaqueMenuFlyoutPresenterStyle"]
    };
}
