using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnkiStats.Core;

public sealed class MappingAlias
{
    public string Expression { get; set; } = "";
    public string Lookup { get; set; } = "";
    public string Reading { get; set; } = "";
    public string NoteType { get; set; } = "";
}
public sealed record MappingDeferred(string Key, string Fingerprint);
public sealed record MediaMappingDecision(string CorpusId, string WordKey, string Fingerprint, List<string> EntryIds, bool Exclude = false);
public sealed record MappingReviewItem(string Key, string Kind, string Fingerprint, long? NoteId, string? CorpusId, string? WordKey,
    string Expression, string Reading, string Meaning, string Location, string Sentence, int Cards, List<string> CandidateIds, string Reason, bool Resolved = false);

public static class MappingSuggestions
{
    public static List<DictionaryEntry> Find(VocabularyMatcher matcher, string expression, string reading, string meaning,
        IEnumerable<MappingAlias>? aliases = null, string noteType = "")
    {
        var source = TextNormalizer.Clean(expression).Normalize(NormalizationForm.FormKC).Trim();
        var rules = (aliases ?? []).Where(r => r.Expression.Normalize(NormalizationForm.FormKC).Trim().Equals(source, StringComparison.OrdinalIgnoreCase)
            && (r.NoteType.Length == 0 || r.NoteType == noteType)).ToList();
        var result = rules.SelectMany(r => matcher.Find(r.Lookup.Trim(), r.Reading.Trim())).ToList();
        var parsed = VocabularyExpression.Parse(new(0, source, reading, meaning, "Vocabulary", noteType));
        result.AddRange(matcher.Find(parsed.Word, parsed.Readings.Count == 1 ? parsed.Readings[0] : ""));
        foreach (var form in Regex.Matches(source, @"[一-龯々ぁ-ゖァ-ヺー]+", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Value).Distinct().Take(12))
        {
            result.AddRange(matcher.Find(form, ""));
            if (form.EndsWith('だ') && form.Length > 1) result.AddRange(matcher.Find(form[..^1], ""));
            // These are proposals for explicit review, never automatic component credit.
            if (form is "のは" or "のが" or "には" or "では")
                foreach (var particle in form) result.AddRange(matcher.Find(particle.ToString(), ""));
        }
        return result.DistinctBy(e => e.Id).OrderByDescending(e => DefinitionMatcher.Score(meaning, e.Meaning))
            .ThenBy(e => e.Id, StringComparer.Ordinal).Take(40).ToList();
    }
}

public static class MappingReview
{
    public const string JapaneseDeck = "日本語";
    public static ImportedCollection JapaneseSource(ImportedCollection source) => DeckProfiles.Select(source,
        new DeckProfile { Name = JapaneseDeck, DeckRules = [new(JapaneseDeck, true)] });
    public static Settings JapaneseSettings(Settings settings)
    {
        var scoped = DeckProfiles.Copy(settings);
        scoped.Profiles = []; scoped.ActiveProfileId = ""; scoped.DeckRules = [new(JapaneseDeck, true)];
        return scoped;
    }
    public const string ExcludedSource = "Excluded by mapping";
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static string WordKey(MediaWord word) => Hash(new { word.Form, word.Reading, word.SourceEntryId });
    public static string SourceFingerprint(MediaCorpus corpus, MediaWord word) => Hash(new { corpus.ContentHash, corpus.Analyzer, corpus.DictionaryVersion, word.Form, word.Reading, word.Status, word.SourceEntryId });
    public static bool IsUnresolved(MediaWord word, IReadOnlyDictionary<string, DictionaryEntry> entries) =>
        word.Status != ExcludedSource && (word.EntryId is null || !entries.ContainsKey(word.EntryId));
    public static MediaCorpus Apply(MediaCorpus corpus, IEnumerable<MediaMappingDecision> decisions, IReadOnlyDictionary<string, DictionaryEntry> entries)
    {
        var saved = decisions.Where(d => d.CorpusId == corpus.Id).GroupBy(d => d.WordKey).ToDictionary(g => g.Key, g => g.Last());
        var words = new List<MediaWord>();
        foreach (var word in corpus.Words)
        {
            if (!saved.TryGetValue(WordKey(word), out var decision) || decision.Fingerprint != SourceFingerprint(corpus, word)
                || !decision.Exclude && (decision.EntryIds.Count == 0 || decision.EntryIds.Any(id => !entries.ContainsKey(id)))) { words.Add(word); continue; }
            if (decision.Exclude) words.Add(word with { EntryId = null, Status = ExcludedSource });
            else foreach (var id in decision.EntryIds.Distinct()) words.Add(word with { EntryId = id, Status = "Manually mapped" });
        }
        return corpus with { Words = words };
    }
    public static List<MappingReviewItem> Items(ImportedCollection source, CoverageAnalysis analysis, IEnumerable<MediaCorpus> library,
        IEnumerable<MediaMappingDecision> mediaDecisions, VocabularyMatcher matcher, IEnumerable<MappingAlias> aliases)
    {
        var notes = source.Notes.ToDictionary(n => n.Id);
        var cardsByNote = source.Cards.ToLookup(c => c.NoteId);
        var items = new List<MappingReviewItem>();
        foreach (var match in analysis.Matches.Where(m => m.NeedsReview || m.Status is "Corrected" or "Excluded"))
        {
            if (!notes.TryGetValue(match.NoteId, out var note)) continue;
            var cards = cardsByNote[note.Id].ToList();
            var candidates = match.Candidates.Concat(MappingSuggestions.Find(matcher, note.SourceExpression.Length > 0 ? note.SourceExpression : note.Expression,
                note.Reading, note.Meaning, aliases, note.NoteType)).DistinctBy(e => e.Id)
                .OrderByDescending(e => DefinitionMatcher.Score(note.Meaning, e.Meaning)).ThenBy(e => e.Id, StringComparer.Ordinal).Select(e => e.Id).ToList();
            items.Add(new("note:" + note.Id, "Anki", VocabularyMatcher.Fingerprint(note), note.Id, null, null,
                note.SourceExpression.Length > 0 ? note.SourceExpression : note.Expression, note.Reading, note.Meaning,
                $"{note.Category} · {note.NoteType}\n{string.Join(", ", cards.Select(c => c.Deck).Distinct())}\nNote {note.Id} · Cards: {string.Join(", ", cards.Select(c => c.Id))}", "", cards.Count,
                !match.NeedsReview ? match.EntryIds : candidates, match.Reason, !match.NeedsReview));
        }
        foreach (var corpus in library)
        {
            var effective = Apply(corpus, mediaDecisions, matcher.Entries);
            var effectiveByForm = effective.Words.ToLookup(WordKey);
            foreach (var group in corpus.Words.Where(w => IsUnresolved(w, matcher.Entries)).GroupBy(WordKey))
            {
                var word = group.First();
                var lookup = Regex.Replace(word.Form, @"\[[^\[\]]*\]", "");
                var candidates = MappingSuggestions.Find(matcher, lookup, word.Reading, "", aliases).Select(e => e.Id).ToList();
                if (word.SourceEntryId is {} id && matcher.Entries.ContainsKey(id)) candidates = new[] { id }.Concat(candidates).Distinct().ToList();
                var mapped = effectiveByForm[group.Key].ToList();
                var resolved = mapped.Count > 0 && mapped.All(w => !IsUnresolved(w, matcher.Entries));
                if (resolved) candidates = mapped.Where(w => w.EntryId is not null).Select(w => w.EntryId!).Distinct().ToList();
                items.Add(new("media:" + corpus.Id + ":" + group.Key, "Media", SourceFingerprint(corpus, word), null, corpus.Id, group.Key,
                    word.Form, word.Reading, "", corpus.Title + "\n" + corpus.Edition + "\n"
                        + (corpus.Source == "Jiten vocabulary" ? "Downloaded Jiten vocabulary list (no script downloaded)" : corpus.Source), word.Sentence, 0, candidates,
                    resolved ? (mapped.Any(w => w.Status == ExcludedSource) ? "Ignored source form" : "Confirmed source mapping") : word.Status, resolved));
            }
        }
        return items;
    }
}
