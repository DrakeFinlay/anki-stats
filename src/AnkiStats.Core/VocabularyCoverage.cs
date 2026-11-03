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
public sealed record MatchDecision(long NoteId, string Fingerprint, string? EntryId, bool Exclude = false, List<string>? AdditionalEntryIds = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> EntryIds => EntryId is null ? [] : new[] { EntryId }.Concat(AdditionalEntryIds ?? []).Distinct().ToList();
}
public sealed record FamilyDecision(string EntryId, string Family);
public sealed record CoverageRules(string KnownDeck = "日本語::永遠", string FamiliarDeck = "日本語::勉強してる",
    string SuspendedDeck = "日本語::Suspended");
public sealed record VocabularyMatch(long NoteId, string Expression, string Reading, string Meaning, string Decks,
    bool Known, bool Familiar, string Status, string Reason, List<string> EntryIds, List<DictionaryEntry> Candidates, string Fingerprint,
    List<string> ReviewEntryIds, bool HasUnresolved, bool HasWarning, bool HasDefinitionWarning = false)
{
    public string? EntryId => EntryIds.Count == 1 ? EntryIds[0] : null;
    public string EntryIdsDisplay => string.Join(", ", EntryIds);
    public bool NeedsReview => HasUnresolved || HasWarning || HasDefinitionWarning;
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
    public int Unresolved => Matches.Count(m => m.HasUnresolved);
    public int ReadingWarnings => Matches.Count(m => m.HasWarning);
    public int DefinitionWarnings => Matches.Count(m => m.HasDefinitionWarning);
}

/// <summary>Local dictionary lookup. No sentence tokenization, stemming, or network lookup.</summary>
public sealed class VocabularyMatcher
{
    public const string DefinitionRules = "Definition evidence: one supported form and one supported meaning are sufficient; spellings/readings are alternatives, not a checklist. English gloss phrases distinguish otherwise compatible homophones. Exact normalized phrases outrank short phrase containment; ties and broad generic words do not resolve ambiguity. A unique Japanese match with unverified English wording remains credited with a definition warning rather than treating a paraphrase as proven wrong. Explicit manual corrections override automatic evidence.";
    public const string MatchingRules = "Matching v3: exact script before kana-folded fallback; explicit reading alternatives resolved independently; separated phonetic segments join only when no alternative matches and a unique exact-form dictionary reading supports the whole; unique exact-kanji matches with a one-edit reading discrepancy (readings at least four characters; substitution, insertion, deletion, or adjacent transposition) count with a warning; unresolved alternatives receive no credit.";
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
        var parsed = VocabularyExpression.Parse(note);
        return (parsed.Word, string.Join(" / ", parsed.Readings));
    }
    // Accept existing saved decisions using the former normalization, without rewriting user data.
    private static string LegacyFingerprint(NoteRecord note)
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
        return Key(word) + "\u001f" + Key(reading);
    }
    private static string WrittenKey(string value) => value.Normalize(NormalizationForm.FormKC).Trim();
    private static bool ExactForm(DictionaryEntry entry, string word) => entry.Spellings.Concat(entry.Readings.Select(r => r.Text))
        .Any(form => WrittenKey(form) == WrittenKey(word));
    private static bool Applies(DictionaryEntry entry, DictionaryReading reading, string word) => entry.Spellings.Count == 0
        || Key(word) == Key(reading.Text) || reading.AppliesTo.Contains("*") || reading.AppliesTo.Any(s => Key(s) == Key(word));
    public List<DictionaryEntry> Find(string word, string reading)
    {
        if (!index.TryGetValue(Key(word), out var candidates)) return [];
        var compatible = candidates.Where(e => reading.Length == 0 || e.Readings.Any(r => Key(r.Text) == Key(reading) && Applies(e, r, word))).ToList();
        var exact = compatible.Where(e => ExactForm(e, word)).ToList();
        return (exact.Count > 0 ? exact : compatible).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
    }
    private sealed record Resolution(List<DictionaryEntry> Candidates, bool Confirmed, bool Warning, string Reason, bool ReadingMatched = true, bool DefinitionWarning = false);
    private Resolution WithDefinition(Resolution resolution, string meaning)
    {
        if (string.IsNullOrWhiteSpace(meaning) || !resolution.ReadingMatched || resolution.Candidates.Count == 0) return resolution;
        var scored = resolution.Candidates.Select(e => (Entry: e, Score: DefinitionMatcher.Score(meaning, e.Meaning))).ToList();
        var best = scored.Max(s => s.Score);
        var supported = scored.Where(s => s.Score == best && best > 0).ToList();
        if (supported.Count == 1)
            return resolution with { Candidates = [supported[0].Entry], Confirmed = true, Reason = resolution.Candidates.Count > 1
                ? "Expression/reading and English definition identify one entry" : resolution.Reason + "; English definition supports this entry" };
        if (resolution.Confirmed && best == 0)
            return resolution with { DefinitionWarning = true, Reason = resolution.Reason + "; definition wording not verified—review the gloss" };
        return resolution with { Reason = resolution.Reason + (best == 0 ? "; definition does not distinguish the candidates" : "; definition supports more than one candidate") };
    }
    private Resolution Resolve(string word, string reading, string meaning)
    {
        var strict = Find(word, reading);
        if (strict.Count > 0) return WithDefinition(new(strict, strict.Count == 1, false, strict.Count == 1 ? "Exact expression/reading match" : "Multiple dictionary entries; choose one"), meaning);
        var written = index.GetValueOrDefault(Key(word), []).Where(e => ExactForm(e, word)).ToList();
        // A bounded reading discrepancy is evidence only with an exact kanji spelling. Never fuzzy-match headwords or kana homophones.
        if (word.Any(c => c is >= '一' and <= '龯') && reading.Length >= 4)
        {
            var near = written.Where(e => e.Readings.Any(r => Applies(e, r, word) && OneEditApart(Key(r.Text), Key(reading)))).ToList();
            if (near.Count == 1)
                return WithDefinition(new(near, true, true, $"Exact spelling; small reading discrepancy (one edit). Dictionary: {string.Join(" / ", near[0].Readings.Select(r => r.Text))}"), meaning);
        }
        return new(written, false, false, written.Count > 0 ? "Expression exists, but its reading does not match; review the reading" : "No expression/reading match", false);
    }
    private static bool OneEditApart(string expected, string actual)
    {
        if (expected.Length < 4 || Math.Abs(expected.Length - actual.Length) > 1 || expected == actual) return false;
        if (expected.Length == actual.Length)
        {
            var differences = Enumerable.Range(0, expected.Length).Where(p => expected[p] != actual[p]).Take(3).ToList();
            if (differences.Count == 2 && differences[1] == differences[0] + 1
                && expected[differences[0]] == actual[differences[1]] && expected[differences[1]] == actual[differences[0]]) return true;
        }
        var i = 0; var j = 0; var edits = 0;
        while (i < expected.Length && j < actual.Length)
        {
            if (expected[i] == actual[j]) { i++; j++; continue; }
            if (++edits > 1) return false;
            if (expected.Length >= actual.Length) i++;
            if (actual.Length >= expected.Length) j++;
        }
        return edits + expected.Length - i + actual.Length - j == 1;
    }

    public CoverageAnalysis Analyze(ImportedCollection data, IEnumerable<MatchDecision>? decisions = null,
        IEnumerable<FamilyDecision>? families = null, CoverageRules? rules = null)
    {
        rules ??= new();
        var saved = (decisions ?? []).ToDictionary(d => d.NoteId);
        var cards = data.Cards.ToLookup(c => c.NoteId);
        var matches = new List<VocabularyMatch>();
        foreach (var note in data.Notes.Where(n => n.Category == "Vocabulary" && cards.Contains(n.Id)))
        {
            var parsed = VocabularyExpression.Parse(note);
            var word = parsed.Word; var reading = string.Join(" / ", parsed.Readings);
            var oldFingerprint = "v2\u001f" + word + "\u001f" + reading;
            var fingerprint = "v3\u001f" + word + "\u001f" + reading + "\u001f" + DefinitionMatcher.Fingerprint(note.Meaning);
            var resolutions = parsed.Error is not null ? [] : parsed.Readings.DefaultIfEmpty("").Select(r => Resolve(word, r, note.Meaning)).ToList();
            if (parsed.Error is null && parsed.JoinedReading is not null && resolutions.All(r => !r.ReadingMatched))
            {
                var joined = Find(word, parsed.JoinedReading);
                if (joined.Count == 1 && ExactForm(joined[0], word))
                    resolutions = [WithDefinition(new(joined, true, false, "Separated phonetic segments match one complete dictionary reading"), note.Meaning)];
            }
            var candidates = resolutions.SelectMany(r => r.Candidates).DistinctBy(e => e.Id).ToList();
            var confirmed = new HashSet<string>(); var pending = new HashSet<string>();
            var unresolved = parsed.Error is not null; var warning = resolutions.Any(r => r.Warning);
            var definitionWarning = resolutions.Any(r => r.DefinitionWarning);
            foreach (var resolution in resolutions)
            {
                if (resolution.Confirmed) confirmed.Add(resolution.Candidates[0].Id);
                else { unresolved = true; pending.UnionWith(resolution.Candidates.Select(c => c.Id)); }
            }
            var eligible = cards[note.Id].Where(c => !LearningAnalyzer.Under(c.Deck, rules.SuspendedDeck)).ToList();
            var known = eligible.Any(c => LearningAnalyzer.Under(c.Deck, rules.KnownDeck));
            var familiar = eligible.Any(c => LearningAnalyzer.Under(c.Deck, rules.FamiliarDeck));
            var status = confirmed.Count > 0 ? unresolved ? "Partial" : warning ? "Reading warning" : definitionWarning ? "Definition warning" : "Matched"
                : candidates.Count > 1 ? "Ambiguous" : "Unmatched";
            var reason = parsed.Error ?? string.Join("; ", resolutions.Select(r => r.Reason).Distinct());
            if (confirmed.Count > 1) reason = "Explicit reading alternatives support separate entries. " + reason;
            if (saved.TryGetValue(note.Id, out var decision))
            {
                if ((decision.Fingerprint != fingerprint && decision.Fingerprint != oldFingerprint && decision.Fingerprint != LegacyFingerprint(note))
                    || (!decision.Exclude && (decision.EntryIds.Count == 0 || decision.EntryIds.Any(id => !Entries.ContainsKey(id)))))
                { status = "Stale decision"; reason = "The note or reference entry changed; review the saved decision"; confirmed.Clear(); pending.UnionWith(candidates.Select(c => c.Id)); unresolved = true; warning = definitionWarning = false; }
                else if (decision.Exclude) { status = "Excluded"; reason = "Manually excluded from dictionary coverage"; confirmed.Clear(); pending.Clear(); unresolved = warning = definitionWarning = false; }
                else { status = "Corrected"; reason = "Saved manual dictionary entries"; confirmed = decision.EntryIds.ToHashSet(); pending.Clear(); unresolved = warning = definitionWarning = false; }
            }
            matches.Add(new(note.Id, word, reading, note.Meaning, string.Join(", ", cards[note.Id].Select(c => c.Deck).Distinct()),
                known, familiar, status, reason, confirmed.Order(StringComparer.Ordinal).ToList(), candidates, fingerprint, pending.Except(confirmed).ToList(), unresolved, warning, definitionWarning));
        }
        HashSet<string> Ids(Func<VocabularyMatch, bool> predicate) => matches.Where(predicate).SelectMany(m => m.EntryIds).ToHashSet();
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
        var text = new StringBuilder($"# {list}: {(absentDatabase ? "no confirmed match in imported database" : "not covered by known/familiar decks")}\n\n");
        text.AppendLine($"Generated: {generatedAt ?? DateTimeOffset.Now:O}  \nCollection refreshed: {result.RefreshedAt:O}  \nScope: {Cell(result.Scope)}");
        text.AppendLine($"\nKnown = 永遠; familiar = 勉強してる; combined = their union. Suspended deck contributes to database only.\n\n{missing.Count:N0} entries without a confirmed match in this scope. {result.Unresolved:N0} notes have unresolved readings/matches; {result.ReadingWarnings:N0} confirmed notes have reading warnings; {result.DefinitionWarnings:N0} confirmed notes have unverified definition wording. Confirmed portions of multi-reading notes count; unresolved alternatives do not. Absence of a confirmed match is not proof of absence or lack of knowledge.\n");
        var meta = result.Reference.Metadata;
        text.AppendLine($"Reference: JMdict {meta.DictionaryVersion} ({meta.DictionaryDate}); OpenJLPT {meta.JlptVersion}. {meta.UnlinkedJlptItems} unlinked JLPT rows excluded from entry denominators.\n\n{meta.Rules}\n\n{meta.Attribution}\n");
        text.AppendLine(VocabularyMatcher.MatchingRules + "\n\n" + VocabularyMatcher.DefinitionRules + "\n");
        var pending = result.Matches.SelectMany(m => m.ReviewEntryIds).ToHashSet();
        void Table(string heading, IEnumerable<DictionaryEntry> entries)
        {
            text.AppendLine($"## {heading}\n");
            text.AppendLine("One accepted spelling or reading is enough; other forms are optional. One matching sense is enough; the whole gloss list is not required.\n");
            text.AppendLine("| One accepted form | Other accepted forms (optional) | Meaning | In imported database | JMdict ID |\n| --- | --- | --- | --- | --- |");
            foreach (var entry in entries)
                text.AppendLine($"| {Cell(entry.Readings[0].Text)} | {Cell(string.Join(" / ", entry.Spellings.Concat(entry.Readings.Skip(1).Select(r => r.Text)).Where(f => f != entry.Readings[0].Text).Distinct()))} | {Cell(entry.Meaning)} | {(result.Database.Contains(entry.Id) ? "Yes" : pending.Contains(entry.Id) ? "Possible match; needs review" : "No confirmed match")} | {entry.Id} |");
            text.AppendLine();
        }
        var reviewCount = missing.Count(e => pending.Contains(e.Id));
        Table($"{(absentDatabase ? "No supporting vocabulary found" : "No confirmed known/familiar coverage")} ({missing.Count - reviewCount:N0})", missing.Where(e => !pending.Contains(e.Id)));
        Table($"Possible matches — resolve before treating as absent ({reviewCount:N0})", missing.Where(e => pending.Contains(e.Id)));
        return text.ToString();
    }
}
