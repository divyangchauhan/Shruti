using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.Storage;

namespace Shruti.App.WinUI;

public sealed class HistoryControl : LibraryPage
{
    private readonly TranscriptHistoryRepository _repository = new(AppDataPaths.CreateDefault());
    private readonly StackPanel _entries = new() { Spacing = 12 };
    private readonly TextBox _search = new() { PlaceholderText = "Search transcriptions", MaxLength = 256 };
    private readonly TextBlock _summary = Text("");
    private readonly Button _more;
    private int _generation;
    private int _limit = 100;
    public HistoryControl() : base("Transcriptions", "Your dictations, saved on this PC. Copy a transcript or open it to see the original.")
    {
        Actions.Children.Add(Action("Clear history", async () =>
        {
            if (await ConfirmAsync("Clear transcription history?", "This deletes all saved transcripts and their usage statistics from this PC. Dictionary entries and snippets stay saved."))
            { await _repository.DeleteAsync(null); await RefreshAsync(); }
        }));
        Body.Children.Add(_summary); Body.Children.Add(_search); Body.Children.Add(_entries);
        _more = Action("Show more", async () => { _limit += 100; await RefreshAsync(); });
        Body.Children.Add(_more);
        _search.TextChanged += async (_, _) => { _limit = 100; await RefreshAsync(); };
    }
    public override Task RefreshAsync() => RunAsync(async () =>
    {
        int generation = ++_generation;
        var rows = new List<TranscriptHistoryEntry>();
        for (int offset = 0; offset < _limit; offset += 100)
            rows.AddRange(await _repository.ListAsync(_search.Text, 100, offset));
        var stats = await _repository.GetInsightsAsync();
        if (generation != _generation) return;
        _summary.Text = $"{stats.Words:N0} words produced   ·   {stats.Dictations:N0} dictations";
        _entries.Children.Clear();
        foreach (var day in rows.GroupBy(r => r.CreatedAtUtc.LocalDateTime.Date))
        {
            var heading = Text(day.Key.ToString("D"), 14); heading.Margin = new Thickness(0, 16, 0, 4); heading.Opacity = .65;
            _entries.Children.Add(heading);
            foreach (var entry in day)
            {
                var content = new Grid { ColumnSpacing = 20 };
                content.ColumnDefinitions.Add(new() { Width = new GridLength(70) });
                content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                var time = Text(entry.CreatedAtUtc.LocalDateTime.ToString("t"), 13); time.Opacity = .6; content.Children.Add(time);
                var preview = Text(entry.Text); preview.MaxLines = 5; preview.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(preview, 1); content.Children.Add(preview);
                var menu = PopupAppearance.CreateMenuFlyout();
                var open = new MenuFlyoutItem { Text = "View transcript" };
                open.Click += async (_, _) => await RunAsync(() => OpenAsync(entry)); menu.Items.Add(open);
                var delete = new MenuFlyoutItem { Text = "Delete" };
                delete.Click += async (_, _) => await RunAsync(async () =>
                {
                    if (await ConfirmAsync("Delete this transcript?", "This also removes it from Insights."))
                    { await _repository.DeleteAsync(entry.Id); await RefreshAsync(); }
                }); menu.Items.Add(delete);
                _entries.Children.Add(Card(Row(content,
                    Action("Copy", () => { Copy(entry.Text); Message.Text = "Transcript copied."; return Task.CompletedTask; }),
                    new Button { Content = "More", Flyout = menu })));
            }
        }
        if (rows.Count == 0) _entries.Children.Add(Card(Text(_search.Text.Length > 0 ? "No matching transcripts." : "Your first dictation will appear here. Use your dictation shortcut or the floating bar to start.")));
        _more.Visibility = rows.Count >= _limit ? Visibility.Visible : Visibility.Collapsed;
    });
    private async Task OpenAsync(TranscriptHistoryEntry entry)
    {
        var fields = new StackPanel { Spacing = 16 };
        fields.Children.Add(Text("Final text", 18));
        fields.Children.Add(new TextBox { Text = entry.Text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 240 });
        if (entry.RawText != entry.Text)
        {
            fields.Children.Add(Text("Original recognition", 18));
            fields.Children.Add(new TextBox { Text = entry.RawText, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180 });
        }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = entry.CreatedAtUtc.LocalDateTime.ToString("g"),
            Content = new ScrollViewer { Content = fields, MaxHeight = 460 },
            CloseButtonText = "Close",
            PrimaryButtonText = "Copy final text"
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) Copy(entry.Text);
    }
}
