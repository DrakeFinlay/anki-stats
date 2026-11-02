using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AnkiStats.Core;

public static partial class TextNormalizer
{
    private static string Replace(string value, string pattern, string replacement) => Regex.Replace(value, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    public static string Clean(string value, bool stripBracketReadings = false)
    {
        value = Replace(value, @"<(script|style)\b[^>]*>.*?</\1>", "");
        value = Replace(value, @"<rt\b[^>]*>.*?</rt>", "");
        value = Replace(value, @"\[sound:[^\]]*\]", "");
        value = Replace(value, @"<(br\b[^>]*|/div|/p)>", " ");
        value = Replace(value, @"<[^>]*>", "");
        value = WebUtility.HtmlDecode(value);
        if (stripBracketReadings) value = Replace(value, @"\[[\p{IsHiragana}\p{IsKatakana}ー\s]+\]", "");
        return value.Normalize(NormalizationForm.FormC).Trim();
    }
}

public static class Analyzer
{
    public static readonly string[] BucketNames = ["Overnight · 00–06", "Morning · 06–12", "Afternoon · 12–18", "Evening · 18–24"];
    public static AnalysisResult Analyze(ImportedCollection data, int? days = null, int? kind = 1, DateTimeOffset? clock = null)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(data.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock ?? DateTimeOffset.UtcNow, zone).DateTime);
        var start = days is {} d ? today.AddDays(1 - d) : DateOnly.MinValue;
        var result = new AnalysisResult();
        var cardsByNote = data.Cards.ToLookup(c => c.NoteId);
        foreach (var n in data.Notes.Where(n => n.Category == "Vocabulary" && string.IsNullOrWhiteSpace(n.Expression)))
            result.Skipped.Add(new(n.Id, n.NoteType, "Expression is empty after extraction"));
        result.Vocabulary = data.Notes.Where(n => n.Category == "Vocabulary" && !string.IsNullOrWhiteSpace(n.Expression))
            .GroupBy(n => (n.Expression, n.Reading))
            .Select(g =>
            {
                var cards = g.SelectMany(n => cardsByNote[n.Id]).ToList();
                return new VocabularyRow(g.Key.Expression, g.Key.Reading, string.Join(" / ", g.Select(n => n.Meaning).Where(m => m.Length > 0).Distinct()), g.Count(), cards.Count,
                    string.Join(", ", cards.Select(c => c.Deck).Distinct().Order()), string.Join(", ", g.Select(n => n.Id)), string.Join(", ", cards.Select(c => c.Id)));
            }).OrderBy(v => v.Expression, StringComparer.Ordinal).ThenBy(v => v.Reading, StringComparer.Ordinal).ToList();
        var dated = new List<(ReviewRecord Review, DateOnly Date, int Bucket)>();
        foreach (var review in data.Reviews)
        {
            if (review.Kind is < 0 or > 3 || review.Rating is < 1 or > 4) continue;
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(review.Id), zone);
            var date = DateOnly.FromDateTime(local.DateTime);
            if (date < start || date > today) continue;
            dated.Add((review, date, local.Hour / 6));
        }
        var selected = dated.Where(r => kind is null || r.Review.Kind == kind).ToList();
        var ordinary = dated.Where(r => r.Review.Kind == 1).ToList();
        result.TotalAnswers = selected.Count;
        result.OrdinaryReviews = ordinary.Count;
        result.Again = ordinary.Count(r => r.Review.Rating == 1);
        result.Daily = selected.GroupBy(r => r.Date).Select(g => new DailyRow(g.Key, g.Count())).OrderBy(d => d.Day).ToList();
        for (var i = 0; i < 4; i++)
        {
            var items = selected.Where(r => r.Bucket == i).Select(r => r.Review).ToList();
            var times = items.Where(r => r.Milliseconds > 0).Select(r => r.Milliseconds / 1000.0).Order().ToArray();
            double? median = times.Length == 0 ? null : times.Length % 2 == 1 ? times[times.Length / 2] : (times[times.Length / 2 - 1] + times[times.Length / 2]) / 2;
            result.Buckets.Add(new(BucketNames[i], items.Count, items.Count(r => r.Rating == 1), items.Count == 0 ? null : 100.0 * items.Count(r => r.Rating == 1) / items.Count, median, times.Length));
        }
        var byCard = ordinary.ToLookup(r => r.Review.CardId);
        var notes = data.Notes.ToDictionary(n => n.Id);
        result.Rankings = data.Cards.Where(c => byCard.Contains(c.Id)).Select(c => new RankingRow(c.Id, notes[c.NoteId].Expression, c.Deck, byCard[c.Id].Count(), byCard[c.Id].Count(r => r.Review.Rating == 1), c.LifetimeLapses)).OrderByDescending(r => r.Reviews).ThenBy(r => r.CardId).ToList();
        result.Insights = BuildInsights(result, selected.Select(r => (r.Date, r.Bucket)).ToList());
        return result;
    }

    private static List<Insight> BuildInsights(AnalysisResult r, List<(DateOnly Date, int Bucket)> events)
    {
        var insights = new List<Insight>();
        if (r.Rankings.FirstOrDefault() is {} top)
            insights.Add(new("most-reviewed", "Your most-reviewed card", $"{top.Expression} has {top.Reviews:N0} ordinary review answers.", "Ranks individual cards by ordinary review answers. Learning and relearning answers are excluded from this ranking; repeated answers to a card count separately."));
        if (r.Rankings.OrderByDescending(c => c.Again).FirstOrDefault() is { Again: > 0 } lapse)
            insights.Add(new("most-again", "The card with the most Again answers", $"{lapse.Expression} received Again {lapse.Again:N0} times during ordinary reviews.", "Counts Again ratings during ordinary reviews for each card. This is not the leech tag or the current lifetime lapse counter."));
        if (r.Daily.Count > 0)
        {
            var busiest = r.Daily.MaxBy(d => d.Answers)!;
            insights.Add(new("busiest-day", "Your busiest review day", $"{busiest.Day:MMM d, yyyy}: {busiest.Answers:N0} recorded answers.", "The midnight-to-midnight local calendar date with the most answers. Each rated answer counts, including repeat answers to the same card."));
            var longest = 0; var streak = 0; DateOnly? previous = null;
            foreach (var d in r.Daily) { streak = previous?.AddDays(1) == d.Day ? streak + 1 : 1; longest = Math.Max(longest, streak); previous = d.Day; }
            insights.Add(new("longest-streak", "Your longest review streak", $"{longest:N0} consecutive calendar days with at least one recorded answer.", "A streak breaks on a calendar date with no answers. Dates run from local midnight to midnight; this can differ from Anki's configured study-day rollover."));
            var weekday = events.GroupBy(e => e.Date.DayOfWeek).OrderByDescending(g => g.Count()).First();
            insights.Add(new("weekday", "Your busiest day of the week", $"{weekday.Key} accounts for {weekday.Count():N0} answers, the most of any weekday.", "Sums answers across all occurrences of each weekday in the date range. This is total volume, not an accuracy ranking."));
            var bucket = r.Buckets.MaxBy(b => b.Answers)!;
            insights.Add(new("usual-period", "When you answer the most cards", $"{bucket.Period}: {bucket.Answers:N0} of {r.TotalAnswers:N0} answers ({100.0*bucket.Answers/r.TotalAnswers:F1}%).", "Assigns each answer timestamp to a six-hour local-time bucket. This describes when you study, not when you perform best."));
            insights.Add(new("evening-share", "Your evening review share", $"{r.Buckets[3].Answers:N0} of {r.TotalAnswers:N0} answers ({100.0 * r.Buckets[3].Answers / r.TotalAnswers:F1}%) occurred between 18:00 and midnight.", "Counts all answers timestamped from 18:00 inclusive to 00:00 exclusive in the displayed time zone."));
            var best = r.Buckets.Where(b => b.Answers >= 30).MinBy(b => b.AgainPercent);
            if (best is not null) insights.Add(new("lowest-again", "The time bucket with the lowest Again rate", $"{best.Period}: {best.Again:N0} Again answers out of {best.Answers:N0} ({best.AgainDisplay}).", "Compares only buckets with at least 30 answers. Again rate is Again ratings divided by answers, not a fluency score. Different workloads and card difficulty can explain the difference."));
            var span = r.Daily[^1].Day.DayNumber-r.Daily[0].Day.DayNumber+1;
            insights.Add(new("active-days", "How consistently you reviewed", $"You answered cards on {r.Daily.Count:N0} of {span:N0} calendar days ({100.0*r.Daily.Count/span:F1}%) between {r.Daily[0].Day:MMM d, yyyy} and {r.Daily[^1].Day:MMM d, yyyy}.", "An active date has at least one rated answer. The denominator includes inactive dates between the first and last recorded answer. Days run midnight to midnight, so this can differ from Anki's study-day count."));
            insights.Add(new("typical-day", "Answers per active day", $"{r.TotalAnswers:N0} answers ÷ {r.Daily.Count:N0} active dates = {r.TotalAnswers / (double)r.Daily.Count:F0} answers per active day.", "An active date has at least one answer. This average excludes dates with no answers and counts repeated answers to the same card."));
        }
        if (r.Vocabulary.Any(v => v.Notes > 1))
        {
            var duplicate = r.Vocabulary.MaxBy(v => v.Notes)!;
            insights.Add(new("duplicate-entry", "One entry, several notes", $"{duplicate.Expression} appears in {duplicate.Notes:N0} notes and counts once in the vocabulary inventory for this reading.", "Groups vocabulary notes with identical normalized expressions and readings. Related dictionary entries and stem families remain separate in Phase 1A."));
        }
        return insights;
    }
}

public static class CsvExport
{
    public static string Cell(object? value)
    {
        var s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        var trimmed = s.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || s.StartsWith('\t') || s.StartsWith('\r')) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
    public static void Write(string path, IEnumerable<IEnumerable<object?>> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        foreach (var row in rows) writer.WriteLine(string.Join(",", row.Select(Cell)));
    }
}
