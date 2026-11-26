using System.Globalization;
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
    protected LibraryPage(string title, string? subtitle, bool pinHeader = false)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var titles = new StackPanel { Spacing = 5 };
        var heading = Heading(title, 32);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        titles.Children.Add(heading);
        if (subtitle is not null) titles.Children.Add(Secondary(subtitle, 14));
        var header = new Grid { ColumnSpacing = 16 };
        header.RowDefinitions.Add(new() { Height = GridLength.Auto });
        header.RowDefinitions.Add(new() { Height = GridLength.Auto });
        header.ColumnDefinitions.Add(new());
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(titles);
        header.Children.Add(Actions);
        Grid.SetColumn(Actions, 1);
        heading.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double titleWidth = heading.DesiredSize.Width;
        void ArrangeHeader()
        {
            bool stack = Actions.ActualWidth > 0 && header.ActualWidth < titleWidth + 16 + Actions.ActualWidth;
            Grid.SetColumnSpan(titles, stack ? 2 : 1);
            Grid.SetRow(Actions, stack ? 1 : 0);
            Grid.SetColumn(Actions, stack ? 0 : 1);
            Grid.SetColumnSpan(Actions, stack ? 2 : 1);
            header.RowSpacing = stack ? 8 : 0;
            Actions.HorizontalAlignment = stack ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }
        header.SizeChanged += (_, _) => ArrangeHeader();
        Actions.SizeChanged += (_, _) => ArrangeHeader();
        if (!pinHeader) Body.Children.Add(header);
        Message.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(Message, AutomationLiveSetting.Polite);
        Message.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
            Message.Visibility = string.IsNullOrEmpty(Message.Text) ? Visibility.Collapsed : Visibility.Visible);
        Body.Children.Add(Message);
        var viewport = new ScrollViewer { Content = Body, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(40, 36, 40, 48), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (pinHeader)
        {
            var layout = new Grid();
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            layout.Children.Add(header);
            Grid.SetRow(viewport, 1);
            layout.Children.Add(viewport);
            Content = layout;
        }
        else Content = viewport;
        SizeChanged += (_, e) =>
        {
            var padding = e.NewSize.Width < 700 ? new Thickness(20, 16, 20, 24) : new Thickness(40, 36, 40, 48);
            if (pinHeader) header.Margin = new Thickness(padding.Left, padding.Top, padding.Right, 12);
            viewport.Padding = pinHeader ? new Thickness(padding.Left, 0, padding.Right, padding.Bottom) : padding;
        };
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
        var content = new StackPanel { Spacing = 8, Children = { message } };
        if (action is not null)
        {
            action.Margin = new Thickness(62, 0, 0, 0);
            action.HorizontalAlignment = HorizontalAlignment.Left;
            content.Children.Add(action);
        }
        entries.Children.Add(new Border { Padding = new Thickness(22), Child = content });
    }

    protected Button? UpdateListTools(StackPanel tools, AutoSuggestBox language, bool hasEntriesOrFilters)
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
        };
        return change;
    }

    protected static Button ResetSearch(TextBox search, ComboBox? type = null)
    {
        var clear = new Button { Content = type is null ? "Clear search" : "Clear filters" };
        clear.Click += (_, _) =>
        {
            if (type is not null) type.SelectedIndex = 0;
            search.Text = "";
            search.Focus(FocusState.Keyboard);
        };
        return clear;
    }

    protected StackPanel AddEntryList()
    {
        var list = new StackPanel();
        var card = Card(list);
        card.Padding = new Thickness(0);
        Body.Children.Add(card);
        return list;
    }

    protected void AddEntryRow(StackPanel entries, Guid id, string glyph, string title, string? description, string name, Func<Task> edit, Func<Task> delete, bool isTextPreview = false)
    {
        var copy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var first = Heading(title, 16);
        first.MaxLines = 2;
        first.TextTrimming = TextTrimming.CharacterEllipsis;
        copy.Children.Add(first);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var second = isTextPreview ? Text(description, 14) : Secondary(description);
            second.MaxLines = 2;
            second.TextTrimming = TextTrimming.CharacterEllipsis;
            copy.Children.Add(second);
        }
        var editButton = Action("Edit", edit);
        AutomationProperties.SetName(editButton, $"Edit {name}");
        var remove = new Button
        {
            Content = new SymbolIcon(Symbol.Delete),
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"]
        };
        AutomationProperties.SetName(remove, $"Delete {name}");
        ToolTipService.SetToolTip(remove, $"Delete {name}");
        remove.Click += async (_, _) => await RunAsync(delete);
        var item = new Border
        {
            Padding = new Thickness(15, 10, 15, 10),
            BorderThickness = new Thickness(0, entries.Children.Count == 0 ? 0 : 1, 0, 0),
            Style = (Style)Application.Current.Resources["LibrarySeparator"],
            Tag = id,
            Child = Row(WithLeadingIcon(IconTile(glyph), copy), editButton, remove)
        };
        entries.Children.Add(item);
    }

    protected Grid SearchTools(TextBox search, params FrameworkElement[] options)
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
        tools.RowDefinitions.Add(new() { Height = GridLength.Auto });
        tools.RowDefinitions.Add(new() { Height = GridLength.Auto });
        bool stacked = false;
        var searchHeader = search.Header;
        double pageSpacing = Body.Spacing;
        var widths = options.Select(option => option.Width).ToArray();
        tools.SizeChanged += (_, args) =>
        {
            bool narrow = args.NewSize.Width < (options.Length == 1 ? 370 : 510);
            if (stacked == narrow) return;
            stacked = narrow;
            search.Header = narrow ? null : searchHeader;
            Body.Spacing = narrow ? 10 : pageSpacing;
            tools.RowSpacing = narrow ? 6 : 0;
            Grid.SetColumnSpan(search, narrow ? options.Length : 1);
            for (int i = 0; i < options.Length; i++)
            {
                int column = narrow ? i : i + 1;
                Grid.SetRow(options[i], narrow ? 1 : 0);
                Grid.SetColumn(options[i], column);
                options[i].Width = narrow ? double.NaN : widths[i];
                tools.ColumnDefinitions[i + 1].Width = narrow && i + 1 < options.Length
                    ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            }
        };
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

    internal static void ApplyDialogAccent(ContentDialog dialog, bool confirmDeletion = false)
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

    protected async Task EditAsync(string title, StackPanel fields, string language, StackPanel entries, Guid id, Func<string, Task> save, params TextBox[] required)
    {
        var current = Text(DisplayLanguage(language));
        current.Name = "EditorCurrentLanguage";
        var change = new Button { Name = "ChangeEditorLanguage", Content = "Change language" };
        var languageLabel = new StackPanel { Children = { Secondary("Language"), current } };
        fields.Children.Add(Row(languageLabel, change));
        var error = Text("", 13);
        error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        fields.Children.Add(error);
        var viewport = new ScrollViewer { Name = "EditorFields", Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var host = new Grid { Children = { viewport } };
        double formHeight = 0;
        void FitEditor(XamlRoot root, XamlRootChangedEventArgs? args)
        {
            host.Width = Math.Clamp(root.Size.Width - 96, 0, 480);
            viewport.MaxHeight = Math.Clamp(root.Size.Height - 200, 96, 480);
            if (!double.IsNaN(host.Height)) host.Height = Math.Min(formHeight, viewport.MaxHeight);
        }
        FitEditor(XamlRoot, null);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = title,
            Content = host,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        ApplyDialogAccent(dialog);
        dialog.Resources["ContentDialogMinWidth"] = Math.Clamp(XamlRoot.Size.Width - 48, 0, 528);
        var picker = new EditorLanguagePicker();
        host.Children.Add(picker);
        void ClosePicker(string? selected)
        {
            if (selected is not null)
            {
                language = selected;
                current.Text = DisplayLanguage(language);
            }
            picker.Visibility = Visibility.Collapsed;
            viewport.Visibility = Visibility.Visible;
            host.Height = double.NaN;
            dialog.Title = title;
            dialog.PrimaryButtonText = "Save";
            dialog.CloseButtonText = "Cancel";
            Validate();
            change.Focus(FocusState.Keyboard);
        }
        change.Click += (_, _) =>
        {
            formHeight = viewport.ActualHeight;
            host.Height = formHeight;
            viewport.Visibility = Visibility.Collapsed;
            picker.Visibility = Visibility.Visible;
            dialog.Title = "Choose language";
            dialog.PrimaryButtonText = "Use language";
            dialog.CloseButtonText = "Back";
            picker.Open(language);
            Validate();
        };
        void Validate() => dialog.IsPrimaryButtonEnabled = picker.Visibility == Visibility.Visible
            ? picker.PendingCode.Length is > 0 and <= 32
            : language.Length is > 0 and <= 32 && required.All(box =>
                box.Visibility == Visibility.Collapsed || !string.IsNullOrWhiteSpace(box.Text));
        picker.CodeChanged += Validate;
        picker.Done += ClosePicker;
        foreach (var box in required)
        {
            box.TextChanged += (_, _) => Validate();
            box.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Validate());
        }
        Validate();
        dialog.Opened += (_, _) => dialog.DispatcherQueue.TryEnqueue(() => required[0].Focus(FocusState.Programmatic));
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (picker.Visibility == Visibility.Visible)
            {
                args.Cancel = true;
                ClosePicker(picker.PendingCode);
                return;
            }
            var deferral = args.GetDeferral();
            try { await save(language); }
            catch
            {
                args.Cancel = true;
                error.Text = "Check the fields and try again. The phrase may already exist.";
                error.Visibility = Visibility.Visible;
            }
            finally { deferral.Complete(); }
        };
        dialog.Closing += (_, args) =>
        {
            if (picker.Visibility != Visibility.Visible || args.Result != ContentDialogResult.None) return;
            args.Cancel = true;
            ClosePicker(null);
        };
        var owner = XamlRoot;
        owner.Changed += FitEditor;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await RefreshAsync();
                if (XamlRoot is null || Visibility != Visibility.Visible) return;
                var row = entries.Children.OfType<Border>().FirstOrDefault(item => Equals(item.Tag, id));
                var target = row is null ? null : Microsoft.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(row) as Control;
                (target ?? Actions.Children.OfType<Button>().FirstOrDefault())?.Focus(FocusState.Keyboard);
            }
        }
        finally { owner.Changed -= FitEditor; }
    }

    private sealed record LanguageChoice(string Code, string Name)
    {
        public override string ToString() => $"{Name} · {Code}";
    }

    private static readonly LanguageChoice[] Languages = CultureInfo.GetCultures(CultureTypes.AllCultures)
        .Where(c => c.Name.Length > 0)
        .Select(c => new LanguageChoice(c.Name, c.EnglishName))
        .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    protected static AutoSuggestBox LanguageField(string code)
    {
        var field = new AutoSuggestBox
        {
            Header = "Language",
            PlaceholderText = "Search by name or code",
            QueryIcon = new SymbolIcon(Symbol.World),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetHelpText(field, "Search by language name or enter your own language code.");
        SetLanguage(field, code);
        field.TextChanged += (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            string query = field.Text.Trim();
            field.ItemsSource = FindLanguages(query);
        };
        field.QuerySubmitted += (_, args) =>
        {
            string selected = args.ChosenSuggestion is LanguageChoice choice ? choice.Code : LanguageCode(field);
            SetLanguage(field, selected);
        };
        return field;
    }

    private static LanguageChoice[] FindLanguages(string query) =>
        Languages.Where(c => c.Code.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Code.Equals(query, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .Take(16).ToArray();

    private sealed class EditorLanguagePicker : Grid
    {
        private readonly TextBlock _current = Text("", 14);
        private readonly TextBox _query = new() { Name = "EditorLanguageSearch", PlaceholderText = "Search by name or enter a code" };
        private readonly ListView _results;
        private readonly Grid _resultArea = new();
        private string _original = "";

        public event Action? CodeChanged;
        public event Action<string?>? Done;
        public string PendingCode => (_results.SelectedItem as LanguageChoice)?.Code ??
            (string.IsNullOrWhiteSpace(_query.Text) ? _original : ResolveLanguageCode(_query.Text, _original));

        public EditorLanguagePicker()
        {
            Name = "EditorLanguagePicker";
            Visibility = Visibility.Collapsed;
            RowSpacing = 5;
            RowDefinitions.Add(new() { Height = GridLength.Auto });
            RowDefinitions.Add(new() { Height = GridLength.Auto });
            RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            _current.VerticalAlignment = VerticalAlignment.Center;
            _current.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetLiveSetting(_current, AutomationLiveSetting.Polite);
            var selected = new Grid { ColumnSpacing = 7 };
            selected.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            selected.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            _current.MaxLines = 1;
            _current.TextTrimming = TextTrimming.CharacterEllipsis;
            SetColumn(_current, 1);
            var check = new FontIcon { Glyph = "\uE73E", FontSize = 13, Style = (Style)Application.Current.Resources["LibraryAccentIcon"] };
            selected.Children.Add(check);
            selected.Children.Add(_current);
            Children.Add(selected);
            AutomationProperties.SetName(_query, "Language name or code");
            SetRow(_query, 1);
            Children.Add(_query);

            var rowStyle = new Style(typeof(ListViewItem));
            rowStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 32d));
            rowStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32d));
            _results = new ListView
            {
                Name = "EditorLanguageSuggestions",
                Style = (Style)Application.Current.Resources["LibraryLanguageResults"],
                ItemContainerStyle = rowStyle,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top
            };
            foreach (string theme in new[] { "Light", "Dark" })
            {
                var palette = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[theme];
                _results.Resources.ThemeDictionaries[theme] = new ResourceDictionary
                {
                    ["ListViewItemBackgroundSelected"] = palette["ShrutiAccentBrush"],
                    ["ListViewItemBackgroundSelectedPointerOver"] = palette["ShrutiAccentHoverBrush"],
                    ["ListViewItemBackgroundSelectedPressed"] = palette["ShrutiAccentHoverBrush"],
                    ["ListViewItemForegroundSelected"] = palette["ShrutiAccentForegroundBrush"]
                };
            }
            _results.Resources.ThemeDictionaries["HighContrast"] = new ResourceDictionary();
            SetRow(_resultArea, 2);
            _resultArea.Children.Add(_results);
            Children.Add(_resultArea);
            _resultArea.SizeChanged += (_, _) => SizeResults();
            AutomationProperties.SetName(_results, "Matching languages");
            ScrollViewer.SetVerticalScrollBarVisibility(_results, ScrollBarVisibility.Visible);
            _query.TextChanged += (_, _) => RefreshMatches();
            _results.SelectionChanged += (_, _) =>
            {
                _current.Text = $"Selected language: {DisplayLanguage(PendingCode)}";
                CodeChanged?.Invoke();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    e.Handled = true;
                    Done?.Invoke(null);
                }
            };
            _query.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Down && _results.Items.Count > 0)
                {
                    e.Handled = true;
                    _results.SelectedIndex = 0;
                    _results.UpdateLayout();
                    (_results.ContainerFromIndex(0) as ListViewItem)?.Focus(FocusState.Keyboard);
                }
                else if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    e.Handled = true;
                    string code = PendingCode;
                    if (code.Length is > 0 and <= 32) Done?.Invoke(code);
                }
            };
            _results.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter && _results.SelectedItem is LanguageChoice choice)
                {
                    e.Handled = true;
                    Done?.Invoke(choice.Code);
                }
            };
        }

        public void Open(string code)
        {
            _original = code;
            _current.Text = $"Selected language: {DisplayLanguage(code)}";
            _query.Text = "";
            RefreshMatches();
            _query.Focus(FocusState.Keyboard);
        }

        private void SizeResults()
        {
            // Keep the last result wholly visible above the fixed footer.
            double available = _resultArea.ActualHeight;
            if (available < 34) return;
            int visible = Math.Min(_results.Items.Count, (int)((available - 2) / 32));
            _results.Height = 2 + 32 * visible;
        }

        private void RefreshMatches()
        {
            string query = _query.Text.Trim();
            var choices = FindLanguages(query);
            if (query.Length == 0)
            {
                var current = Languages.FirstOrDefault(c => c.Code.Equals(_original, StringComparison.OrdinalIgnoreCase))
                    ?? new LanguageChoice(_original, "Custom language");
                choices = [current, .. choices.Where(c => !c.Code.Equals(current.Code, StringComparison.OrdinalIgnoreCase)).Take(15)];
            }
            if (query.Length is > 0 and <= 32 && !choices.Any(c => c.Code.Equals(query, StringComparison.Ordinal)))
                choices = [.. choices, new LanguageChoice(query, "Use code")];
            _results.ItemsSource = choices;
            SizeResults();
            _results.SelectedIndex = choices.Length > 0 ? 0 : -1;
            CodeChanged?.Invoke();
        }
    }

    private static string DisplayLanguage(string code) =>
        Languages.FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.ToString() ?? code;

    protected static void SetLanguage(AutoSuggestBox field, string code)
    {
        field.Tag = code;
        field.Text = DisplayLanguage(code);
    }

    protected static string LanguageCode(AutoSuggestBox field) => ResolveLanguageCode(field.Text, (string)field.Tag);

    private static string ResolveLanguageCode(string text, string original)
    {
        string input = text.Trim();
        if (input == DisplayLanguage(original)) return original;
        var choice = Languages.FirstOrDefault(c => c.ToString().Equals(input, StringComparison.OrdinalIgnoreCase) ||
            c.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
        return choice?.Code ?? input;
    }

    protected static string LanguageName(string code) =>
        Languages.FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Name ?? code;

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
