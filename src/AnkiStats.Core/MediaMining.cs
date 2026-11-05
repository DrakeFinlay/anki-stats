namespace AnkiStats.Core;

public sealed record MediaMiningWord(string EntryId, string Word, string Reading, string Meaning, string Lists,
    int TitleCount, int Occurrences, string Titles, bool Eligible, string Sentence = "", string SentenceSource = "");

public static class MediaMining
{
    public static bool CanImport(MediaWordRow word) => word.EntryId.Length > 0 && word.Status is "Remaining" or "Not in Anki";

    public static List<MediaMiningWord> Selected(MediaCorpus corpus, CoverageAnalysis coverage, IEnumerable<string> selectedIds)
    {
        var selected = selectedIds.ToHashSet(StringComparer.Ordinal);
        var examples = corpus.Words.Where(w => w.EntryId is not null && !string.IsNullOrWhiteSpace(w.Sentence))
            .GroupBy(w => w.EntryId!).ToDictionary(g => g.Key, g => g.First().Sentence);
        // Any shelf and either list membership are allowed; only selected, resolved,
        // confidently missing identities may reach the shared Anki preview.
        return MediaAnalysis.Compare(corpus, coverage).Words.Where(w => selected.Contains(w.EntryId) && CanImport(w))
            .Select(w => new MediaMiningWord(w.EntryId, w.Form, w.Reading, w.Meaning, w.Lists, 1, w.Occurrences, corpus.Title, w.Eligible,
                examples.GetValueOrDefault(w.EntryId, ""), examples.ContainsKey(w.EntryId) ? corpus.Title : "")).ToList();
    }

    public static List<MediaMiningWord> Missing(IEnumerable<MediaCorpus> library, CoverageAnalysis coverage, int minimumTitles, bool eligible)
    {
        if (minimumTitles < 1) throw new ArgumentOutOfRangeException(nameof(minimumTitles));
        var entries = coverage.Reference.Entries.ToDictionary(e => e.Id);
        var excluded = coverage.Database.Concat(coverage.Matches.SelectMany(m => m.ReviewEntryIds)).ToHashSet();
        var corpora = library.ToList();
        var examples = corpora.OrderBy(c => c.Id, StringComparer.Ordinal)
            .SelectMany(c => c.Words.Where(w => w.EntryId is not null && w.Sentence.Length > 0)
                .Select(w => new { Id = w.EntryId!, w.Sentence, c.Title })).GroupBy(w => w.Id)
            .ToDictionary(g => g.Key, g => g.First());
        // Count a title once even when multiple forms/occurrences map to the same entry.
        var words = corpora.Where(c => c.State == MediaShelves.Completed).DistinctBy(c => c.Id)
            .SelectMany(c => c.Words.Where(w => w.EntryId is not null && entries.ContainsKey(w.EntryId) && !excluded.Contains(w.EntryId))
                .GroupBy(w => w.EntryId!).Select(g => new { Corpus = c, Entry = entries[g.Key], Uses = g.Sum(w => w.Occurrences),
                    Sentence = g.Select(w => w.Sentence).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "" }))
            .GroupBy(w => w.Entry.Id).Select(g =>
            {
                var entry = g.First().Entry;
                var form = entry.StudyForm;
                var example = g.OrderBy(w => w.Corpus.Id, StringComparer.Ordinal).FirstOrDefault(w => w.Sentence.Length > 0);
                return new MediaMiningWord(entry.Id, form?.Written ?? entry.Spellings.FirstOrDefault() ?? entry.Readings[0].Text,
                    form?.Reading ?? entry.Readings[0].Text, entry.Meaning,
                    string.Join(" / ", (entry.Common ? new[] { "Common" } : []).Concat(entry.Levels)),
                    g.Count(), g.Sum(w => w.Uses), string.Join(" · ", g.Select(w => w.Corpus.Title).Order(StringComparer.Ordinal)),
                    entry.Common || entry.Levels.Count > 0, example?.Sentence ?? "", example?.Corpus.Title ?? "");
            });
        return words.Where(w => w.Eligible == eligible && w.TitleCount >= minimumTitles)
            .Select(w => w.Sentence.Length == 0 && examples.TryGetValue(w.EntryId, out var example)
                ? w with { Sentence = example.Sentence, SentenceSource = example.Title } : w)
            .OrderByDescending(w => w.TitleCount).ThenByDescending(w => w.Occurrences).ThenBy(w => w.EntryId, StringComparer.Ordinal).ToList();
    }
}
