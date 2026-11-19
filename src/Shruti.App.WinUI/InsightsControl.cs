using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Shruti.Storage;

namespace Shruti.App.WinUI;

public sealed class InsightsControl : LibraryPage
{
    private readonly TranscriptHistoryRepository _repository = new(AppDataPaths.CreateDefault());
    private readonly StackPanel _content = new() { Spacing = 18 };
    private int _generation;

    public InsightsControl() : base("Insights", "Dictation activity from the history saved on this PC.")
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
            _content.Children.Add(EmptyCard("\uE9D9", "No dictations yet", "Your first saved transcription starts your word count and daily activity."));
            return;
        }

        var metrics = new List<(string Value, string Label)>
        {
            (stats.Words.ToString("N0"), "Words dictated"),
            (stats.Dictations.ToString("N0"), "Dictations")
        };
        if (stats.AudioSeconds > 0 && stats.AudioSeconds < 60)
            metrics.Add((Math.Max(1, Math.Round(stats.AudioSeconds)).ToString("N0"), "Seconds recorded"));
        else if (stats.AudioSeconds >= 60)
            metrics.Add((TimeSpan.FromSeconds(stats.AudioSeconds).TotalMinutes.ToString("N1"), "Minutes recorded"));
        if (stats.Edits > 0) metrics.Add((stats.Edits.ToString("N0"), "Transcript edits"));
        var totals = new Grid { ColumnSpacing = 2, RowSpacing = 8 };
        for (int i = 0; i < metrics.Count; i++)
        {
            totals.ColumnDefinitions.Add(new());
            var cell = new StackPanel { Spacing = 1, Padding = new Thickness(14, 2, 10, 2) };
            cell.Children.Add(Secondary(metrics[i].Label));
            cell.Children.Add(Heading(metrics[i].Value, 32));
            Grid.SetColumn(cell, i);
            totals.Children.Add(cell);
        }
        var metricsCard = Card(totals);
        metricsCard.Padding = new Thickness(6, 13, 6, 13);
        _content.Children.Add(metricsCard);

        var activity = CreateActivity(stats);
        if (stats.Apps.Count == 0)
        {
            _content.Children.Add(activity);
        }
        else
        {
            var apps = CreateApps(stats);
            var charts = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
            charts.ColumnDefinitions.Add(new() { Width = new GridLength(3, GridUnitType.Star) });
            charts.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });
            charts.RowDefinitions.Add(new() { Height = GridLength.Auto });
            charts.RowDefinitions.Add(new() { Height = GridLength.Auto });
            charts.Children.Add(activity);
            charts.Children.Add(apps);
            void Arrange(double width)
            {
                bool stack = width < 660;
                Grid.SetColumnSpan(activity, stack ? 2 : 1);
                Grid.SetColumn(apps, stack ? 0 : 1);
                Grid.SetColumnSpan(apps, stack ? 2 : 1);
                Grid.SetRow(apps, stack ? 1 : 0);
            }
            Arrange(_content.ActualWidth);
            charts.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
            _content.Children.Add(charts);
        }
        _content.Children.Add(Secondary("Deleting a transcription also removes it from these totals."));
    });

    private Border CreateActivity(TranscriptInsights stats)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly end = today.AddDays(6 - (int)today.DayOfWeek);
        DateOnly start = end.AddDays(-90);
        var days = stats.Activity.Where(a => a.Date >= start && a.Date <= today).ToDictionary(a => a.Date);
        int peak = Math.Max(1, days.Values.Select(a => a.Dictations).DefaultIfEmpty(0).Max());

        var activity = new StackPanel { Spacing = 6 };
        activity.Children.Add(Heading("Daily activity", 20));
        activity.Children.Add(Secondary($"{start:MMM d} to {today:MMM d, yyyy}"));
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4, Margin = new Thickness(0, 12, 0, 6) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(32) });
        grid.RowDefinitions.Add(new() { Height = new GridLength(18) });
        for (int week = 0; week < 13; week++)
        {
            grid.ColumnDefinitions.Add(new());
            var first = start.AddDays(week * 7);
            if (week != 0 && first.Month != first.AddDays(-7).Month)
            {
                var month = Secondary(first.ToString("MMM"), 11);
                month.TextWrapping = TextWrapping.NoWrap;
                Grid.SetColumn(month, week + 1);
                Grid.SetColumnSpan(month, Math.Min(3, 13 - week));
                grid.Children.Add(month);
            }
        }
        for (int day = 0; day < 7; day++)
        {
            grid.RowDefinitions.Add(new() { Height = new GridLength(16) });
            if (day is 1 or 3 or 5)
            {
                var label = Secondary(start.AddDays(day).ToString("ddd"), 11);
                Grid.SetRow(label, day + 1);
                grid.Children.Add(label);
            }
        }
        for (int i = 0; i < 91; i++)
        {
            DateOnly date = start.AddDays(i);
            if (date > today) break;
            int count = days.GetValueOrDefault(date)?.Dictations ?? 0;
            var cell = new Border
            {
                Style = (Style)Application.Current.Resources[count > 0 ? "LibraryChartInk" : "LibraryChartTrack"],
                Opacity = count == 0 ? 1 : .45 + .55 * count / peak
            };
            string detail = $"{date:D}, {count} {(count == 1 ? "dictation" : "dictations")}";
            ToolTipService.SetToolTip(cell, detail);
            AutomationProperties.SetName(cell, detail);
            Grid.SetColumn(cell, i / 7 + 1);
            Grid.SetRow(cell, i % 7 + 1);
            grid.Children.Add(cell);
        }
        activity.Children.Add(grid);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        foreach (int count in new[] { 0, (peak + 1) / 2, peak }.Distinct())
        {
            var swatch = new Border
            {
                Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources[count == 0 ? "LibraryChartTrack" : "LibraryChartInk"],
                Opacity = count == 0 ? 1 : .45 + .55 * count / peak
            };
            var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            entry.Children.Add(swatch);
            entry.Children.Add(Secondary(count.ToString("N0"), 12));
            legend.Children.Add(entry);
        }
        legend.Children.Add(Secondary("per day", 12));
        var frequency = Secondary(days.Count == 1 ? "1 active day" : $"{days.Count:N0} active days");
        activity.Children.Add(Row(frequency, legend));
        var card = Card(activity);
        card.Padding = new Thickness(19);
        return card;
    }

    private Border CreateApps(TranscriptInsights stats)
    {
        var apps = new StackPanel { Spacing = 13 };
        apps.Children.Add(Heading("Where you dictate", 20));
        apps.Children.Add(Secondary("Share of all saved dictations"));
        foreach (var app in stats.Apps)
        {
            var name = Text(app.Name, 14);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxLines = 1;
            double share = (double)app.Dictations / stats.Dictations;
            var count = Secondary($"{app.Dictations:N0} of {stats.Dictations:N0} · {share:P0}");
            var meter = new Grid { Height = 5, Margin = new Thickness(0, 4, 0, 0) };
            meter.ColumnDefinitions.Add(new() { Width = new GridLength(app.Dictations, GridUnitType.Star) });
            meter.ColumnDefinitions.Add(new() { Width = new GridLength(Math.Max(0, stats.Dictations - app.Dictations), GridUnitType.Star) });
            var track = new Border { Style = (Style)Application.Current.Resources["LibraryChartTrack"] };
            Grid.SetColumnSpan(track, 2);
            meter.Children.Add(track);
            meter.Children.Add(new Border { Style = (Style)Application.Current.Resources["LibraryChartInk"] });
            AutomationProperties.SetName(meter, $"{app.Dictations} of {stats.Dictations} dictations");
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(Row(name, count));
            row.Children.Add(meter);
            apps.Children.Add(row);
        }
        var card = Card(apps);
        card.Padding = new Thickness(19);
        return card;
    }
}
