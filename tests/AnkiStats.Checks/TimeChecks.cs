using AnkiStats.Core;

internal static class TimeChecks
{
    public static void Run(Action<bool, string> check)
    {
        ReviewRecord Review(string instant, int duration, int kind = 1, int rating = 3) => new(DateTimeOffset.Parse(instant).ToUnixTimeMilliseconds(), 1, rating, duration, kind);
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        var data = new ImportedCollection { TimeZoneId = "UTC", Reviews = [
            Review("2026-01-01T12:00Z", 3600000, 0), Review("2026-05-01T12:00Z", 60000, 1), Review("2026-05-02T12:00Z", 120000, 2),
            Review("2026-11-01T03:59Z", 1000, 3), Review("2026-11-01T04:00Z", 3000), Review("2026-11-02T12:00Z", 2000),
            Review("2026-11-02T11:00Z", 0), Review("2026-11-02T11:01Z", -1000), Review("2026-11-02T11:02Z", 99999, 4),
            Review("2026-11-02T11:03Z", 99999, 99), Review("2026-11-02T11:04Z", 99999, 1, 0), Review("2026-11-03T12:00Z", 99999)] };
        var result = TimeAnalyzer.Analyze(data, TimeRange.Last30Days, now);
        check(result.AllHistory.Milliseconds == 3786000 && result.AllHistory.TimedAnswers == 6 && result.AllHistory.Answers == 8,
            "time totals include rated answer kinds and exclude nonpositive durations, invalid ratings, admin, unknown, and future events");
        check(result.AllHistory.Start == new DateOnly(2026, 1, 1) && result.AllHistory.End == new DateOnly(2026, 11, 2), "all-history range extends through current study day");
        check(result.Recent.Start == new DateOnly(2026, 5, 2) && result.Recent.CalendarDays == 185, "six months uses calendar months and exact inclusive dates");
        check(result.Recent.Milliseconds == 126000 && result.Recent.MeanSeconds == 31.5 && result.Recent.MedianSeconds == 2.5, "recent average and median use individual positive durations");
        check(result.Selected.Start == new DateOnly(2026, 10, 4) && result.Selected.CalendarDays == 30, "30-day filter includes today and 29 previous study dates");
        check(result.Selected.Milliseconds == 6000 && result.Selected.Answers == 5 && result.Selected.TimedAnswers == 3, "missing durations keep activity and answer counts but do not dilute mean time");
        check(result.Selected.MeanSeconds == 2 && result.Selected.MedianSeconds == 2, "odd median and mean");
        check(result.Selected.MinutesPerDay == 6000 / 60000.0 / 30 && result.Selected.MinutesPerWeek == result.Selected.MinutesPerDay * 7,
            "calendar-day and seven-day averages include zero-review dates");
        check(result.Daily.Count == 30 && result.Daily[0].Minutes == 0 && result.Daily[0].Stats.Answers == 0, "daily series fills zero-review gaps");
        check(result.Daily.Single(d => d.Start == new DateOnly(2026, 10, 31)).Stats.Milliseconds == 1000, "before 4 AM belongs to previous study date");
        check(result.Daily.Single(d => d.Start == new DateOnly(2026, 11, 1)).Stats.Milliseconds == 3000, "4 AM starts new study date");
        check(result.Weekly.Sum(w => w.Stats.Milliseconds) == result.Selected.Milliseconds && result.Weekly.First().Start == result.Selected.Start && result.Weekly.Last().End == result.Selected.End, "weekly totals preserve selection and partial edge weeks");
        check(result.Weekly.First().Start == result.Weekly.First().End && result.Weekly.Last().Start.DayOfWeek == DayOfWeek.Monday, "weeks are Monday through Sunday study dates");
        check(result.Weekdays.Sum(w => w.Stats.Milliseconds) == result.Selected.Milliseconds && result.Weekdays.Sum(w => w.Occurrences) == 30, "weekday breakdown reconciles time and date denominators");
        check(result.Weekdays[5].Period == "Saturday" && result.Weekdays[5].Stats.Milliseconds == 1000, "weekday follows study date rather than timestamp weekday");
        check(result.Hours[3].Stats.Milliseconds == 1000 && result.Hours[4].Stats.Milliseconds == 3000 && result.Hours.Sum(h => h.Stats.Milliseconds) == 6000, "hour breakdown retains local clock hours and reconciles totals");
        check(TimeAnalyzer.Analyze(data, TimeRange.Last90Days, now).Selected.CalendarDays == 90, "90-day filter");
        check(TimeAnalyzer.Analyze(data, TimeRange.Last6Months, now).Selected.Milliseconds == result.Recent.Milliseconds, "six-month filter matches recent comparison");
        var young = new ImportedCollection { TimeZoneId = "UTC", Reviews = [Review("2026-11-01T12:00Z", 1000)] };
        check(TimeAnalyzer.Analyze(young, TimeRange.Last6Months, now).Selected.CalendarDays == 2, "averages do not invent pre-history zero days for young profiles");
        var empty = TimeAnalyzer.Analyze(new() { TimeZoneId = "UTC" }, TimeRange.Last6Months, now);
        check(empty.Selected.MeanSeconds is null && empty.Selected.MedianSeconds is null && empty.Selected.HoursDisplay == "No data", "empty timing is no data");
        var missing = TimeAnalyzer.Analyze(new() { TimeZoneId = "UTC", Reviews = [Review("2026-11-01T12:00Z", 0)] }, clock: now);
        check(missing.AllHistory.Answers == 1 && missing.AllHistory.ActiveDays == 1 && missing.AllHistory.MeanSeconds is null && missing.Daily[0].MinutesDisplay == "No data", "only missing durations remains no data while retaining activity");
        var large = TimeAnalyzer.Analyze(new() { TimeZoneId = "UTC", Reviews = [Review("2026-11-01T12:00Z", int.MaxValue), Review("2026-11-02T12:00Z", int.MaxValue)] }, clock: now);
        check(large.AllHistory.Milliseconds == 2L * int.MaxValue && large.AllHistory.MedianSeconds == int.MaxValue / 1000.0, "large time totals and even median do not overflow");
        var dst = new ImportedCollection { TimeZoneId = "Eastern Standard Time", Reviews = [Review("2026-11-01T05:30Z", 1000), Review("2026-11-01T06:30Z", 3000), Review("2026-11-01T09:00Z", 2000)] };
        var fall = TimeAnalyzer.Analyze(dst, clock: now);
        check(fall.Hours[1].Stats.Milliseconds == 4000 && fall.Daily.Single(d => d.Start == new DateOnly(2026, 10, 31)).Stats.Milliseconds == 4000, "fall DST repeated hours combine on prior study day");
        var spring = TimeAnalyzer.Analyze(new() { TimeZoneId = "Eastern Standard Time", Reviews = [Review("2026-03-08T07:30Z", 1000), Review("2026-03-08T08:00Z", 2000)] }, clock: DateTimeOffset.Parse("2026-03-08T12:00Z"));
        check(spring.Daily[0].Start == new DateOnly(2026, 3, 7) && spring.Daily[0].Stats.Milliseconds == 1000 && spring.Hours[3].Stats.Milliseconds == 1000, "spring DST cutoff uses wall time before subtracting four hours");
        var monthEnd = TimeAnalyzer.Analyze(data, TimeRange.Last6Months, DateTimeOffset.Parse("2026-08-31T12:00Z"));
        check(monthEnd.Selected.Start == new DateOnly(2026, 2, 28), "six-month subtraction handles short months");
        var beforeCutoff = TimeAnalyzer.Analyze(data, clock: DateTimeOffset.Parse("2026-11-02T03:59Z"));
        check(beforeCutoff.Selected.End == new DateOnly(2026, 11, 1), "analysis clock before cutoff ends on previous study date");
    }
}
