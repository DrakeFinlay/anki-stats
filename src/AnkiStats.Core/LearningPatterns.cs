using System.Text.RegularExpressions;

namespace AnkiStats.Core;

public sealed record LearningRules(string VocabularyRoot = "日本語", string KanjiRoot = "漢字",
    string Known = "日本語::永遠", string VocabularyRecovery = "日本語::Suspended",
    string Immersion = "日本語::Seen in Immersion", string KanjiRecovery = "漢字::Relearn", string Unused = "漢字::Unused");

public sealed record CharacterReading(string Kanji, string Reading);
public sealed record LearningCard(long CardId, long NoteId, string Word, string Reading, string Meaning, string Deck,
    string Domain, int Queue, int Type, int Lapses, int StoredLapses, int Reviews, int RecentLapses, int RecentReviews,
    bool Known, bool Recovery, bool Excluded, List<CharacterReading> CharacterReadings, bool ReadingUnresolved)
{
    public bool HistoryFlagged => Lapses >= 3 && !Known && !Excluded;
    public bool Suspended => Queue == -1;
    public bool Candidate => !Known && !Excluded && (HistoryFlagged || Recovery || Suspended);
    public string Reason => Known ? "Known · excluded" : Excluded ? "Outside difficulty scope" : Recovery
        ? (Suspended ? "Recovery · suspended" : Type == 0 ? "Recovery · new" : "Recovery")
        : Suspended ? "Suspended" : HistoryFlagged ? "3+ historical lapses" : "Below threshold";
    public string ReadingStatus => ReadingUnresolved ? "Character reading unresolved" : CharacterReadings.Count > 0 ? "Explicit character reading" : "Kana-only / not applicable";
    public string RecentDisplay => $"{RecentLapses:N0} / {RecentReviews:N0}";
}

public sealed record LearningFinding(string Kind, string Pattern, string Evidence, List<long> CardIds,
    int FlaggedWords, int RecentLapses, string Confidence);
public sealed record LearningAnalysisResult(List<LearningCard> Cards, List<LearningFinding> CrossDeck,
    List<LearningFinding> WordGroups, DateOnly RecentStart, DateOnly Through, DateTimeOffset RefreshedAt);

public static class LearningAnalyzer
{
    private const string Annotation = @"([一-龯々ぁ-ゖァ-ヺー]+)\[([ぁ-ゖァ-ヺー]+)\]";
    private static readonly HashSet<string> BroadGlosses = ["plain", "right", "game", "break", "close", "life", "thing", "things", "person", "people", "one", "way", "something", "someone", "do", "get", "be", "have", "make", "take", "place", "time", "part", "state", "act", "action", "use"];
    public static bool Under(string deck, string root) => deck.Equals(root, StringComparison.OrdinalIgnoreCase) || deck.StartsWith(root + "::", StringComparison.OrdinalIgnoreCase);
    public static string WrittenForm(string value) => Regex.Replace(Regex.Replace(value, @"\[[^\]]*\]", ""), @"[^一-龯々ぁ-ゖァ-ヺー]", "");
    private static IEnumerable<string> Kanji(string value) => value.Where(ch => ch is >= '一' and <= '龯').Select(ch => ch.ToString()).Distinct();

    public static (string FullReading, List<CharacterReading> Characters, bool Unresolved) Readings(string expression, string explicitReading = "")
    {
        var characters = new List<CharacterReading>();
        void AddCharacter(string text, string reading)
        {
            var positions = text.Select((ch, index) => (ch, index)).Where(p => p.ch is >= '一' and <= '龯').ToList();
            if (positions.Count != 1) return;
            var index = positions[0].index;
            var prefix = text[..index]; var suffix = text[(index + 1)..];
            if (reading.StartsWith(prefix, StringComparison.Ordinal) && reading.EndsWith(suffix, StringComparison.Ordinal) && reading.Length > prefix.Length + suffix.Length)
                characters.Add(new(text[index].ToString(), reading.Substring(prefix.Length, reading.Length - prefix.Length - suffix.Length)));
        }
        foreach (Match match in Regex.Matches(expression, Annotation)) AddCharacter(match.Groups[1].Value, match.Groups[2].Value);
        if (Regex.IsMatch(explicitReading.Trim(), @"^[ぁ-ゖァ-ヺー]+$")) AddCharacter(WrittenForm(expression), explicitReading.Trim());
        var full = explicitReading.Trim();
        if (!Regex.IsMatch(full, @"^[ぁ-ゖァ-ヺー]+$"))
        {
            full = Regex.Replace(expression.Trim(), Annotation, match => match.Groups[2].Value);
            if (!Regex.IsMatch(full, @"^[ぁ-ゖァ-ヺー]+$")) full = "";
        }
        var unresolved = Kanji(WrittenForm(expression)).Except(characters.Select(c => c.Kanji)).Any();
        return (full, characters.Distinct().ToList(), unresolved);
    }

    public static LearningAnalysisResult Analyze(ImportedCollection source, LearningRules? rules = null, DateTimeOffset? clock = null)
    {
        rules ??= new();
        var now = clock ?? DateTimeOffset.UtcNow;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(source.TimeZoneId);
        var through = Streaks.StudyDay(now, zone);
        var recentStart = through.AddMonths(-6);
        var history = source.Reviews.Where(r => r.Kind == 1 && r.Rating is >= 1 and <= 4 && r.Id <= now.ToUnixTimeMilliseconds()).ToLookup(r => r.CardId);
        var notes = source.Notes.ToDictionary(n => n.Id);
        var cards = new List<LearningCard>();
        foreach (var card in source.Cards)
        {
            if (!notes.TryGetValue(card.NoteId, out var note)) continue;
            var vocabulary = Under(card.Deck, rules.VocabularyRoot) && note.Category == "Vocabulary";
            var meaning = Under(card.Deck, rules.KanjiRoot) && note.Category == "Kanji";
            if (!vocabulary && !meaning) continue;
            var expression = string.IsNullOrWhiteSpace(note.SourceExpression) ? note.Expression : note.SourceExpression;
            var reading = Readings(expression, note.Reading);
            var reviews = history[card.Id].ToList();
            var recent = reviews.Where(r => Streaks.StudyDay(DateTimeOffset.FromUnixTimeMilliseconds(r.Id), zone) >= recentStart).ToList();
            cards.Add(new(card.Id, card.NoteId, WrittenForm(note.Expression), reading.FullReading, note.Meaning, card.Deck,
                vocabulary ? "Vocabulary" : "Kanji meaning", card.Queue, card.Type, reviews.Count(r => r.Rating == 1), card.LifetimeLapses,
                reviews.Count, recent.Count(r => r.Rating == 1), recent.Count, Under(card.Deck, rules.Known),
                Under(card.Deck, vocabulary ? rules.VocabularyRecovery : rules.KanjiRecovery),
                Under(card.Deck, rules.Immersion) || Under(card.Deck, rules.Unused), reading.Characters, vocabulary && reading.Unresolved));
        }
        var eligibleWords = cards.Where(c => c.Domain == "Vocabulary" && !c.Known && !c.Excluded).ToList();
        var knownWords = cards.Where(c => c.Domain == "Vocabulary" && c.Known && !c.Excluded).ToList();
        // Build a character index once; common characters do not trigger a full vocabulary scan per finding.
        var wordIndex = eligibleWords.SelectMany(c => Kanji(c.Word).Select(k => (Kanji: k, Card: c))).ToLookup(p => p.Kanji, p => p.Card);
        var knownIndex = knownWords.SelectMany(c => Kanji(c.Word).Select(k => (Kanji: k, Card: c))).ToLookup(p => p.Kanji, p => p.Card);
        var cross = new List<LearningFinding>();
        foreach (var group in cards.Where(c => c.Domain == "Kanji meaning" && !c.Excluded && Kanji(c.Word).Count() == 1 && c.Word.Length == 1).GroupBy(c => c.Word))
        {
            var meanings = group.ToList(); var words = wordIndex[group.Key].ToList(); var known = knownIndex[group.Key].ToList();
            var recoveryWords = words.Where(c => c.Recovery || c.Suspended).ToList();
            var flagged = words.Where(c => c.HistoryFlagged).ToList();
            var difficultMeaning = meanings.Any(c => c.HistoryFlagged);
            var supportedMeaning = meanings.All(c => !c.HistoryFlagged) && meanings.Any(c => !c.Recovery && !c.Suspended && c.Reviews >= 10);
            var kind = difficultMeaning && flagged.Count > 0 ? "Both decks" : supportedMeaning && flagged.Count > 0 ? "Vocabulary only"
                : difficultMeaning && flagged.Count == 0 && known.Count > 0 ? "Kanji meaning only" : "";
            if (kind.Length == 0) continue;
            var wordCount = flagged.Select(c => c.Word).Distinct().Count();
            var details = $"Meaning cards: {meanings.Sum(c => c.Lapses):N0} historical Again / {meanings.Sum(c => c.Reviews):N0} ordinary answers; "
                + $"{meanings.Sum(c => c.RecentLapses):N0} recent Again / {meanings.Sum(c => c.RecentReviews):N0} recent answers. "
                + $"Vocabulary: {wordCount:N0} distinct words with 3+ historical lapses; {known.Select(c => c.Word).Distinct().Count():N0} words in the known deck. "
                + $"{recoveryWords.Select(c => c.Word).Distinct().Count():N0} related recovery/suspended words (placement only). Categories compare historical lapse evidence, not recovery placement. "
                + (kind == "Vocabulary only" ? "Meaning cards are below threshold with at least 10 ordinary answers; this does not prove mastery. " : "")
                + "Word failures may concern reading or meaning; shared kanji does not establish the cause.";
            cross.Add(new(kind, group.Key, details, meanings.Concat(flagged).Concat(recoveryWords).Concat(known).Select(c => c.CardId).Distinct().ToList(), wordCount,
                meanings.Concat(flagged).Sum(c => c.RecentLapses), wordCount == 1 ? "Single vocabulary word · limited evidence" : "Cross-deck association"));
        }
        var groups = new List<LearningFinding>();
        var flaggedWords = eligibleWords.Where(c => c.HistoryFlagged && c.Word.Length > 0).ToList();
        void AddGroups(string kind, IEnumerable<(string Key, LearningCard Card)> entries, string explanation, int maximumWords = 12)
        {
            var findings = entries.GroupBy(p => p.Key).Select(g => (Key: g.Key, Cards: g.Select(p => p.Card).DistinctBy(c => c.CardId).ToList()))
                .Where(g => g.Cards.Select(c => c.Word).Distinct().Count() is >= 2 && g.Cards.Select(c => c.Word).Distinct().Count() <= maximumWords)
                .OrderByDescending(g => g.Cards.Sum(c => c.RecentLapses)).ThenByDescending(g => g.Cards.Sum(c => c.Lapses)).Take(150);
            foreach (var group in findings)
                groups.Add(new(kind, group.Key, explanation + " Every supporting card has 3+ historical ordinary-review Again answers. This is a candidate connection, not proof of confusion.",
                    group.Cards.Select(c => c.CardId).ToList(), group.Cards.Select(c => c.Word).Distinct().Count(), group.Cards.Sum(c => c.RecentLapses), "Candidate connection"));
        }
        AddGroups("Same reading", flaggedWords.Where(c => c.Reading.Length > 0).Select(c => (c.Reading, c)), "Different written words share an explicit whole-word reading.");
        AddGroups("Character reading", flaggedWords.SelectMany(c => c.CharacterReadings.Select(r => ($"{r.Kanji} → {r.Reading}", c))),
            "Single-kanji readings follow explicit furigana and matching surrounding kana. Related forms can share a root; these are not independent proof of a reading failure.");
        AddGroups("Written form", flaggedWords.Where(c => c.Word.Length is >= 2 and <= 6 && Kanji(c.Word).Count() >= 2)
            .SelectMany(c => Enumerable.Range(0, c.Word.Length).Select(i => (c.Word[..i] + "□" + c.Word[(i + 1)..], c))),
            "Written forms differ at one character position. This compares text overlap, not stroke or radical similarity.", 8);
        AddGroups("Meaning overlap", flaggedWords.SelectMany(c => Glosses(c.Meaning).Select(gloss => (gloss, c))),
            "Cards share a normalized English gloss from their stored meanings. English can have multiple senses; this is not a verified synonym group.", 8);
        return new(cards, cross.OrderByDescending(f => f.RecentLapses).ThenByDescending(f => f.FlaggedWords).ToList(),
            groups.OrderByDescending(f => f.RecentLapses).ThenByDescending(f => f.FlaggedWords).ToList(), recentStart, through, source.RefreshedAt);
    }
    private static IEnumerable<string> Glosses(string meaning) => Regex.Split(meaning.ToLowerInvariant(), @"[,;/\n]")
        .Select(text => Regex.Replace(text, @"\([^)]*\)", ""))
        .Select(text => Regex.Replace(text, @"\s+", " ").Trim())
        .Select(text => Regex.Replace(text, @"^(to |a |an |the )", ""))
        .Where(text => text.Length is >= 4 and <= 50 && !BroadGlosses.Contains(text) && Regex.IsMatch(text, @"^[a-z][a-z '-]+$"))
        .Distinct();
}
