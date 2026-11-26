using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.Storage;
using Shruti.Core;
using Shruti.Core.Dictation;
using Shruti.Workflow.Dictation;
using Microsoft.UI.Xaml.Automation.Peers;
using Shruti.Core.Triggers;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Text;

namespace Shruti.App.WinUI;

public sealed class HistoryControl : LibraryPage
{
    private readonly TranscriptHistoryRepository _repository = new(AppDataPaths.CreateDefault());
    private readonly StackPanel _entries = new() { Spacing = 8 };
    private readonly TextBox _search = new() { PlaceholderText = "Search transcriptions", MaxLength = 256 };
    private readonly TextBlock _summary = Text("", 14);
    private readonly TextBlock _shortcut = Text("", 14);
    private readonly Border _emptyState;
    private readonly StackPanel _modelPrompt;
    public event EventHandler? ModelsRequested;
    public event EventHandler? DictationRequested;
    private readonly Button _record = new();
    private readonly TextBlock _recordingNotice = Text("", 14);
    private readonly DropDownButton _options;
    private readonly Button _more;
    private int _generation;
    private int _limit = 100;

    public HistoryControl() : base("Transcriptions", null)
    {
        Body.Spacing = 14;
        _summary.Opacity = .8;
        _recordingNotice.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(_recordingNotice, AutomationLiveSetting.Polite);
        Body.Children.Add(_recordingNotice);
        Body.Children.Add(_summary);
        _record.Style = (Style)Application.Current.Resources["LibraryPrimaryButton"];
        _record.Click += (_, _) => DictationRequested?.Invoke(this, EventArgs.Empty);
        Actions.Children.Add(_record);
        var menu = PopupAppearance.CreateMenuFlyout();
        menu.Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight;
        var clear = new MenuFlyoutItem { Text = "Clear history" };
        clear.Click += async (_, _) => await RunAsync(async () =>
        {
            if (await ConfirmAsync("Clear transcription history?", "This deletes all saved transcripts and their usage statistics from this PC. Dictionary entries and snippets stay saved."))
            {
                await _repository.DeleteAsync(null);
                await RefreshAsync();
            }
        });
        menu.Items.Add(clear);
        _options = new DropDownButton { Content = "History options", Flyout = menu };
        AutomationProperties.SetName(_options, "History options");
        Actions.Children.Add(_options);
        AutomationProperties.SetName(_search, "Search transcriptions");
        Body.Children.Add(_search);
        var intro = new StackPanel { Spacing = 6 };
        var heading = Text("Record your first dictation", 22);
        heading.FontWeight = FontWeights.SemiBold;
        intro.Children.Add(heading);
        intro.Children.Add(_shortcut);
        var modelPrompt = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        modelPrompt.Children.Add(Secondary("Download your selected speech model to start recording on this PC.", 14));
        var openModels = new Button { Content = "Open Models", Style = (Style)Application.Current.Resources["LibraryPrimaryButton"] };
        openModels.Click += (_, _) => ModelsRequested?.Invoke(this, EventArgs.Empty);
        modelPrompt.Children.Add(openModels);
        intro.Children.Add(modelPrompt);
        _modelPrompt = modelPrompt;
        var microphone = new FontIcon { Glyph = "\uE720", FontSize = 26 };
        var icon = new Border
        {
            Width = 56,
            Height = 56,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["DictationIconTileStyle"],
            Child = microphone
        };
        var guide = new Grid { ColumnSpacing = 18 };
        guide.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        guide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        guide.Children.Add(icon);
        Grid.SetColumn(intro, 1);
        guide.Children.Add(intro);
        _emptyState = Card(guide);
        _emptyState.Visibility = Visibility.Collapsed;
        Body.Children.Add(_emptyState);
        Body.Children.Add(_entries);
        _more = Action("Show more", async () => { _limit += 100; await RefreshAsync(); });
        Body.Children.Add(_more);
        _search.TextChanged += async (_, _) => { _limit = 100; await RefreshAsync(); };
    }

    private FrameworkElement? _reviewPanel;
    public FrameworkElement? ReviewPanel
    {
        get => _reviewPanel;
        set
        {
            if (_reviewPanel is not null) Body.Children.Remove(_reviewPanel);
            _reviewPanel = value;
            if (value is not null) Body.Children.Insert(Body.Children.IndexOf(_search), value);
        }
    }

    internal void UpdateRecordingAction(DictationShellState state, bool recordingSaveFailed)
    {
        bool recording = state.SessionState is DictationSessionState.Recording or DictationSessionState.Paused;
        string label = recording ? "Stop dictation" : state.IsRunning ? "Working…" : "Start dictation";
        _record.Content = label;
        _record.IsEnabled = recording ? state.CanStop : state.CanStart && !state.CanInsertPreview;
        ToolTipService.SetToolTip(_record, state.IsRunning ? state.StatusText : "Record here and copy the finished text to the clipboard.");
        _recordingNotice.Text = recordingSaveFailed ? "Shruti couldn't save the audio file." :
            state.LastOutcome == DictationRunOutcome.NoSpeech ? state.UserMessage : "";
        _recordingNotice.Visibility = _recordingNotice.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetModelInstalled(bool installed)
    {
        _record.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        _shortcut.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        _modelPrompt.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetShortcut(TriggerConfiguration triggers)
    {
        if (triggers.EnablePushToTalk && !string.IsNullOrWhiteSpace(triggers.PushToTalkKey))
            _shortcut.Text = $"Place your cursor in a text field. Hold {ReadableGesture(triggers.PushToTalkKey)} to speak, then release to finish.";
        else if (triggers.EnableGlobalHotkey && !string.IsNullOrWhiteSpace(triggers.HotkeyGesture))
            _shortcut.Text = $"Place your cursor in a text field. Press {ReadableGesture(triggers.HotkeyGesture)} to start and again to finish.";
        else
            _shortcut.Text = "Choose a dictation shortcut in Settings to record while you write.";
    }

    private static string ReadableGesture(string gesture) => gesture.Replace("RightControl", "Right Ctrl", StringComparison.Ordinal).Replace("+", " + ", StringComparison.Ordinal);

    private static Button IconButton(Symbol symbol, string name)
    {
        var button = new Button
        {
            Content = new SymbolIcon(symbol),
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Width = 36,
            Height = 32,
            Padding = new Thickness(0)
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    public override Task RefreshAsync() => RunAsync(async () =>
    {
        int generation = ++_generation;
        var rows = new List<TranscriptHistoryEntry>();
        for (int offset = 0; offset < _limit; offset += 100)
            rows.AddRange(await _repository.ListAsync(_search.Text, 100, offset));
        var stats = await _repository.GetInsightsAsync();
        if (generation != _generation) return;
        string total = $"{stats.Dictations:N0} {(stats.Dictations == 1 ? "dictation" : "dictations")}";
        _summary.Text = string.IsNullOrWhiteSpace(_search.Text)
            ? $"{total}  ·  {stats.Words:N0} {(stats.Words == 1 ? "word" : "words")} on this PC"
            : $"Searching {total} on this PC";
        bool hasHistory = stats.Dictations > 0;
        _summary.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        _search.Visibility = hasHistory || _search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _options.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        _entries.Children.Clear();
        foreach (var day in rows.GroupBy(r => r.CreatedAtUtc.LocalDateTime.Date))
        {
            var relativeDay = day.Key == DateTime.Today ? "Today" : day.Key == DateTime.Today.AddDays(-1) ? "Yesterday" : day.Key.ToString("dddd");
            var heading = Text($"{relativeDay}, {day.Key:M}", 15);
            if (day.Key.Year != DateTime.Today.Year) heading.Text = day.Key.ToString("D");
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 12, 0, 0);
            _entries.Children.Add(heading);
            var group = new StackPanel { Spacing = 0 };
            var card = Card(group);
            card.Padding = new Thickness(0);
            _entries.Children.Add(card);
            foreach (var entry in day)
            {
                var content = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                var timestamp = entry.CreatedAtUtc.LocalDateTime.ToString("t");
                var time = Text(string.IsNullOrWhiteSpace(entry.ProcessName) ? timestamp : $"{timestamp}  ·  {entry.ProcessName}", 13);
                time.Opacity = .75;
                time.VerticalAlignment = VerticalAlignment.Center;
                var preview = Text(entry.Text, 16);
                preview.MaxLines = ActualWidth < 700 ? 3 : 4;
                preview.SizeChanged += (_, _) => preview.MaxLines = ActualWidth < 700 ? 3 : 4;
                preview.TextTrimming = TextTrimming.WordEllipsis;
                content.Children.Add(preview);
                var readMore = new HyperlinkButton
                {
                    Content = "Read transcript",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(0)
                };
                readMore.Click += async (_, _) => await RunAsync(() => OpenAsync(entry, readMore));
                preview.IsTextTrimmedChanged += (_, _) =>
                    readMore.Content = preview.IsTextTrimmed ? "Read full transcript" : "Read transcript";

                var more = IconButton(Symbol.More, "Transcript options");
                var menu = PopupAppearance.CreateMenuFlyout();
                menu.Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight;
                var open = new MenuFlyoutItem { Text = "Read transcript", Icon = new SymbolIcon(Symbol.Document) };
                open.Click += async (_, _) => await RunAsync(() => OpenAsync(entry, more)); menu.Items.Add(open);
                var delete = new MenuFlyoutItem { Text = "Delete", Icon = new SymbolIcon(Symbol.Delete) };
                delete.Click += async (_, _) => await RunAsync(async () =>
                {
                    if (await ConfirmAsync("Delete this transcript?", "This also removes it from Insights."))
                    { await _repository.DeleteAsync(entry.Id); await RefreshAsync(); }
                }); menu.Items.Add(delete);
                var copyLabel = new TextBlock { Text = "Copy" };
                AutomationProperties.SetLiveSetting(copyLabel, AutomationLiveSetting.Polite);
                var copyIcon = new FontIcon { Glyph = "\uE8C8", FontSize = 14 };
                int copyGeneration = 0;
                var copy = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children = { copyIcon, copyLabel }
                    },
                    VerticalAlignment = VerticalAlignment.Center,
                    Height = 32
                };
                copy.MinWidth = 90;
                AutomationProperties.SetName(copy, "Copy transcript");
                copy.Click += async (_, _) => await RunAsync(async () =>
                {
                    Copy(entry.Text);
                    int generation = ++copyGeneration;
                    copyIcon.Glyph = "\uE73E";
                    copyLabel.Text = "Copied";
                    await Task.Delay(1800);
                    if (generation == copyGeneration)
                    {
                        copyIcon.Glyph = "\uE8C8";
                        copyLabel.Text = "Copy";
                    }
                });
                more.Flyout = menu;
                content.Children.Insert(0, Row(time, copy, more));
                content.Children.Add(readMore);
                var item = Card(content);
                item.CornerRadius = new CornerRadius(0);
                item.Padding = new Thickness(18, 12, 18, 12);
                item.Background = null;
                item.BorderThickness = new Thickness(0, group.Children.Count == 0 ? 0 : 1, 0, 0);
                group.Children.Add(item);
            }
        }
        _emptyState.Visibility = !hasHistory && _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Count == 0 && _search.Text.Length > 0)
        {
            var clearSearch = new Button { Content = "Clear search" };
            clearSearch.Click += (_, _) =>
            {
                _search.Text = string.Empty;
                _search.Focus(FocusState.Programmatic);
            };
            _entries.Children.Add(Card(Row(Text("No matching transcripts."), clearSearch)));
        }
        _more.Visibility = rows.Count >= _limit ? Visibility.Visible : Visibility.Collapsed;
    });
    private async Task OpenAsync(TranscriptHistoryEntry entry, Control opener)
    {
        const double lineHeight = 28;
        var fields = new StackPanel { Spacing = 12, MinWidth = 240 };
        var timestamp = entry.CreatedAtUtc.LocalDateTime;
        var final = Text(entry.Text, 19);
        final.IsTextSelectionEnabled = true;
        final.LineHeight = lineHeight;
        final.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        fields.Children.Add(final);
        if (entry.RawText != entry.Text)
        {
            var original = Text(entry.RawText);
            original.IsTextSelectionEnabled = true;
            original.LineHeight = lineHeight;
            original.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            fields.Children.Add(new Expander
            {
                Header = "Original recognition",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = original
            });
        }
        var page = new Border { Child = fields };
        fields.SizeChanged += (_, e) =>
        {
            double height = e.NewSize.Height;
            page.Padding = new Thickness(0, 0, 0, Math.Ceiling(height / lineHeight) * lineHeight - height);
        };
        var reader = new ScrollViewer { Name = "TranscriptReader", Content = page, IsTabStop = true, UseSystemFocusVisuals = false, VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
        AutomationProperties.SetName(reader, "Transcript text");
        var frame = Card(reader);
        frame.Padding = new Thickness(14, 8, 8, 8);
        reader.RegisterPropertyChangedCallback(Control.FocusStateProperty, (_, _) => frame.Style = (Style)Application.Current.Resources[
            reader.FocusState == FocusState.Keyboard ? "FocusedTranscriptFrame" : "LibraryCardStyle"]);
        var position = Text("", 14);
        position.Name = "ReaderEndStatus";
        position.HorizontalAlignment = HorizontalAlignment.Right;
        position.VerticalAlignment = VerticalAlignment.Center;
        position.FontWeight = FontWeights.SemiBold;
        reader.PreviewKeyDown += (_, args) =>
        {
            if (reader.FocusState == FocusState.Unfocused) return;
            double pageLines = Math.Max(1, Math.Floor(reader.ViewportHeight / lineHeight) - 1);
            double line = reader.VerticalOffset / lineHeight;
            double offset;
            switch (args.Key)
            {
                case Windows.System.VirtualKey.PageDown: offset = (Math.Floor(line) + pageLines) * lineHeight; break;
                case Windows.System.VirtualKey.PageUp: offset = (Math.Ceiling(line) - pageLines) * lineHeight; break;
                case Windows.System.VirtualKey.Home: offset = 0; break;
                case Windows.System.VirtualKey.End: offset = reader.ScrollableHeight; break;
                default: return;
            }
            args.Handled = true;
            reader.Focus(FocusState.Keyboard);
            reader.ChangeView(null, Math.Clamp(offset, 0, reader.ScrollableHeight), null, true);
        };
        var content = new Grid { RowSpacing = 8 };
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(Row(Text($"{timestamp:D}  ·  {timestamp:t}", 14), position));
        Grid.SetRow(frame, 1);
        content.Children.Add(frame);
        void UpdatePosition()
        {
            position.Visibility = reader.ScrollableHeight > 1 ? Visibility.Visible : Visibility.Collapsed;
            bool atEnd = reader.VerticalOffset >= reader.ScrollableHeight - 1;
            AutomationProperties.SetLiveSetting(position, atEnd ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
            position.Text = atEnd ? "End of transcript"
                : reader.VerticalOffset < 1 ? "Scroll to read more"
                : $"{Math.Clamp(reader.VerticalOffset / Math.Max(1, reader.ScrollableHeight) * 100, 0, 99):0}% scrolled";
        }
        reader.Loaded += (_, _) => UpdatePosition();
        reader.SizeChanged += (_, _) => UpdatePosition();
        fields.SizeChanged += (_, _) => UpdatePosition();
        reader.ViewChanged += (_, _) => UpdatePosition();
        UpdatePosition();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "Transcript",
            DefaultButton = ContentDialogButton.Primary,
            Content = content,
            CloseButtonText = "Close",
            PrimaryButtonText = "Copy final text"
        };
        dialog.Opened += (_, _) => dialog.DispatcherQueue.TryEnqueue(() => reader.Focus(FocusState.Programmatic));
        dialog.Resources["ContentDialogMaxWidth"] = 608d;
        ApplyDialogAccent(dialog);
        void SizeReader()
        {
            // Reserve space for the dialog chrome and keep the last visible line whole.
            reader.MaxHeight = lineHeight * Math.Clamp(Math.Floor((XamlRoot.Size.Height - 288) / lineHeight), 1, 11);
            content.Width = Math.Clamp(XamlRoot.Size.Width - 104, 0, 558);
        }
        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => SizeReader();
        SizeReader();
        XamlRoot.Changed += OnRootChanged;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) Copy(entry.Text);
        }
        finally { XamlRoot.Changed -= OnRootChanged; }
        if (opener.IsLoaded && opener.XamlRoot is not null) opener.Focus(FocusState.Keyboard);
    }
}
