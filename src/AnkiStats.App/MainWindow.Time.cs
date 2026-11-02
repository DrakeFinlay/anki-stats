using System.Windows;
using System.Windows.Controls;
using AnkiStats.Core;

namespace AnkiStats.App;

public partial class MainWindow
{
    private sealed record TimeComparisonMetric(string Metric, string AllHistory, string Recent);
    private TimeAnalysisResult? timeAnalysis;
    private async Task UpdateTimeAnalysis()
    {
        if (data is null) return;
        var range = TimeRangePicker.SelectedIndex switch { 1 => TimeRange.Last30Days, 2 => TimeRange.Last90Days, 3 => TimeRange.Last6Months, _ => TimeRange.AllHistory };
        timeAnalysis = await Task.Run(() => TimeAnalyzer.Analyze(data, range));
        var selected = timeAnalysis.Selected;
        RecordedHours.Text = timeAnalysis.AllHistory.HoursDisplay;
        RecordedTimeScope.Text = $"{timeAnalysis.AllHistory.TimedAnswers:N0} timed answers · all history\nAnki's recorded durations; may be capped";
        SelectedHours.Text = selected.HoursDisplay;
        SelectedDailyTime.Text = selected.MinutesPerDay is null ? "No data" : selected.DailyDisplay + " min/day";
        SelectedWeeklyTime.Text = selected.MinutesPerWeek is null ? "No data" : selected.WeeklyDisplay + " min/week";
        SelectedAnswerTime.Text = selected.MeanSeconds is null ? "No data" : selected.MeanDisplay + " / " + selected.MedianDisplay + " s";
        TimeScope.Text = $"{data.Scope}\n{selected.Start:MMM d, yyyy} – {selected.End:MMM d, yyyy} · {selected.CalendarDays:N0} calendar days · {selected.ActiveDays:N0} active days · {selected.TimedAnswers:N0} of {selected.Answers:N0} answers have positive recorded durations. {data.TimeZoneId} · 4 AM study-day cutoff.";
        var all = timeAnalysis.AllHistory;
        var recent = timeAnalysis.Recent;
        TimeComparisonGrid.ItemsSource = new[]
        {
            new TimeComparisonMetric("From", all.Start.ToString("MMM d, yyyy"), recent.Start.ToString("MMM d, yyyy")),
            new("Through", all.End.ToString("MMM d, yyyy"), recent.End.ToString("MMM d, yyyy")),
            new("Recorded review time", all.HoursDisplay, recent.HoursDisplay),
            new("Average minutes per day", all.DailyDisplay, recent.DailyDisplay),
            new("Average minutes per seven days", all.WeeklyDisplay, recent.WeeklyDisplay),
            new("Average seconds per answer", all.MeanDisplay, recent.MeanDisplay),
            new("Median seconds per answer", all.MedianDisplay, recent.MedianDisplay),
            new("Calendar study days", all.CalendarDays.ToString("N0"), recent.CalendarDays.ToString("N0")),
            new("Active study days", all.ActiveDays.ToString("N0"), recent.ActiveDays.ToString("N0")),
            new("Answers with recorded durations", all.TimedAnswers.ToString("N0"), recent.TimedAnswers.ToString("N0"))
        };
        WeekdayTimeGrid.ItemsSource = timeAnalysis.Weekdays;
        HourlyTimeGrid.ItemsSource = timeAnalysis.Hours;
        ShowTimeChart();
    }
    private void ShowTimeChart()
    {
        if (timeAnalysis is null) return;
        var weekly = TimeGroupingPicker.SelectedIndex == 1;
        var all = weekly ? timeAnalysis.AllHistory.MinutesPerWeek : timeAnalysis.AllHistory.MinutesPerDay;
        var recent = weekly ? timeAnalysis.Recent.MinutesPerWeek : timeAnalysis.Recent.MinutesPerDay;
        var rows = weekly ? timeAnalysis.Weekly : timeAnalysis.Daily;
        TimeHistoryChart.SetRows(rows, all, recent);
        TimePeriodsGrid.ItemsSource = rows;
        string Minutes(double? value) => value is {} number ? $"{number:N1} min" : "No data";
        AllTimeAverageLegend.Text = "All-history average: " + Minutes(all);
        RecentTimeAverageLegend.Text = "Last 6 months average: " + Minutes(recent);
        TimeChartCaption.Text = weekly ? "Recorded minutes per Monday–Sunday study week. First and last weeks may be partial; average lines represent seven full days. Long series average adjacent weeks per bar."
            : "Recorded minutes per study day, including days with no answers. Long series average adjacent days per bar.";
    }
    private async void TimeRangeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready && !busy && data is not null) await Guard(UpdateTimeAnalysis);
    }
    private void TimeGroupingChanged(object sender, SelectionChangedEventArgs e) { if (ready) ShowTimeChart(); }
}
