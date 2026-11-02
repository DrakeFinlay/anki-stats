using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AnkiStats.Core;

public sealed record DictionaryReading(string Text, List<string> AppliesTo);
public sealed record DictionaryEntry(string Id, List<string> Spellings, List<DictionaryReading> Readings,
    string Meaning, bool Common, List<string> Levels);
public sealed record ReferenceMetadata(string DictionaryVersion, string DictionaryDate, string JlptVersion,
    int UnlinkedJlptItems, string Attribution, string Rules = "JMdict entry coverage; common = news1/ichi1/spec1/spec2/gai1; OpenJLPT corrected memberships; family rules v1");
public sealed record CoverageReference(ReferenceMetadata Metadata, List<DictionaryEntry> Entries);
public sealed record MatchDecision(long NoteId, string Fingerprint, string? EntryId, bool Exclude = false);
public sealed record FamilyDecision(string EntryId, string Family);
public sealed record CoverageRules(string KnownDeck = "日本語::永遠", string FamiliarDeck = "日本語::勉強してる",
    string SuspendedDeck = "日本語::Suspended");
public sealed record VocabularyMatch(long NoteId, string Expression, string Reading, string Meaning, string Decks,
    bool Known, bool Familiar, string Status, string Reason, string? EntryId, List<DictionaryEntry> Candidates, string Fingerprint)
{
    public string Cohort => Known && Familiar ? "Known + familiar" : Known ? "Known" : Familiar ? "Familiar" : "Database only";
}
public sealed record CoverageCount(string List, int Total, int Known, int Familiar, int Combined, int Database)
{
    public string KnownDisplay => Display(Known);
    public string FamiliarDisplay => Display(Familiar);
    public string CombinedDisplay => Display(Combined);
    public string DatabaseDisplay => Display(Database);
    public int Missing => Total - Combined;
    private string Display(int count) => Total == 0 ? "No data" : $"{count:N0} / {Total:N0} · {100.0 * count / Total:F1}%";
}
public sealed record StemFamily(string Key, string Rule, List<DictionaryEntry> Entries)
{
    public string Name => Key.StartsWith("entry:", StringComparison.Ordinal) ? Entries[0].Spellings.FirstOrDefault() ?? Entries[0].Readings[0].Text : Key[(Key.IndexOf(':') + 1)..];
    public string Members => string.Join(" / ", Entries.Select(e => e.Spellings.FirstOrDefault() ?? e.Readings[0].Text));
}
public sealed record CoverageAnalysis(List<VocabularyMatch> Matches, List<CoverageCount> Counts,
    HashSet<string> Known, HashSet<string> Familiar, HashSet<string> Combined, HashSet<string> Database,
    List<StemFamily> Families, CoverageReference Reference, string Scope, DateTimeOffset RefreshedAt)
{
    public int Unresolved => Matches.Count(m => m.Status is "Ambiguous" or "Unmatched" or "Stale decision");
}

/// <summary>Local dictionary lookup. No sentence tokenization, stemming, or network lookup.</summary>
public sealed class VocabularyMatcher
{
    private readonly Dictionary<string, List<DictionaryEntry>> index = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, DictionaryEntry> Entries { get; }
    public CoverageReference Reference { get; }
    public VocabularyMatcher(CoverageReference reference)
    {
        Reference = reference;
        Entries = reference.Entries.ToDictionary(e => e.Id);
        foreach (var entry in reference.Entries)
            foreach (var form in entry.Spellings.Concat(entry.Readings.Select(r => r.Text)).Select(Key).Distinct())
            {
                if (!index.TryGetValue(form, out var entries)) index[form] = entries = [];
                entries.Add(entry);
            }
    }
    // Width and katakana folding are lookup conveniences only; the inventory is unchanged.
    public static string Key(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c)).Trim();
    public static (string Word, string Reading) Extract(NoteRecord note)
    {
        var source = string.IsNullOrWhiteSpace(note.SourceExpression) ? note.Expression : note.SourceExpression;
        var word = TextNormalizer.Clean(source, true).Replace(" ", "").Replace("　", "");
        var reading = TextNormalizer.Clean(note.Reading).Trim();
        if (reading.Length == 0)
        {
            // Both whole-word 食べる[たべる] and segment 食[た]べる conventions.
            reading = Regex.Replace(source, @"([一-龯々ぁ-ゖァ-ヺー]+)\[([ぁ-ゖァ-ヺー\s]+)\]", m => m.Groups[2].Value,
                RegexOptions.None, TimeSpan.FromSeconds(1));
            reading = reading.Replace(" ", "").Replace("　", "");
            if (!Regex.IsMatch(reading, @"^[ぁ-ゖァ-ヺー]+$", RegexOptions.None, TimeSpan.FromSeconds(1))) reading = "";
        }
        return (Key(word), Key(reading));
    }
    public List<DictionaryEntry> Find(string word, string reading) => !index.TryGetValue(Key(word), out var candidates) ? []
        : candidates.Where(e => reading.Length == 0 || e.Readings.Any(r => Key(r.Text) == Key(reading)
            && (e.Spellings.Count == 0 || Key(word) == Key(r.Text) || r.AppliesTo.Contains("*") || r.AppliesTo.Any(s => Key(s) == Key(word)))))
            .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();

    public CoverageAnalysis Analyze(ImportedCollection data, IEnumerable<MatchDecision>? decisions = null,
        IEnumerable<FamilyDecision>? families = null, CoverageRules? rules = null)
    {
        rules ??= new();
        var saved = (decisions ?? []).ToDictionary(d => d.NoteId);
        var cards = data.Cards.ToLookup(c => c.NoteId);
        var matches = new List<VocabularyMatch>();
        foreach (var note in data.Notes.Where(n => n.Category == "Vocabulary" && cards.Contains(n.Id)))
        {
            var (word, reading) = Extract(note);
            var fingerprint = word + "\u001f" + reading;
            var candidates = Find(word, reading);
            var eligible = cards[note.Id].Where(c => !LearningAnalyzer.Under(c.Deck, rules.SuspendedDeck)).ToList();
            var known = eligible.Any(c => LearningAnalyzer.Under(c.Deck, rules.KnownDeck));
            var familiar = eligible.Any(c => LearningAnalyzer.Under(c.Deck, rules.FamiliarDeck));
            var status = candidates.Count == 1 ? "Matched" : candidates.Count == 0 ? "Unmatched" : "Ambiguous";
            var reason = candidates.Count == 1 ? "Unique expression/reading match" : candidates.Count == 0 ? "No expression/reading match" : "Multiple dictionary entries; choose one";
            string? id = candidates.Count == 1 ? candidates[0].Id : null;
            if (saved.TryGetValue(note.Id, out var decision))
            {
                if (decision.Fingerprint != fingerprint || (!decision.Exclude && (decision.EntryId is null || !Entries.ContainsKey(decision.EntryId))))
                { status = "Stale decision"; reason = "The note or reference entry changed; review the saved decision"; id = null; }
                else if (decision.Exclude) { status = "Excluded"; reason = "Manually excluded from dictionary coverage"; id = null; }
                else { status = "Corrected"; reason = "Saved manual dictionary entry"; id = decision.EntryId; }
            }
            matches.Add(new(note.Id, word, reading, note.Meaning, string.Join(", ", cards[note.Id].Select(c => c.Deck).Distinct()),
                known, familiar, status, reason, id, candidates, fingerprint));
        }
        HashSet<string> Ids(Func<VocabularyMatch, bool> predicate) => matches.Where(m => m.EntryId is not null && predicate(m)).Select(m => m.EntryId!).ToHashSet();
        var knownIds = Ids(m => m.Known); var familiarIds = Ids(m => m.Familiar);
        var combined = Ids(m => m.Known || m.Familiar); var database = Ids(_ => true);
        var counts = new List<CoverageCount>();
        void Add(string name, IEnumerable<DictionaryEntry> entries)
        {
            var ids = entries.Select(e => e.Id).ToHashSet();
            counts.Add(new(name, ids.Count, ids.Count(knownIds.Contains), ids.Count(familiarIds.Contains), ids.Count(combined.Contains), ids.Count(database.Contains)));
        }
        Add("Common", Reference.Entries.Where(e => e.Common));
        for (var level = 5; level >= 1; level--)
            Add("N" + level, Reference.Entries.Where(e => e.Levels.Contains("N" + level)));
        for (var level = 4; level >= 1; level--)
            Add("Through N" + level, Reference.Entries.Where(e => e.Levels.Any(l => int.Parse(l[1..], CultureInfo.InvariantCulture) >= level)));
        var customFamilies = (families ?? []).ToDictionary(f => f.EntryId, f => f.Family.Trim());
        // A deliberately small, documented family. Shared kanji, glosses, and mere xrefs are not sufficient evidence.
        string Family(string id) => customFamilies.TryGetValue(id, out var family) && family.Length > 0 ? "manual:" + family
            : id is "1515630" or "2005410" or "2833992" ? "curated:報う・報いる・報われる" : "entry:" + id;
        var grouped = combined.Select(id => Entries[id]).GroupBy(e => Family(e.Id))
            .Select(g => new StemFamily(g.Key, g.Key.StartsWith("manual:", StringComparison.Ordinal) ? "Manual family"
                : g.Key.StartsWith("curated:", StringComparison.Ordinal) ? "Curated family v1" : "Separate entry; relationship unestablished", g.ToList())).ToList();
        return new(matches, counts, knownIds, familiarIds, combined, database, grouped, Reference, data.Scope, data.RefreshedAt);
    }
}

public static class CoverageReport
{
    public static string Markdown(CoverageAnalysis result, string list, bool absentDatabase, DateTimeOffset? generatedAt = null)
    {
        bool InList(DictionaryEntry e) => list == "Common" ? e.Common : list.StartsWith("Through N", StringComparison.Ordinal)
            ? e.Levels.Any(l => int.Parse(l[1..], CultureInfo.InvariantCulture) >= int.Parse(list[9..], CultureInfo.InvariantCulture)) : e.Levels.Contains(list);
        var present = absentDatabase ? result.Database : result.Combined;
        var missing = result.Reference.Entries.Where(InList).Where(e => !present.Contains(e.Id)).OrderBy(e => e.Spellings.FirstOrDefault() ?? e.Readings.First().Text, StringComparer.Ordinal).ToList();
        static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Replace("<", "&lt;").Replace(">", "&gt;");
        var text = new StringBuilder($"# {list}: {(absentDatabase ? "absent from imported database" : "not covered by known/familiar decks")}\n\n");
        text.AppendLine($"Generated: {generatedAt ?? DateTimeOffset.Now:O}  \nCollection refreshed: {result.RefreshedAt:O}  \nScope: {Cell(result.Scope)}");
        text.AppendLine($"\nKnown = 永遠; familiar = 勉強してる; combined = their union. Suspended deck contributes to database only.\n\n{missing.Count:N0} entries without a confirmed match in this scope. {result.Unresolved:N0} unresolved notes can affect this list. Absence of a confirmed match is not proof of absence or lack of knowledge.\n");
        var meta = result.Reference.Metadata;
        text.AppendLine($"Reference: JMdict {meta.DictionaryVersion} ({meta.DictionaryDate}); OpenJLPT {meta.JlptVersion}. {meta.UnlinkedJlptItems} unlinked JLPT rows excluded from entry denominators.\n\n{meta.Rules}\n\n{meta.Attribution}\n");
        text.AppendLine("| Word | Reading | Meaning | In imported database | JMdict ID |\n| --- | --- | --- | --- | --- |");
        foreach (var entry in missing)
            text.AppendLine($"| {Cell(entry.Spellings.FirstOrDefault() ?? entry.Readings.First().Text)} | {Cell(string.Join(" / ", entry.Readings.Select(r => r.Text)))} | {Cell(entry.Meaning)} | {(result.Database.Contains(entry.Id) ? "Yes" : "No confirmed match")} | {entry.Id} |");
        return text.ToString();
    }
}
