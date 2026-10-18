using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Shruti.App.WinUI;

public abstract class LibraryPage : UserControl
{
    protected readonly StackPanel Body = new() { Spacing = 24, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch };
    protected readonly StackPanel Actions = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
    protected readonly TextBlock Message = Text("");
    protected LibraryPage(string title, string subtitle)
    {
        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(Text(title, 32));
        Grid.SetColumn(Actions, 1); header.Children.Add(Actions);
        Body.Children.Add(header);
        var description = Text(subtitle); description.Opacity = .65; Body.Children.Add(description);
        Message.Visibility = Visibility.Collapsed;
        Message.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
            Message.Visibility = string.IsNullOrEmpty(Message.Text) ? Visibility.Collapsed : Visibility.Visible);
        Body.Children.Add(Message);
        Content = new ScrollViewer { Content = Body, Padding = new Thickness(40, 40, 40, 48), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += async (_, _) => await RefreshAsync();
    }

    public abstract Task RefreshAsync();
    protected async Task RunAsync(Func<Task> action)
    {
        try { Message.Text = ""; await action(); }
        catch { Message.Text = "Couldn't complete this action. Try again."; }
    }
    internal static TextBlock Text(string value, double size = 15) => new()
    { Text = value, FontSize = size, Style = (Style)Application.Current.Resources["LibraryTextStyle"] };
    internal static Border Card(UIElement child) => new() { Child = child, Style = (Style)Application.Current.Resources["LibraryCardStyle"] };
    internal static Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
        button.Click += async (_, _) => await action();
        return button;
    }
    protected Button Action(string label, Func<Task> action) => Button(label, () => RunAsync(action));
    protected static StackPanel FilterBar(params FrameworkElement[] controls)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var control in controls)
        {
            // Align input boxes and buttons, excluding the space used by field headers.
            control.VerticalAlignment = VerticalAlignment.Bottom;
            panel.Children.Add(control);
        }
        return panel;
    }
    protected static Grid Row(UIElement content, params UIElement[] actions)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(content);
        var buttons = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions) buttons.Children.Add(action);
        Grid.SetColumn(buttons, 1); row.Children.Add(buttons);
        return row;
    }
    protected async Task<bool> ConfirmAsync(string title, string detail)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = title,
            Content = detail,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    protected async Task EditAsync(string title, StackPanel fields, Func<Task> save)
    {
        var error = Text(""); fields.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = title,
            Content = new ScrollViewer { Content = fields, MaxHeight = 480 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try { await save(); }
            catch { args.Cancel = true; error.Text = "Check the fields. The phrase may already exist. Nothing was saved."; }
            finally { deferral.Complete(); }
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await RefreshAsync();
    }
    protected static TextBox Field(string label, string value = "", int maximum = 256, bool multiline = false) => new()
    { Header = label, Text = value, MaxLength = maximum, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, MinWidth = 320, MaxHeight = multiline ? 240 : 90 };
    protected static void Copy(string text)
    {
        var package = new DataPackage(); package.SetText(text); Clipboard.SetContent(package);
    }
}
