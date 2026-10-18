using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Shruti.Storage;

namespace Shruti.App.WinUI;

public sealed class InsightsControl : LibraryPage
{
    private readonly TranscriptHistoryRepository _repository = new(AppDataPaths.CreateDefault());
    private readonly StackPanel _content = new() { Spacing = 24 };
    private int _generation;
    public InsightsControl() : base("Insights", "Usage from your saved transcriptions. Deleting history removes it from these totals.")
    {
        Body.Children.Add(_content);
        Actions.Children.Add(Action("Refresh", RefreshAsync));
    }
    public override Task RefreshAsync() => RunAsync(async () =>
    {
        int generation = ++_generation;
        var stats = await _repository.GetInsightsAsync();
        if (generation != _generation) return;
        _content.Children.Clear();
        if (stats.Dictations == 0)
        {
            _content.Children.Add(Card(Text("Dictate something to start your usage record. Insights will appear as you use Shruti.")));
            return;
        }
        var cards = new Grid { ColumnSpacing = 20, RowSpacing = 20 };
        cards.ColumnDefinitions.Add(new()); cards.ColumnDefinitions.Add(new());
        var metrics = new List<(string Value, string Label)> { (stats.Words.ToString("N0"), "Words produced"), (stats.Dictations.ToString("N0"), "Dictations") };
        if (stats.AudioSeconds > 0) metrics.Add((TimeSpan.FromSeconds(stats.AudioSeconds).TotalMinutes.ToString("N1"), "Minutes recorded"));
        if (stats.Edits > 0) metrics.Add((stats.Edits.ToString("N0"), "Transcript edits"));
        for (int i = 0; i < metrics.Count; i++)
        {
            if (i % 2 == 0) cards.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(Text(metrics[i].Value, 36)); stack.Children.Add(Text(metrics[i].Label));
            var card = Card(stack); Grid.SetRow(card, i / 2); Grid.SetColumn(card, i % 2); cards.Children.Add(card);
        }
        _content.Children.Add(cards);
        var activity = new StackPanel { Spacing = 16 };
        activity.Children.Add(Text("Daily activity", 23));
        activity.Children.Add(Text("The last 13 weeks. Each square is one day; darker squares mean more dictations."));
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        DateOnly end = today.AddDays(6 - (int)today.DayOfWeek);
        DateOnly start = end.AddDays(-90);
        var days = stats.Activity.ToDictionary(a => a.Date);
        var grid = new Grid { ColumnSpacing = 5, RowSpacing = 5, HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(36) });
        for (int week = 0; week < 13; week++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(20) });
        int peak = Math.Max(1, stats.Activity.Where(a => a.Date >= start && a.Date <= today).Select(a => a.Dictations).DefaultIfEmpty(1).Max());
        for (int d = 0; d < 7; d++)
        {
            grid.RowDefinitions.Add(new() { Height = new GridLength(20) });
            var label = Text(new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }[d], 11);
            Grid.SetRow(label, d); grid.Children.Add(label);
        }
        for (int i = 0; i < 91; i++)
        {
            DateOnly date = start.AddDays(i); int count = days.GetValueOrDefault(date)?.Dictations ?? 0;
            var cell = new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(count == 0
                ? Windows.UI.Color.FromArgb(70, 140, 130, 120)
                : Windows.UI.Color.FromArgb((byte)(90 + 165.0 * count / peak), 222, 110, 30)),
                Opacity = date > today ? .2 : 1
            };
            ToolTipService.SetToolTip(cell, $"{date:D}: {count} dictations");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell, $"{date:D}: {count} dictations");
            Grid.SetColumn(cell, i / 7 + 1); Grid.SetRow(cell, i % 7); grid.Children.Add(cell);
        }
        activity.Children.Add(grid);
        activity.Children.Add(Text($"{stats.Activity.Count:N0} active days in saved history", 13));
        _content.Children.Add(Card(activity));
        if (stats.Apps.Count > 0)
        {
            var apps = new StackPanel { Spacing = 16 };
            apps.Children.Add(Text("Where you dictate", 23));
            apps.Children.Add(Text("Dictations by destination app. Browser usage is grouped under the browser."));
            foreach (var app in stats.Apps)
            {
                var row = new StackPanel { Spacing = 6 };
                row.Children.Add(Text($"{app.Name}   ·   {app.Dictations:N0}"));
                row.Children.Add(new ProgressBar { Minimum = 0, Maximum = stats.Dictations, Value = app.Dictations, Height = 6 });
                apps.Children.Add(row);
            }
            _content.Children.Add(Card(apps));
        }
    });
}
