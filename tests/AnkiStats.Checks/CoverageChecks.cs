using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AnkiStats.Core;
using Microsoft.Data.Sqlite;

internal static class CoverageChecks
{
    public static CoverageReference Reference() => new(new("synthetic", "2026-11-02", "synthetic", 1, "Synthetic fixture; no personal data"),
    [
        new("1000001", ["食べる", "喰べる"], [new("たべる", ["*"])], "eat | dine", true, ["N5"]),
        new("1000002", ["開く"], [new("あく", ["*"])], "open", true, ["N5", "N4"]),
        new("1000003", ["開く"], [new("ひらく", ["*"])], "open an event", true, ["N4"]),
        new("1000004", ["生"], [new("せい", ["*"])], "life", false, ["N3"]),
        new("1000005", ["制"], [new("せい", ["*"])], "system", false, ["N2"]),
        new("1000006", ["山"], [new("やま", ["*"])], "mountain", true, ["N5"]),
        new("1000007", ["外"], [new("そと", ["*"])], "outside", true, ["N1"]),
        new("1515630", ["報いる"], [new("むくいる", ["*"])], "reward", true, []),
        new("2005410", ["報う"], [new("むくう", ["*"])], "reward", false, []),
        new("2833992", ["報われる"], [new("むくわれる", ["*"])], "be rewarded", true, []),
        new("1000008", ["甲", "乙"], [new("こう", ["甲"]), new("おつ", ["乙"])], "restricted readings", false, [])
    ]);
    public static ImportedCollection Collection() => new()
    {
        CollectionKey = "synthetic-coverage", TimeZoneId = "UTC", Scope = "Synthetic Japanese", RefreshedAt = DateTimeOffset.Parse("2026-11-02T12:00:00Z"),
        Notes = [new(1, "食べる[たべる]", "", "eat", "Vocabulary", "Word", "食べる[たべる]"),
            new(2, "喰べる", "たべる", "eat", "Vocabulary", "Word"), new(3, "開く[あく]", "", "open", "Vocabulary", "Word"),
            new(4, "せい", "", "", "Vocabulary", "Word"), new(5, "山[やま]", "", "mountain", "Vocabulary", "Word"),
            new(6, "報いる", "むくいる", "reward", "Vocabulary", "Word"), new(7, "報う", "むくう", "reward", "Vocabulary", "Word"),
            new(8, "報われる", "むくわれる", "rewarded", "Vocabulary", "Word"), new(9, "未知語", "", "", "Vocabulary", "Word"),
            new(10, "字", "", "", "Kanji", "Kanji"), new(11, "開く", "", "", "Vocabulary", "Word"),
            new(12, "外", "そと", "outside", "Vocabulary", "Word")],
        Cards = [new(1, 1, "日本語::永遠", 2, 2, 0), new(101, 1, "日本語::永遠", 2, 2, 0),
            new(2, 2, "日本語::勉強してる", 0, 0, 0), new(3, 3, "日本語::勉強してる::Sub", -1, 2, 0),
            new(4, 4, "日本語::永遠", 2, 2, 0), new(5, 5, "日本語::Suspended", -1, 2, 0),
            new(6, 6, "日本語::永遠", 2, 2, 0), new(7, 7, "日本語::永遠", 2, 2, 0), new(8, 8, "日本語::永遠", 2, 2, 0),
            new(9, 9, "日本語::Other", 0, 0, 0), new(10, 10, "日本語::永遠", 2, 2, 0),
            new(11, 11, "日本語::永遠", 2, 2, 0), new(12, 12, "日本語::永遠に", 2, 2, 0)]
    };
    public static void Run(Action<bool, string> check, string sandbox)
    {
        var reference = Reference(); var matcher = new VocabularyMatcher(reference); var source = Collection();
        var result = matcher.Analyze(source);
        check(result.Matches.Count == 11, "coverage ignores kanji-category notes");
        check(result.Matches.Single(m => m.NoteId == 1).EntryId == "1000001", "whole-word furigana matches dictionary");
        check(VocabularyMatcher.Extract(new(99, "食べる", "", "", "Vocabulary", "Word", "食[た]べる")) == ("食べる", "たべる"), "segment furigana uses preserved source");
        check(VocabularyMatcher.Extract(new(99, "食べる", "タベル", "", "Vocabulary", "Word")).Reading == "たべる", "lookup folds katakana readings");
        check(VocabularyMatcher.Key("ｶﾀｶﾅ") == "かたかな", "lookup normalizes width without changing inventory");
        check(result.Known.Count == 4 && result.Familiar.Count == 2 && result.Combined.Count == 5, "known and familiar form union rather than sum");
        check(result.Database.Count == 7, "database includes suspended and other imported vocabulary");
        var overall = result.Counts.Single(c => c.List == "All entries");
        check(overall.Total == reference.Entries.Count && overall.Database == 7 && overall.Known == 4 && overall.Familiar == 2 && overall.Combined == 5,
            "overall dictionary totals include all reference entries and deduplicate variants and sibling cards");
        check(!result.Combined.Contains("1000006") && result.Database.Contains("1000006"), "Suspended deck contributes to database only");
        check(result.Familiar.Contains("1000002"), "queue suspension does not override named-deck familiarity rule");
        check(!result.Known.Contains("1000007"), "deck rules use hierarchy boundary rather than prefix");
        check(result.Matches.Single(m => m.NoteId == 4).EntryId is null && result.Matches.Single(m => m.NoteId == 4).Candidates.Count == 2, "ambiguous kana does not credit every homophone");
        check(result.Matches.Single(m => m.NoteId == 11).Status == "Ambiguous", "missing reading leaves homographs unresolved");
        check(matcher.Find("乙", "こう").Count == 0 && matcher.Find("甲", "こう").Count == 1, "reading-to-spelling restrictions respected");
        check(result.Matches.Single(m => m.NoteId == 9).Status == "Unmatched", "unknown expression stays unmatched");
        var n5 = result.Counts.Single(c => c.List == "N5");
        check(n5.Total == 3 && n5.Combined == 2 && n5.Database == 3, "entry-based denominator and deduplicated numerator");
        check(result.Counts.Single(c => c.List == "N4").Total == 2 && result.Counts.Single(c => c.List == "Through N4").Total == 4, "overlapping levels deduplicate in cumulative union");
        check(result.Counts.Single(c => c.List == "N2").KnownDisplay.StartsWith("0 / 1"), "unrepresented nonempty denominator is zero coverage");
        check(new CoverageCount("empty", 0, 0, 0, 0, 0).CombinedDisplay == "No data", "empty reference denominator is no data");
        check(result.Families.Count == 3 && result.Families.Single(f => f.Key.StartsWith("curated:", StringComparison.Ordinal)).Entries.Count == 3, "agreed reward words form one family without changing coverage");
        var custom = matcher.Analyze(source, families: [new("1000001", "synthetic"), new("1000002", "synthetic")]);
        check(custom.Families.Count == 2 && custom.Combined.SetEquals(result.Combined), "manual family grouping leaves reference coverage unchanged");
        var ambiguous = result.Matches.Single(m => m.NoteId == 4);
        var decision = new MatchDecision(4, ambiguous.Fingerprint, "1000004");
        var corrected = matcher.Analyze(source, [decision]);
        check(corrected.Known.Contains("1000004") && !corrected.Known.Contains("1000005"), "manual correction credits only selected entry");
        source.Notes[source.Notes.FindIndex(n => n.Id == 4)] = new(4, "生", "なま", "", "Vocabulary", "Word");
        check(matcher.Analyze(source, [decision]).Matches.Single(m => m.NoteId == 4).Status == "Stale decision", "changed vocabulary invalidates personal correction");
        source = Collection();
        check(matcher.Analyze(source, [decision with { EntryId = "9999999" }]).Matches.Single(m => m.NoteId == 4).Status == "Stale decision", "deleted reference identity requires correction review");
        var excluded = matcher.Analyze(source, [new(1, result.Matches.Single(m => m.NoteId == 1).Fingerprint, null, true)]);
        check(!excluded.Known.Contains("1000001") && excluded.Combined.Contains("1000001"), "exclusion is note-specific and retains duplicate evidence from another cohort");
        var profile = DeckProfiles.Select(source, new() { Name = "Familiar", DeckRules = [new("日本語::勉強してる", true)] });
        check(matcher.Analyze(profile).Known.Count == 0 && matcher.Analyze(profile).Database.Count == 2, "coverage respects active profile scope");
        var profileOverall = matcher.Analyze(profile).Counts.Single(c => c.List == "All entries");
        check(profileOverall.Total == overall.Total && profileOverall.Database == 2 && profileOverall.Known == 0,
            "overall dictionary denominator stays fixed while numerator follows active profile");
        var report = CoverageReport.Markdown(result, "N5", false);
        check(report.Contains("山") && report.Contains("| Yes | 1000006 |") && !report.Contains("| 食べる |"), "not-covered report includes suspended entry and database evidence");
        check(!CoverageReport.Markdown(result, "N5", true).Contains("| 山 |"), "absent-database report excludes already-added suspended entries");
        check(CoverageReport.Markdown(result, "Through N3", false).Contains("生"), "cumulative report uses same reference union as UI");
        check(report.Contains("synthetic") && report.Contains("unresolved") && report.Contains("Synthetic Japanese"), "report includes provenance scope and unresolved caveat");
        var missingReport = CoverageReport.Markdown(matcher.Analyze(new()), "N5", true);
        var overallReport = CoverageReport.Markdown(matcher.Analyze(new()), "All entries", true);
        check(overallReport.Contains("甲") && overallReport.Contains("1000008") && overallReport.Contains("11 entries without a confirmed match"),
            "overall missing-entry export includes non-common non-JLPT dictionary entries");
        check(missingReport.Contains("eat \\| dine"), "markdown report escapes table syntax in glosses");
        var folder = Path.Combine(sandbox, "coverage");
        var store = new CoverageReferenceStore(folder); store.Save(reference);
        check(store.Load()?.Entries.Count == reference.Entries.Count, "compressed reference roundtrip");
        try { store.Save(reference with { Entries = [] }); } catch (InvalidDataException) { }
        check(store.Load()?.Entries.Count == reference.Entries.Count, "failed reference replacement preserves prior bundle");
        var personal = new LocalStore(folder);
        personal.SaveCoverageDecisions("a", "matches", new List<MatchDecision> { decision });
        personal.SaveCoverageDecisions("a", "families", new List<FamilyDecision> { new("1000001", "synthetic") });
        check(personal.LoadCoverageDecisions<MatchDecision>("a", "matches").Single() == decision, "personal match decision survives persistence");
        check(personal.LoadCoverageDecisions<MatchDecision>("b", "matches").Count == 0, "personal corrections isolated by collection");
        store.Save(reference);
        check(personal.LoadCoverageDecisions<FamilyDecision>("a", "families").Single().Family == "synthetic", "reference replacement preserves personal families");
        var dictionary = """{"dictDate":"2026-11-02","words":[{"id":"1000001","kanji":[{"text":"食べる","common":true}],"kana":[{"text":"たべる","common":false,"appliesToKanji":["*"]}],"sense":[{"gloss":[{"lang":"eng","text":"eat"}]}]}]}""";
        var built = CoverageReferenceStore.Build(new MemoryStream(Encoding.UTF8.GetBytes(dictionary)),
            [Encoding.UTF8.GetBytes("""[{"jmdict_id":1000001,"level":"N5"},{"jmdict_id":1000001,"level":"N5"},{"level":"N3"},{"jmdict_id":9999999,"level":"N1"}]""")]);
        check(built.Entries.Single().Common && built.Entries.Single().Levels.SequenceEqual(new[] { "N5" }), "reference import deduplicates linked level membership");
        check(built.Metadata.UnlinkedJlptItems == 2, "reference import discloses unlinked and absent entry IDs");
    }

    public static void PrepareReference(string sources, string target)
    {
        using var dictionary = File.OpenRead(Path.Combine(sources, "jmdict", "jmdict-eng-3.6.2.json"));
        var reference = CoverageReferenceStore.Build(dictionary, Enumerable.Range(1, 5).Select(n => File.ReadAllBytes(Path.Combine(sources, $"n{n}.json"))));
        new CoverageReferenceStore(target).Save(reference);
        Console.WriteLine($"Reference prepared: {reference.Entries.Count:N0} dictionary entries; {reference.Metadata.UnlinkedJlptItems} unlinked JLPT rows.");
    }
    public static void Audit(string referenceRoot, string appDatabase, string? reportPath = null, IEnumerable<string>? requestedWords = null)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = appDatabase, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT payload FROM cache WHERE id=1";
        var bytes = (byte[])cmd.ExecuteScalar()!;
        using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        var data = JsonSerializer.Deserialize<ImportedCollection>(gzip)!;
        var matcher = new VocabularyMatcher(new CoverageReferenceStore(referenceRoot).Load()!);
        var timer = System.Diagnostics.Stopwatch.StartNew(); var result = matcher.Analyze(data);
        Console.WriteLine($"Local cached-data audit: {result.Matches.Count:N0} vocabulary notes; {result.Matches.Count(m => m.EntryIds.Count > 0):N0} wholly/partly confirmed; {result.Unresolved:N0} unresolved; {result.ReadingWarnings} reading warnings; {timer.Elapsed.TotalSeconds:F2}s matching. No personal text emitted; source Anki collection not opened.");
        foreach (var group in result.Matches.GroupBy(m => m.Status)) Console.WriteLine($"{group.Key}: {group.Count():N0}");
        if (reportPath is not null)
        {
            var ids = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(reportPath), @"\| (\d+) \|\s*$", System.Text.RegularExpressions.RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value).ToHashSet();
            var pending = result.Matches.SelectMany(m => m.ReviewEntryIds).ToHashSet();
            Console.WriteLine($"Prior report: {ids.Count} entries; now confirmed: {ids.Count(result.Database.Contains)}; remaining possible matches: {ids.Count(id => !result.Database.Contains(id) && pending.Contains(id))}; remaining without supporting candidates: {ids.Count(id => !result.Database.Contains(id) && !pending.Contains(id))}.");
            Console.WriteLine($"Current N5: {result.Counts.Single(c => c.List == "N5").DatabaseDisplay} in imported database.");
            // Optionally validate caller-specified reproductions against a private cache, without storing that cache as a fixture.
            foreach (var word in requestedWords ?? [])
            {
                var notes = result.Matches.Where(m => m.Expression == word).ToList();
                Console.WriteLine($"Requested example {word}: {string.Join("; ", notes.Select(m => m.Status + " → " + m.EntryIdsDisplay))}");
                if (notes.Count == 0 || notes.Any(m => m.EntryIds.Count == 0)) throw new Exception("Requested cached-data reproduction remains unresolved.");
            }
        }
    }
}
