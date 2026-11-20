using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Shruti.App.WinUI;

public abstract class LibraryPage : UserControl
{
    protected readonly StackPanel Body = new() { Spacing = 22, HorizontalAlignment = HorizontalAlignment.Stretch };
    protected readonly StackPanel Actions = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    protected readonly TextBlock Message = Text("");
    private bool _filtersOpened;
    protected LibraryPage(string title, string? subtitle)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var titles = new StackPanel { Spacing = 5 };
        var heading = Heading(title, 32);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        titles.Children.Add(heading);
        if (subtitle is not null) titles.Children.Add(Secondary(subtitle, 14));
        Body.Children.Add(Row(titles, Actions));
        Message.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(Message, AutomationLiveSetting.Polite);
        Message.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
            Message.Visibility = string.IsNullOrEmpty(Message.Text) ? Visibility.Collapsed : Visibility.Visible);
        Body.Children.Add(Message);
        Content = new ScrollViewer { Content = Body, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(40, 36, 40, 48), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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

    protected static TextBlock Heading(string value, double size) => new()
    {
        Text = value,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        Style = (Style)Application.Current.Resources["LibraryTextStyle"]
    };

    protected TextBlock Secondary(string value, double size = 13) =>
        new() { Text = value, FontSize = size, Style = (Style)Application.Current.Resources["LibraryMutedText"] };

    internal static Border Card(UIElement child) => new() { Child = child, Style = (Style)Application.Current.Resources["LibraryCardStyle"] };
    internal static Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
        button.Click += async (_, _) => await action();
        return button;
    }
    protected Button Action(string label, Func<Task> action) => Button(label, () => RunAsync(action));

    protected void AddCreateAction(string label, Func<Task> action)
    {
        var button = Action(label, action);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var icon = new FontIcon { Glyph = "\uE710", FontSize = 13 };
        var text = new TextBlock { Text = label, FontSize = 14 };
        var foreground = new Microsoft.UI.Xaml.Data.Binding { Source = button, Path = new PropertyPath("Foreground") };
        icon.SetBinding(FontIcon.ForegroundProperty, foreground);
        text.SetBinding(TextBlock.ForegroundProperty, new Microsoft.UI.Xaml.Data.Binding { Source = button, Path = new PropertyPath("Foreground") });
        content.Children.Add(icon);
        content.Children.Add(text);
        button.Content = content;
        button.Style = (Style)Application.Current.Resources["LibraryPrimaryButton"];
        AutomationProperties.SetName(button, label);
        Actions.Children.Add(button);
    }

    protected Border IconTile(string glyph, double size = 38) => new()
    {
        Width = size,
        Height = size,
        VerticalAlignment = VerticalAlignment.Center,
        Style = (Style)Application.Current.Resources["LibraryAccentTile"],
        Child = new FontIcon { Glyph = glyph, FontSize = size >= 48 ? 23 : 16, Style = (Style)Application.Current.Resources["LibraryAccentIcon"] }
    };

    protected static Grid Row(UIElement content, params UIElement[] actions)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(content);
        var buttons = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions) buttons.Children.Add(action);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        return row;
    }

    protected Grid WithLeadingIcon(UIElement icon, FrameworkElement text)
    {
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(icon);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private Grid EmptyContent(string glyph, string title, string detail)
    {
        var copy = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(Heading(title, 19));
        copy.Children.Add(Secondary(detail, 14));
        return WithLeadingIcon(IconTile(glyph, 48), copy);
    }

    protected Border EmptyCard(string glyph, string title, string detail) => Card(EmptyContent(glyph, title, detail));

    protected void AddListEmpty(StackPanel entries, string glyph, string title, string detail, Button? action)
    {
        var message = EmptyContent(glyph, title, detail);
        entries.Children.Add(new Border { Padding = new Thickness(22), Child = action is null ? message : Row(message, action) });
    }

    protected Button? UpdateListTools(StackPanel tools, TextBox language, bool hasEntriesOrFilters)
    {
        bool visible = hasEntriesOrFilters || _filtersOpened;
        tools.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) return null;
        var change = new Button { Content = "Change language" };
        change.Click += (_, _) =>
        {
            _filtersOpened = true;
            tools.Visibility = Visibility.Visible;
            change.Visibility = Visibility.Collapsed;
            language.Focus(FocusState.Programmatic);
            language.SelectAll();
        };
        return change;
    }

    protected StackPanel AddEntryList()
    {
        var list = new StackPanel();
        var card = Card(list);
        card.Padding = new Thickness(0);
        Body.Children.Add(card);
        return list;
    }

    protected void AddEntryRow(StackPanel entries, string glyph, string title, string? description, string name, Func<Task> edit, Func<Task> delete)
    {
        var copy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var first = Heading(title, 16);
        first.MaxLines = 2;
        first.TextTrimming = TextTrimming.CharacterEllipsis;
        copy.Children.Add(first);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var second = Secondary(description);
            second.MaxLines = 2;
            second.TextTrimming = TextTrimming.CharacterEllipsis;
            copy.Children.Add(second);
        }
        var editButton = Action("Edit", edit);
        AutomationProperties.SetName(editButton, $"Edit {name}");
        var menu = PopupAppearance.CreateMenuFlyout();
        var deleteItem = new MenuFlyoutItem { Text = "Delete", Icon = new SymbolIcon(Symbol.Delete) };
        deleteItem.Click += async (_, _) => await RunAsync(delete);
        menu.Items.Add(deleteItem);
        var more = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 15 }, Width = 36, Height = 32, Padding = new Thickness(0), Flyout = menu };
        ToolTipService.SetToolTip(more, $"More options for {name}");
        AutomationProperties.SetName(more, $"More options for {name}");
        var item = new Border
        {
            Padding = new Thickness(16, 13, 16, 13),
            BorderThickness = new Thickness(0, entries.Children.Count == 0 ? 0 : 1, 0, 0),
            Style = (Style)Application.Current.Resources["LibrarySeparator"],
            Child = Row(WithLeadingIcon(IconTile(glyph), copy), editButton, more)
        };
        entries.Children.Add(item);
    }

    protected static Grid SearchTools(TextBox search, params FrameworkElement[] options)
    {
        AutomationProperties.SetName(search, search.PlaceholderText);
        search.VerticalAlignment = VerticalAlignment.Bottom;
        var tools = new Grid { ColumnSpacing = 10 };
        tools.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        tools.Children.Add(search);
        for (int i = 0; i < options.Length; i++)
        {
            tools.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            options[i].VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(options[i], i + 1);
            tools.Children.Add(options[i]);
        }
        return tools;
    }

    protected async Task<bool> ConfirmAsync(string title, string detail)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = title,
            Content = Text(detail),
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        ApplyDialogAccent(dialog, confirmDeletion: true);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    protected void ApplyDialogAccent(ContentDialog dialog, bool confirmDeletion = false)
    {
        foreach (string theme in new[] { "Light", "Dark" })
        {
            var palette = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[theme];
            if (confirmDeletion)
            {
                var border = palette["ShrutiBorderStrongBrush"];
                var text = palette["ShrutiTextPrimaryBrush"];
                var danger = palette["ShrutiDangerBrush"];
                // WinUI assigns AccentButtonStyle to the keyboard default, which is Cancel here.
                dialog.Resources.ThemeDictionaries[theme] = new ResourceDictionary
                {
                    ["AccentButtonBackground"] = palette["ShrutiCardBrush"],
                    ["AccentButtonBackgroundPointerOver"] = palette["ShrutiHoverBrush"],
                    ["AccentButtonBackgroundPressed"] = palette["ShrutiPressedBrush"],
                    ["AccentButtonForeground"] = text,
                    ["AccentButtonForegroundPointerOver"] = text,
                    ["AccentButtonForegroundPressed"] = text,
                    ["AccentButtonBorderBrush"] = border,
                    ["AccentButtonBorderBrushPointerOver"] = border,
                    ["AccentButtonBorderBrushPressed"] = border,
                    ["ButtonForeground"] = danger,
                    ["ButtonForegroundPointerOver"] = danger,
                    ["ButtonForegroundPressed"] = danger,
                    ["ButtonBorderBrush"] = danger,
                    ["ButtonBorderBrushPointerOver"] = danger,
                    ["ButtonBorderBrushPressed"] = danger,
                    ["ButtonBackground"] = palette["ShrutiCardBrush"],
                    ["ButtonBackgroundPointerOver"] = palette["ShrutiPageBrush"],
                    ["ButtonBackgroundPressed"] = palette["ShrutiSunkenBrush"]
                };
                continue;
            }
            var accent = palette["ShrutiAccentBrush"];
            var hover = palette["ShrutiAccentHoverBrush"];
            var foreground = palette["ShrutiAccentForegroundBrush"];
            dialog.Resources.ThemeDictionaries[theme] = new ResourceDictionary
            {
                ["AccentButtonBackground"] = accent,
                ["AccentButtonBackgroundPointerOver"] = hover,
                ["AccentButtonBackgroundPressed"] = accent,
                ["AccentButtonForeground"] = foreground,
                ["AccentButtonForegroundPointerOver"] = foreground,
                ["AccentButtonForegroundPressed"] = foreground,
                ["TextControlBorderBrushFocused"] = accent
            };
        }
        // The Windows templates supply their own system brushes in contrast themes.
        dialog.Resources.ThemeDictionaries["HighContrast"] = new ResourceDictionary();
    }

    protected async Task EditAsync(string title, StackPanel fields, Func<Task> save, TextBox firstField, params TextBox[] required)
    {
        var error = Text("", 13);
        error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        fields.Children.Add(error);
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
        ApplyDialogAccent(dialog);
        dialog.Resources["ContentDialogMinWidth"] = Math.Clamp(XamlRoot.Size.Width - 48, 0, 528);
        void Validate() => dialog.IsPrimaryButtonEnabled = required.All(box =>
            box.Visibility == Visibility.Collapsed || !string.IsNullOrWhiteSpace(box.Text));
        foreach (var box in required)
        {
            box.TextChanged += (_, _) => Validate();
            box.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Validate());
        }
        Validate();
        dialog.Opened += (_, _) => dialog.DispatcherQueue.TryEnqueue(() => firstField.Focus(FocusState.Programmatic));
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try { await save(); }
            catch
            {
                args.Cancel = true;
                error.Text = "Check the fields and try again. The phrase may already exist.";
                error.Visibility = Visibility.Visible;
            }
            finally { deferral.Complete(); }
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await RefreshAsync();
    }

    protected static TextBox Field(string label, string value = "", int maximum = 256, bool multiline = false) => new()
    {
        Header = label,
        AcceptsReturn = multiline,
        Text = value,
        MaxLength = maximum,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        MinWidth = 280,
        MinHeight = multiline ? 94 : 32,
        MaxHeight = multiline ? 200 : 90
    };
    protected static void Copy(string text)
    {
        var package = new DataPackage(); package.SetText(text); Clipboard.SetContent(package);
    }
}
