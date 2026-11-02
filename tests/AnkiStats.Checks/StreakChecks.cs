using AnkiStats.Core;

internal static class StreakChecks
{
    public static void Run(Action<bool, string> check)
    {
        long Stamp(string date) => DateTimeOffset.Parse(date).ToUnixTimeMilliseconds();
        var now = DateTimeOffset.Parse("2026-07-02T03:59:59Z");
        var data = new ImportedCollection { TimeZoneId = "UTC", Reviews = [
            new(Stamp("2026-06-29T12:00Z"), 1, 3, 1000, 1),
            new(Stamp("2026-06-30T03:59:59Z"), 1, 1, 1000, 0),
            new(Stamp("2026-06-30T12:00Z"), 1, 0, 1000, 4),
            new(Stamp("2026-06-30T13:00Z"), 1, 3, 1000, 99),
            new(Stamp("2026-06-30T14:00Z"), 1, 0, 1000, 1),
            new(Stamp("2026-07-01T04:00Z"), 1, 3, 1000, 2),
            new(Stamp("2026-07-02T03:59Z"), 1, 4, 1000, 3),
            new(Stamp("2026-07-03T12:00Z"), 1, 3, 1000, 1)] };
        var streaks = Streaks.Analyze(data, now);
        check(streaks.Count == 2 && streaks[0].Start == new DateOnly(2026, 7, 1), "missed June 30 starts new July 1 streak; newest first");
        check(streaks[0].Answers == 2 && streaks[1].Answers == 2, "4 AM boundary and all rated kinds; invalid, administrative, unknown and future answers excluded");
        check(streaks[0].IsCurrent, "before 4 AM remains previous study day");
        check(Streaks.Analyze(data, DateTimeOffset.Parse("2026-07-02T04:00Z"))[0].IsCurrent, "unreviewed current study day does not end streak");
        check(!Streaks.Analyze(data, DateTimeOffset.Parse("2026-07-03T04:00Z"))[0].IsCurrent, "complete missed day ends streak at 4 AM");
        check(Streaks.Analyze(new ImportedCollection { TimeZoneId = "UTC" }, now).Count == 0, "empty streak history");
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        foreach (var instant in new[] { "2026-03-08T06:59Z", "2026-03-08T07:01Z", "2026-03-08T07:59Z" })
            check(Streaks.StudyDay(DateTimeOffset.Parse(instant), eastern) == new DateOnly(2026, 3, 7), "spring DST keeps pre-4 AM answers on previous day");
        check(Streaks.StudyDay(DateTimeOffset.Parse("2026-03-08T08:00Z"), eastern) == new DateOnly(2026, 3, 8), "spring 4 AM cutoff");
        foreach (var instant in new[] { "2026-11-01T05:30Z", "2026-11-01T06:30Z", "2026-11-01T08:59Z" })
            check(Streaks.StudyDay(DateTimeOffset.Parse(instant), eastern) == new DateOnly(2026, 10, 31), "fall DST repeated hour stays on previous study day");
        check(Streaks.StudyDay(DateTimeOffset.Parse("2026-11-01T09:00Z"), eastern) == new DateOnly(2026, 11, 1), "fall 4 AM cutoff");
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var end = start.AddYears(70);
        var days = (end - start).Days + 1;
        var longData = new ImportedCollection { TimeZoneId = "UTC", Reviews = Enumerable.Range(0, days)
            .Select(i => new ReviewRecord(start.AddDays(i).ToUnixTimeMilliseconds(), 1, 3, 1000, 1)).ToList() };
        var longStreak = Streaks.Analyze(longData, end).Single();
        check(longStreak.Length == days && longStreak.IsCurrent && longStreak.End == DateOnly.FromDateTime(end.DateTime), "70 years including leap days and year boundaries remain one streak");
    }
}
