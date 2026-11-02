namespace AnkiStats.Core;

public sealed record ReviewStreak(List<DailyRow> Days, bool IsCurrent)
{
    public DateOnly Start => Days[0].Day;
    public DateOnly End => Days[^1].Day;
    public int Length => Days.Count;
    public int Answers => Days.Sum(d => d.Answers);
    public string Label => $"{Start:MMM d, yyyy} – {End:MMM d, yyyy} · {Length:N0} days" + (IsCurrent ? " · Current" : "");
    public override string ToString() => Label;
}

public static class Streaks
{
    // Convert to local wall time before subtracting the cutoff, including across DST.
    public static DateOnly StudyDay(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime.AddHours(-4));

    public static List<ReviewStreak> Analyze(ImportedCollection data, DateTimeOffset? clock = null)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(data.TimeZoneId);
        var now = clock ?? DateTimeOffset.UtcNow;
        var today = StudyDay(now, zone);
        var days = data.Reviews.Where(r => r.Kind is >= 0 and <= 3 && r.Rating is >= 1 and <= 4 && r.Id <= now.ToUnixTimeMilliseconds())
            .GroupBy(r => StudyDay(DateTimeOffset.FromUnixTimeMilliseconds(r.Id), zone))
            .Select(g => new DailyRow(g.Key, g.Count())).OrderBy(d => d.Day);
        var streaks = new List<ReviewStreak>();
        List<DailyRow> current = [];
        foreach (var day in days)
        {
            if (current.Count > 0 && day.Day.DayNumber != current[^1].Day.DayNumber + 1)
            {
                streaks.Add(new(current, false));
                current = [];
            }
            current.Add(day);
        }
        if (current.Count > 0)
            streaks.Add(new(current, today.DayNumber - current[^1].Day.DayNumber is 0 or 1));
        streaks.Reverse();
        return streaks;
    }
}
