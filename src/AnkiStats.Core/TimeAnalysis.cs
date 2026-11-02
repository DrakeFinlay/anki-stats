namespace AnkiStats.Core;

public enum TimeRange { AllHistory, Last30Days, Last90Days, Last6Months }

public sealed record TimeSummary(string Period, DateOnly Start, DateOnly End, int Answers, int TimedAnswers,
    long Milliseconds, int ActiveDays, double? MeanSeconds, double? MedianSeconds)
{
    public int CalendarDays => End.DayNumber - Start.DayNumber + 1;
    public double? MinutesPerDay => TimedAnswers > 0 ? Milliseconds / 60000.0 / CalendarDays : null;
    public double? MinutesPerWeek => MinutesPerDay * 7;
    public string HoursDisplay => TimedAnswers > 0 ? $"{Milliseconds / 3600000.0:N1} h" : "No data";
    public string DailyDisplay => MinutesPerDay is {} value ? $"{value:N1}" : "No data";
    public string WeeklyDisplay => MinutesPerWeek is {} value ? $"{value:N1}" : "No data";
    public string MeanDisplay => MeanSeconds is {} value ? $"{value:N2}" : "No data";
    public string MedianDisplay => MedianSeconds is {} value ? $"{value:N2}" : "No data";
}

public sealed record TimePeriodRow(string Period, DateOnly Start, DateOnly End, TimeSummary Stats)
{
    public double Minutes => Stats.Milliseconds / 60000.0;
    public string MinutesDisplay => Stats.Answers > 0 && Stats.TimedAnswers == 0 ? "No data" : $"{Minutes:N1}";
}

public sealed record TimeBreakdown(string Period, TimeSummary Stats, int Occurrences)
{
    public string AverageMinutes => Stats.TimedAnswers > 0 ? $"{Stats.Milliseconds / 60000.0 / Occurrences:N1}" : "No data";
}

public sealed record TimeAnalysisResult(TimeSummary AllHistory, TimeSummary Recent, TimeSummary Selected,
    List<TimePeriodRow> Daily, List<TimePeriodRow> Weekly, List<TimeBreakdown> Weekdays, List<TimeBreakdown> Hours);

public static class TimeAnalyzer
{
    private sealed record Answer(DateOnly Day, int Hour, int Milliseconds);
    public static TimeAnalysisResult Analyze(ImportedCollection data, TimeRange range = TimeRange.AllHistory, DateTimeOffset? clock = null)
    {
        var now = clock ?? DateTimeOffset.UtcNow;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(data.TimeZoneId);
        var today = Streaks.StudyDay(now, zone);
        var answers = data.Reviews.Where(r => r.Kind is >= 0 and <= 3 && r.Rating is >= 1 and <= 4 && r.Id <= now.ToUnixTimeMilliseconds())
            .Select(r => new Answer(Streaks.StudyDay(DateTimeOffset.FromUnixTimeMilliseconds(r.Id), zone),
                TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(r.Id), zone).Hour, r.Milliseconds)).ToList();
        var first = answers.Count == 0 ? today : answers.Min(a => a.Day);
        var recentStart = Max(first, today.AddMonths(-6));
        var start = range switch
        {
            TimeRange.Last30Days => Max(first, today.AddDays(-29)),
            TimeRange.Last90Days => Max(first, today.AddDays(-89)),
            TimeRange.Last6Months => recentStart,
            _ => first
        };
        var selected = answers.Where(a => a.Day >= start).ToList();
        var allStats = Summarize("All history", first, today, answers);
        var recentStats = Summarize("Last 6 months", recentStart, today, answers.Where(a => a.Day >= recentStart));
        var selectedStats = Summarize("Selected range", start, today, selected);
        var byDay = selected.ToLookup(a => a.Day);
        var daily = new List<TimePeriodRow>();
        for (var number = start.DayNumber; number <= today.DayNumber; number++)
        {
            var day = DateOnly.FromDayNumber(number);
            daily.Add(new(day.ToString("MMM d, yyyy"), day, day, Summarize("Day", day, day, byDay[day])));
        }
        var weekly = daily.GroupBy(d => d.Start.AddDays(-(((int)d.Start.DayOfWeek + 6) % 7))).Select(group =>
        {
            var from = group.First().Start;
            var to = group.Last().End;
            return new TimePeriodRow($"{from:MMM d, yyyy} – {to:MMM d, yyyy}", from, to,
                Summarize("Week", from, to, group.SelectMany(day => byDay[day.Start])));
        }).ToList();
        var weekdays = Enumerable.Range(0, 7).Select(i =>
        {
            var weekday = (DayOfWeek)((i + 1) % 7);
            return new TimeBreakdown(weekday.ToString(), Summarize("Weekday", start, today, selected.Where(a => a.Day.DayOfWeek == weekday)),
                daily.Count(d => d.Start.DayOfWeek == weekday));
        }).ToList();
        var hours = Enumerable.Range(0, 24).Select(hour => new TimeBreakdown($"{hour:00}:00–{hour:00}:59",
            Summarize("Hour", start, today, selected.Where(a => a.Hour == hour)), daily.Count)).ToList();
        return new(allStats, recentStats, selectedStats, daily, weekly, weekdays, hours);
    }
    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static TimeSummary Summarize(string period, DateOnly start, DateOnly end, IEnumerable<Answer> source)
    {
        var answers = source.ToList();
        var times = answers.Where(a => a.Milliseconds > 0).Select(a => a.Milliseconds).Order().ToArray();
        var total = times.Sum(t => (long)t);
        double? median = times.Length == 0 ? null : times.Length % 2 == 0
            ? ((long)times[times.Length / 2 - 1] + times[times.Length / 2]) / 2000.0 : times[times.Length / 2] / 1000.0;
        return new(period, start, end, answers.Count, times.Length, total, answers.Select(a => a.Day).Distinct().Count(),
            times.Length > 0 ? total / 1000.0 / times.Length : null, median);
    }
}
