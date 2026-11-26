using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Shruti.Storage;

namespace Shruti.App.WinUI;

public sealed class InsightsControl : LibraryPage
{
    private readonly TranscriptHistoryRepository _repository = new(AppDataPaths.CreateDefault());
    private readonly StackPanel _content = new() { Spacing = 12 };
    private int _generation;

    public InsightsControl() : base("Insights", "Dictation activity from the history saved on this PC.")
    {
        Body.Spacing = 14;
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
        var totals = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        bool? compactTotals = null;
        void ArrangeTotals(double width)
        {
            bool compact = width < 590;
            if (compactTotals == compact) return;
            compactTotals = compact;
            totals.Children.Clear();
            totals.ColumnDefinitions.Clear();
            totals.RowDefinitions.Clear();
            int columns = compact ? Math.Min(2, metrics.Count) : metrics.Count;
            for (int i = 0; i < columns; i++) totals.ColumnDefinitions.Add(new());
            for (int i = 0; i < metrics.Count; i++)
            {
                if (i % columns == 0) totals.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var label = Secondary(metrics[i].Label);
                var value = Heading(metrics[i].Value, compact ? 21 : 26);
                Grid cell = compact ? Row(label, value) : new Grid();
                if (!compact)
                {
                    cell.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    cell.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    cell.Children.Add(label);
                    cell.Children.Add(value);
                    Grid.SetRow(value, 1);
                }
                label.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(cell, i / columns);
                Grid.SetColumn(cell, i % columns);
                totals.Children.Add(cell);
            }
        }
        ArrangeTotals(_content.ActualWidth);
        totals.SizeChanged += (_, e) => ArrangeTotals(e.NewSize.Width);
        var metricsCard = Card(totals);
        metricsCard.Padding = new Thickness(16, 9, 12, 9);
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
                bool stack = width < 545;
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
        var content = new Grid();
        var card = Card(content);
        card.Padding = new Thickness(14);
        bool? compact = null;
        bool expanded = false;
        Button? range = null;
        void Render()
        {
            content.Children.Clear();
            var header = Heading("Daily activity", 20);
            UIElement title = header;
            if (compact == true)
            {
                range = new Button { Name = "ExpandActivityCalendar", Content = expanded ? "Last 7 days" : "Calendar" };
                AutomationProperties.SetName(range, expanded ? "Show last 7 days" : "Show activity calendar");
                range.Click += (sender, _) =>
                {
                    var focus = ((Button)sender).FocusState;
                    expanded = !expanded;
                    Render();
                    range.Focus(focus == FocusState.Unfocused ? FocusState.Programmatic : focus);
                };
                title = Row(header, range);
            }
            content.Children.Add(compact == true && !expanded
                ? ActivityWeek(stats, title)
                : ActivityCalendar(stats, compact == true ? 8 : 13, title));
        }
        void Arrange(double width)
        {
            bool narrow = width < 355;
            if (compact == narrow) return;
            compact = narrow;
            Render();
        }
        Arrange(400);
        content.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        return card;
    }

    private UIElement ActivityWeek(TranscriptInsights stats, UIElement title)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly start = today.AddDays(-6);
        var days = stats.Activity.Where(day => day.Date >= start && day.Date <= today).ToDictionary(day => day.Date);
        var activity = new StackPanel { Spacing = 7 };
        activity.Children.Add(title);
        activity.Children.Add(Secondary($"{start:MMM d} to {today:MMM d} · dictations", 14));
        var week = new Grid { Name = "ActivityWeek", ColumnSpacing = 3 };
        for (int i = 0; i < 7; i++)
        {
            week.ColumnDefinitions.Add(new());
            DateOnly date = start.AddDays(i);
            int count = days.GetValueOrDefault(date)?.Dictations ?? 0;
            var day = Secondary(date.ToString("ddd"), 14);
            day.TextAlignment = TextAlignment.Center;
            day.TextWrapping = TextWrapping.NoWrap;
            var value = Heading(count.ToString("N0"), 16);
            value.TextAlignment = TextAlignment.Center;
            var column = new StackPanel { Spacing = 3 };
            column.Children.Add(day);
            column.Children.Add(value);
            string detail = $"{date:D}, {count} {(count == 1 ? "dictation" : "dictations")}";
            AutomationProperties.SetName(column, detail);
            ToolTipService.SetToolTip(column, detail);
            Grid.SetColumn(column, i);
            week.Children.Add(column);
        }
        activity.Children.Add(week);
        return activity;
    }

    private UIElement ActivityCalendar(TranscriptInsights stats, int weeks, UIElement title)
    {
        bool compact = weeks == 8;
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly end = today.AddDays(6 - (int)today.DayOfWeek);
        DateOnly start = end.AddDays(1 - weeks * 7);
        var days = stats.Activity.Where(a => a.Date >= start && a.Date <= today).ToDictionary(a => a.Date);

        var activity = new StackPanel { Spacing = 6 };
        activity.Children.Add(title);
        activity.Children.Add(Secondary($"{start:MMM d} to {today:MMM d, yyyy}"));
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = compact ? 2 : 3, Margin = new Thickness(0, 3, 0, 0) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(36) });
        grid.RowDefinitions.Add(new() { Height = new GridLength(22) });
        for (int week = 0; week < weeks; week++)
        {
            grid.ColumnDefinitions.Add(new());
            var first = start.AddDays(week * 7);
            if ((week == 0 || first.Month != first.AddDays(-7).Month) && (week + 2 >= weeks || first.Month == first.AddDays(7).Month))
            {
                var month = Text(first.ToString("MMM"), 14);
                month.TextWrapping = TextWrapping.NoWrap;
                Grid.SetColumn(month, week + 1);
                Grid.SetColumnSpan(month, Math.Min(2, weeks - week));
                grid.Children.Add(month);
            }
        }
        for (int day = 0; day < 7; day++)
        {
            grid.RowDefinitions.Add(new() { Height = new GridLength(compact ? 20 : 22) });
            if (day is 1 or 3 or 5)
            {
                var label = Text(start.AddDays(day).ToString("ddd"), 14);
                Grid.SetRow(label, day + 1);
                grid.Children.Add(label);
            }
        }
        for (int i = 0; i < weeks * 7; i++)
        {
            DateOnly date = start.AddDays(i);
            if (date > today) break;
            int count = days.GetValueOrDefault(date)?.Dictations ?? 0;
            var cell = new Border
            {
                Style = (Style)Application.Current.Resources[count > 0 ? "LibraryCalendarDay" : "LibraryChartTrack"]
            };
            if (count > 0)
            {
                var number = new TextBlock
                {
                    Text = count.ToString("N0"),
                    Style = (Style)Application.Current.Resources["LibraryCalendarCount"]
                };
                cell.Child = new Viewbox
                {
                    StretchDirection = Microsoft.UI.Xaml.Controls.StretchDirection.DownOnly,
                    Child = number
                };
            }
            string detail = $"{date:D}, {count} {(count == 1 ? "dictation" : "dictations")}";
            ToolTipService.SetToolTip(cell, detail);
            AutomationProperties.SetName(cell, detail);
            Grid.SetColumn(cell, i / 7 + 1);
            Grid.SetRow(cell, i % 7 + 1);
            grid.Children.Add(cell);
        }
        activity.Children.Add(grid);
        string frequency = days.Count == 1 ? "1 active day" : $"{days.Count:N0} active days";
        activity.Children.Insert(2, Text($"{frequency} · Dictations per day", 14));
        return activity;
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
        card.Padding = new Thickness(16);
        return card;
    }
}
