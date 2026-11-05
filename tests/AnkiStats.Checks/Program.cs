using System.Security.Cryptography;
using AnkiStats.Core;
using Microsoft.Data.Sqlite;

var checkCount = 0;
if (args is ["--seed-mappings-ui", var mappingsUiRoot]) { CoverageUiFixture.Create(mappingsUiRoot, mappings: true); Console.WriteLine("Synthetic mappings fixture ready."); return; }
if (args is ["--media-revalidate", var revalidateReference, var revalidateRoot])
{
    var reference = new CoverageReferenceStore(revalidateReference).Load() ?? throw new InvalidOperationException("Install reference first.");
    var library = new MediaLibraryStore(revalidateRoot);
    foreach (var corpus in library.Load().Where(c => c.Source == "Jiten vocabulary"))
    {
        var revised = JitenClient.Revalidate(corpus, reference); library.Save(revised);
        Console.WriteLine($"Revalidated {revised.Title}: {revised.Words.Count} forms, {revised.Words.Where(w => w.EntryId is not null).Select(w => w.EntryId).Distinct().Count()} JMdict IDs, {revised.Words.Count(w => w.EntryId is null)} reference mismatches.");
    }
    return;
}
if (args is ["--seed-media-ui", var mediaUiRoot])
{
    CoverageUiFixture.Create(mediaUiRoot);
    var mediaStore = new MediaLibraryStore(mediaUiRoot);
    var mediaReferenceStore = new CoverageReferenceStore(Path.Combine(mediaUiRoot, "reference"));
    var mediaReference = mediaReferenceStore.Load()!;
    mediaReference.Entries.Add(new("9000001", ["共有語"], [new("きょうゆうご", ["*"])], "synthetic shared eligible word", true, ["N3"], new("共有語", "きょうゆうご")));
    mediaReference.Entries.Add(new("9000002", ["未登録語"], [new("みとうろくご", ["*"])], "synthetic shared uncommon word", false, [], new("未登録語", "みとうろくご")));
    mediaReferenceStore.Save(mediaReference with { Metadata = mediaReference.Metadata with { FormSelectionVersion = 1 } });
    for (var i = 0; i < 8; i++)
    {
        var words = new List<MediaWord> { new("1000001", "食べる", "たべる", 4, "Validated"), new("1000008", "甲", "こう", 2, "Validated"), new(null, "Synthetic unknown", "", 1, "Unmatched"),
            new("9000001", "共有語", "きょうゆうご", i + 1, "Resolved", Sentence: "共有語を調べる。"), new("9000002", "未登録語", "みとうろくご", 20 - i, "Resolved") };
        mediaStore.Save(new("synthetic:" + i, i == 0 ? "Synthetic Fate/stay night — UI fixture" : "Synthetic media " + i,
            7, "Synthetic edition", "Synthetic test corpus", "", "Synthetic words; no actual title script", MediaAnalysis.Hash(words),
            "Synthetic analyzer", "synthetic", DateTimeOffset.UtcNow, null, words, i == 0 ? MediaShelves.Reading : i < 4 ? MediaShelves.Planned : MediaShelves.Completed));
    }
    Console.WriteLine("Synthetic media UI fixture ready."); return;
}
if (args is ["--media-smoke", var mediaReferenceRoot, var mediaOutput])
{
    var reference = new CoverageReferenceStore(mediaReferenceRoot).Load() ?? throw new InvalidOperationException("Install the reference first.");
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    var client = new JitenClient(http);
    var title = (await client.Search("Fate/stay night", 7)).Single(t => t.Id == 233);
    var corpus = await client.Download(title, "Provider edition unverified; accepted as a nearby edition for remaster testing", reference,
        new Progress<string>(message => Console.WriteLine(message)));
    new MediaLibraryStore(mediaOutput).Save(corpus);
    Console.WriteLine($"Fate import complete: {corpus.Words.Count} forms; {corpus.Words.Where(w => w.EntryId is not null).Select(w => w.EntryId).Distinct().Count()} validated JMdict IDs; {corpus.Words.Count(w => w.EntryId is null)} reference mismatches. No Anki access.");
    return;
}
if (args is ["--local-media-smoke", var localReferenceRoot, var pythonPath])
{
    var reference = new CoverageReferenceStore(localReferenceRoot).Load() ?? throw new InvalidOperationException("Install the reference first.");
    const string text = "私は昨日、本を読んだ。食べた。山へ行く。";
    var analysis = await LocalMediaText.Analyze(text, pythonPath);
    var corpus = LocalMediaText.Resolve("Synthetic morphology smoke test", 7, "Synthetic", text, analysis, reference);
    if (!corpus.Words.Any(w => w.EntryId is not null)) throw new Exception("Local morphology resolved no dictionary entries.");
    if (!corpus.Words.Any(w => w.EntryId is not null && w.Sentence.Length > 0 && text.Contains(w.Sentence)))
        throw new Exception("Local morphology retained no genuine source sentence.");
    Console.WriteLine($"Local morphology passed: {analysis.Analyzer}; {corpus.Words.Count} token forms, {corpus.Words.Count(w => w.EntryId is not null)} resolved. Synthetic text only.");
    return;
}
if (args is ["--prepare-coverage-reference", var referenceSources, var referenceTarget]) { CoverageChecks.PrepareReference(referenceSources, referenceTarget); return; }
if (args is ["--audit-coverage", var coverageReferenceRoot, var appDatabase]) { CoverageChecks.Audit(coverageReferenceRoot, appDatabase); return; }
if (args.Length >= 4 && args[0] == "--audit-coverage-report") { CoverageChecks.Audit(args[1], args[2], args[3], args.Skip(4)); return; }
if (args is ["--seed-coverage-ui", var coverageRoot]) { CoverageUiFixture.Create(coverageRoot); Console.WriteLine("Synthetic coverage fixture ready."); return; }
if (args.Length >= 3 && args[0] is "--audit" or "--connect-local")
{
    LocalAudit.Run(args[1], args[2], args[0] == "--connect-local");
    return;
}
if (args is ["--metadata", var metadataProfile])
{
    var root = Path.Combine(Path.GetTempPath(), "AnkiStatsMetadata-" + Guid.NewGuid().ToString("N"));
    try
    {
        var meta = new CollectionReader(root, () => new AnkiConnectClient()).Discover(metadataProfile).GetAwaiter().GetResult();
        foreach (var deck in meta.Decks) Console.WriteLine($"Deck: {deck.Name} · direct cards {deck.Cards}");
        foreach (var type in meta.NoteTypes) Console.WriteLine($"Type: {type.Name} · {string.Join(" | ", type.Fields.Select((f,i) => $"{i}:{f}"))}");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    return;
}
if (args is ["--seed-ui", var uiRoot]) { UiFixture.Create(uiRoot); Console.WriteLine("Synthetic UI fixture ready."); return; }
if (args is ["--seed-learning-ui", var learningRoot]) { LearningUiFixture.Create(learningRoot); Console.WriteLine("Synthetic learning-pattern fixture ready."); return; }
if (args is ["--inspect-live", var liveProfile])
{
    using var liveClient = new AnkiConnectClient();
    var reader = new LiveCollectionImporter(liveClient);
    var metadata = await reader.Discover(liveProfile);
    var settings = new Settings { ProfileFolder = liveProfile,
        DeckRules = metadata.Decks.Select(d => new DeckRule(d.Name, true)).ToList(),
        Mappings = metadata.NoteTypes.Select(t => new FieldMapping { NoteTypeId = t.Id, Category = "Vocabulary", Expression = 0 }).ToList() };
    var imported = await reader.Import(settings);
    if (imported.Cards.Count == 0 || imported.Notes.Count == 0 || imported.ImportMethod != "AnkiConnect") throw new Exception("Live import returned no collection data.");
    Console.WriteLine("Live AnkiConnect discovery and full import succeeded. No settings saved or notes created.");
    return;
}
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checkCount++;
}
void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); } catch (T) { checkCount++; return; }
    throw new Exception("FAILED: " + message);
}

// Optional local validation deliberately prints no profile, field names, or card text.
if (args is ["--inspect", var inspectedProfile])
{
    var root = Path.Combine(Path.GetTempPath(), "AnkiStatsInspection-" + Guid.NewGuid().ToString("N"));
    try
    {
        var importer = new CollectionImporter(root);
        var file = Path.Combine(inspectedProfile, "collection.anki2");
        var before = SHA256.HashData(File.ReadAllBytes(file));
        var sidecars = new[] { "-wal", "-shm", "-journal" }.ToDictionary(s => s, s => File.Exists(file + s));
        var meta = importer.Discover(inspectedProfile);
        Check(meta.NoteTypes.Count > 0 && meta.Decks.Count > 0, "real metadata discovery");
        var localSettings = new Settings { ProfileFolder = inspectedProfile,
            DeckRules = meta.Decks.Where(d => !d.Name.Contains("::")).Select(d => new DeckRule(d.Name, true)).ToList(),
            Mappings = meta.NoteTypes.Select(t => new FieldMapping { NoteTypeId = t.Id, Category = "Vocabulary", Expression = 0 }).ToList() };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var imported = importer.Import(localSettings);
        var result = Analyzer.Analyze(imported);
        Check(result.Vocabulary.Count > 0 && result.TotalAnswers > 0, "real import and analysis");
        var store = new LocalStore(root);
        store.SaveCache(imported);
        Check(store.LoadCache(localSettings.CollectionKey)?.Cards.Count == imported.Cards.Count, "real cache roundtrip");
        Check(SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(before), "source hash unchanged");
        Check(sidecars.All(p => File.Exists(file + p.Key) == p.Value), "no source sidecars created");
        Console.WriteLine($"Local validation passed: schema {meta.Schema}; import, analysis, cache, source hash and sidecar checks successful in {clock.Elapsed.TotalSeconds:F1}s. No personal data emitted.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return;
}

var sandbox = Path.Combine(Path.GetTempPath(), "AnkiStatsChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
try
{
    StreakChecks.Run(Check);
    MappingChecks.Run(Check, sandbox);
    MediaChecks.Run(Check, sandbox);
    ProfileChecks.Run(Check, sandbox);
    TimeChecks.Run(Check);
    LearningChecks.Run(Check);
    CoverageChecks.Run(Check, sandbox);
    CoverageMatchingChecks.Run(Check, sandbox);
    DefinitionChecks.Run(Check);
    NoteCreationChecks.Run(Check, sandbox);
    AnkiTransportChecks.Run(Check);
    Check(TextNormalizer.Clean("<b>報いる</b>") == "報いる", "markup extraction");
    Check(TextNormalizer.Clean("<ruby>漢字<rt>かんじ</rt></ruby>") == "漢字", "ruby extraction");
    Check(TextNormalizer.Clean("漢字[かんじ]", true) == "漢字", "explicit bracket-reading removal");
    Check(TextNormalizer.Clean("漢字[かんじ]") == "漢字[かんじ]", "bracket conventions opt in");
    Check(TextNormalizer.Clean("<script>bad()</script>報う[sound:a.mp3]") == "報う", "inert extraction");
    Check(TextNormalizer.Clean("<img src=x>") == "", "media-only extraction");
    Check(CsvExport.Cell(" =SUM(A1)").StartsWith("\"'"), "CSV formula protection after whitespace");
    Check(CsvExport.Cell("a,\"b\"") == "\"a,\"\"b\"\"\"", "CSV escaping");
    var selection = new Settings { ProfileFolder = sandbox, DeckRules = [new("Language", true), new("Language::Later", false)] };
    Check(selection.Includes("Language::Vocabulary"), "parent inclusion");
    Check(!selection.Includes("Language::Later::Imported"), "subtree exclusion");
    Check(!selection.Includes("Other"), "unrelated decks excluded");

    long Stamp(int day, int hour, int offset = 0) => new DateTimeOffset(2026, 10, day, hour, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() + offset;
    var data = new ImportedCollection { CollectionKey = "synthetic", Signature = "scope-1", TimeZoneId = "UTC", RefreshedAt = DateTimeOffset.UtcNow,
        Notes = [new(1, "報う", "むくう", "reward", "Vocabulary", "Word"), new(2, "報いる", "むくいる", "reward", "Vocabulary", "Word"), new(3, "報われる", "むくわれる", "be rewarded", "Vocabulary", "Word"), new(4, "報う", "むくう", "reward", "Vocabulary", "Word"), new(5, "", "", "", "Vocabulary", "Word"), new(6, "conditional", "", "", "Grammar", "Grammar"), new(7, "報う", "alternate", "different reading", "Vocabulary", "Word")],
        Cards = [new(11, 1, "Language", -1, 0, 8), new(12, 1, "Language", 2, 2, 0), new(13, 2, "Language", 2, 2, 0), new(14, 3, "Language", -2, 2, 1), new(15, 4, "Language", 2, 2, 0), new(16, 5, "Language", 0, 0, 0), new(17, 6, "Language", 2, 2, 0), new(18, 7, "Language", 2, 2, 0)],
        Reviews = [new(Stamp(30, 0), 11, 1, 1000, 1), new(Stamp(30, 5), 11, 3, 3000, 1), new(Stamp(30, 6), 13, 3, 0, 1), new(Stamp(30, 12), 14, 2, 2000, 1), new(Stamp(30, 18), 17, 1, 5000, 0), new(Stamp(30, 18, 1), 17, 1, 5000, 2), new(Stamp(30, 18, 2), 17, 4, 5000, 3), new(Stamp(30, 18, 3), 17, 0, 5000, 4), new(Stamp(30, 18, 4), 17, 3, 5000, 5), new(Stamp(30, 18, 5), 17, 3, 5000, 99), new(Stamp(1, 0), 11, 3, 1000, 1)] };
    var now = new DateTimeOffset(2026, 10, 30, 20, 0, 0, TimeSpan.Zero);
    var result = Analyzer.Analyze(data, 30, 1, now);
    Check(result.Vocabulary.Count == 4, "dictionary entries stay distinct, duplicate notes merge, readings separate");
    Check(result.Vocabulary.Single(v => v.Expression == "報う" && v.Reading == "むくう").Cards == 3, "reverse cards do not multiply entries; suspended cards included");
    Check(result.Skipped.Count == 1, "empty expressions inspectable");
    Check(result.OrdinaryReviews == 5 && result.Again == 1, "ordinary review denominator excludes administrative and unknown kinds");
    var allHistory = Analyzer.Analyze(data, 30, null, now);
    Check(allHistory.TotalAnswers == 8 && allHistory.OrdinaryReviews == 5, "overview total includes learning and relearning, ordinary review count stays separate");
    Check(allHistory.Insights.All(i => !string.IsNullOrWhiteSpace(i.Explanation)), "every discovery explains its measurement");
    var consistency = allHistory.Insights.Single(i => i.Id == "active-days");
    Check(consistency.Detail.Contains("2 of 30") && consistency.Explanation.Contains("midnight"), "consistency shows denominator and calendar-day definition");
    Check(result.Buckets[0].MedianSeconds == 1 && result.Buckets[0].Answers == 3, "odd median");
    var recent = Analyzer.Analyze(data, 1, 1, now);
    Check(recent.Buckets[0].MedianSeconds == 2, "even median");
    Check(recent.Buckets[1].MedianSeconds is null && recent.Buckets[1].Answers == 1, "zero durations excluded, answers retained");
    Check(recent.Buckets[1].TimedAnswers == 0, "timing sample size separate from answer count");
    Check(recent.Buckets[3].AgainPercent is null, "empty bucket is no data");
    Check(recent.Buckets[1].Answers == 1 && recent.Buckets[2].Answers == 1, "06 and 12 bucket boundaries");
    Check(Analyzer.Analyze(data, 1, null, now).TotalAnswers == 7, "all rated kinds excludes administrative events");
    Check(Analyzer.Analyze(data, 1, 0, now).TotalAnswers == 1, "learning separated");
    Check(recent.Rankings.First().Reviews == 2 && recent.Rankings.First().LifetimeLapses == 8, "date-range rank separate from lifetime lapses");
    Check(Analyzer.Analyze(data, 1, 1, now.AddDays(1)).TotalAnswers == 0, "date range boundaries");

    // The repeated DST hour belongs to the same local time bucket.
    var dst = new ImportedCollection { TimeZoneId = "Eastern Standard Time", Reviews = [new(new DateTimeOffset(2026,11,1,5,30,0,TimeSpan.Zero).ToUnixTimeMilliseconds(),11,3,1000,1), new(new DateTimeOffset(2026,11,1,6,30,0,TimeSpan.Zero).ToUnixTimeMilliseconds(),11,1,3000,1)] };
    var dstResult = Analyzer.Analyze(dst, 1, 1, new DateTimeOffset(2026,11,1,12,0,0,TimeSpan.Zero));
    Check(dstResult.Buckets[0].Answers == 2 && dstResult.Daily.Count == 1, "DST repeated hour");

    var store = new LocalStore(Path.Combine(sandbox, "store"));
    store.SaveCache(data);
    Check(store.LoadCache("synthetic")?.Reviews.Count == data.Reviews.Count, "cache roundtrip");
    Check(store.LoadCache("other") is null, "cache cannot cross collection identity");
    store.SaveCache(data, selection);
    Check(store.LoadSettings()?.DeckRules.Count == 2, "settings and results persisted together");
    var csvPath = Path.Combine(sandbox, "synthetic.csv");
    CsvExport.Write(csvPath, [new object?[] { "報いる", "a,b", "=1+1" }]);
    Check(File.ReadAllText(csvPath).Contains("報いる") && File.ReadAllText(csvPath).Contains("'=1+1"), "UTF-8 Japanese export and formula protection");
    var candidates = Enumerable.Range(0, 10).Select(i => new Insight("template-" + i, "Title", "Detail")).ToList();
    var history = new List<string>();
    for (var i = 0; i < 24; i++)
    {
        var day = new DateOnly(2026, 10, 1).AddDays(i);
        var chosen = store.SelectInsight("synthetic", day, candidates);
        Check(!history.TakeLast(7).Contains(chosen.Id), "seven-day repeat exclusion");
        Check(new LocalStore(Path.Combine(sandbox, "store")).SelectInsight("synthetic", day, candidates).Id == chosen.Id, "same day stable across restart");
        history.Add(chosen.Id);
    }
    Check(store.SelectInsight("synthetic", new(2026,10,24), []).Id == "none", "unsupported same-day template fallback");
    Check(store.SelectInsight("empty", new(2026,10,24), []).Id == "none", "insufficient template fallback");

    // Synthetic schema 18 includes Anki's collation and normalized metadata.
    var profile = Path.Combine(sandbox, "profile"); Directory.CreateDirectory(profile);
    var file = Path.Combine(profile, "collection.anki2");
    using (var db = new SqliteConnection($"Data Source={file};Pooling=False"))
    {
        db.Open(); db.CreateCollation("unicase", (a,b) => StringComparer.OrdinalIgnoreCase.Compare(a,b));
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
        CREATE TABLE col(ver INTEGER); INSERT INTO col VALUES(18);
        CREATE TABLE decks(id INTEGER PRIMARY KEY,name TEXT COLLATE unicase); INSERT INTO decks VALUES(1,'Language');
        CREATE TABLE notetypes(id INTEGER PRIMARY KEY,name TEXT COLLATE unicase); INSERT INTO notetypes VALUES(1,'Word');
        CREATE TABLE fields(ntid INTEGER,ord INTEGER,name TEXT COLLATE unicase,PRIMARY KEY(ntid,ord)) WITHOUT ROWID;
        CREATE UNIQUE INDEX fields_name ON fields(ntid,name); INSERT INTO fields VALUES(1,0,'Expression');
        CREATE TABLE notes(id INTEGER PRIMARY KEY,mid INTEGER,flds TEXT); INSERT INTO notes VALUES(1,1,'報いる');
        CREATE TABLE cards(id INTEGER PRIMARY KEY,nid INTEGER,did INTEGER,odid INTEGER,queue INTEGER,type INTEGER,lapses INTEGER);
        INSERT INTO cards VALUES(1,1,999,1,-1,0,0);
        CREATE TABLE revlog(id INTEGER PRIMARY KEY,cid INTEGER,ease INTEGER,time INTEGER,type INTEGER);
        INSERT INTO revlog VALUES(1793318400000,1,1,1000,1);
        INSERT INTO revlog VALUES(1793318400001,999,1,1000,1);
        """;
        cmd.ExecuteNonQuery();
    }
    var beforeHash = SHA256.HashData(File.ReadAllBytes(file));
    var importer = new CollectionImporter(Path.Combine(sandbox, "temporary"));
    var metadata = importer.Discover(profile);
    Check(metadata.Schema == 18 && metadata.NoteTypes[0].Fields[0] == "Expression", "normalized metadata and custom collation");
    Check(metadata.Decks[0].Cards == 1, "filtered deck counts use original deck");
    var imported = importer.Import(new Settings { ProfileFolder = profile, DeckRules = [new("Language",true)], Mappings = [new() { NoteTypeId=1,Category="Vocabulary",Expression=0 }] });
    Check(imported.SourceProfileFolder == profile && !imported.IsSynthetic, "import retains its actual source identity");
    File.WriteAllText(Path.Combine(profile,".ankistats-test-data"), "Synthetic fixture");
    Check(importer.Import(new Settings { ProfileFolder = profile, DeckRules = [new("Language",true)], Mappings = [new() { NoteTypeId=1,Category="Vocabulary",Expression=0 }] }).IsSynthetic, "synthetic sources explicitly marked");
    Check(imported.Cards.Count == 1 && imported.Notes.Count == 1 && imported.UnresolvedReviews == 1, "selection, suspended cards, orphaned reviews");
    Throws<InvalidDataException>(() => importer.Import(new Settings { ProfileFolder = profile, DeckRules = [new("Language",true)], Mappings = [new() { NoteTypeId=1, Category="Vocabulary", Expression=0, ExpressionName="Renamed field" }] }), "mapping drift rejected");
    Check(SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(beforeHash), "source bytes unchanged");
    Check(!File.Exists(file+"-wal") && !File.Exists(file+"-shm"), "no source SQLite sidecars");
    var bracketProfile = Path.Combine(sandbox, "bracket-profile"); Directory.CreateDirectory(bracketProfile);
    File.Copy(file, Path.Combine(bracketProfile, "collection.anki2"));
    using (var bracketDb = new SqliteConnection($"Data Source={Path.Combine(bracketProfile, "collection.anki2")};Pooling=False"))
    {
        bracketDb.Open(); using var bracketCommand = bracketDb.CreateCommand();
        bracketCommand.CommandText = "UPDATE notes SET flds='晴れる[はれる]'"; bracketCommand.ExecuteNonQuery();
    }
    var bracketData = importer.Import(new Settings { ProfileFolder = bracketProfile, DeckRules = [new("Language", true)],
        Mappings = [new() { NoteTypeId = 1, Category = "Vocabulary", Expression = 0, StripBracketReadings = true }] });
    Check(bracketData.Notes[0].Expression == "晴れる" && bracketData.Notes[0].SourceExpression == "晴れる[はれる]", "import preserves furigana separately from stripped inventory expression");
    Check(!LearningAnalyzer.Readings(bracketData.Notes[0].SourceExpression).Unresolved, "preserved imported furigana remains usable by learning analysis");
    LiveImportChecks.Run(Check, sandbox, file);
    using (var held = new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        Throws<IOException>(() => importer.Discover(profile), "file importer refuses locked sources for live-reader fallback");
    File.WriteAllBytes(file+"-wal", [1,2,3]);
    Throws<IOException>(() => importer.Discover(profile), "file importer refuses nonempty WAL for live-reader fallback"); File.Delete(file+"-wal");
    File.WriteAllBytes(file+"-journal", [1,2,3]);
    Throws<IOException>(() => importer.Discover(profile), "file importer refuses rollback journals for live-reader fallback"); File.Delete(file+"-journal");
    Check(Directory.GetFiles(Path.Combine(sandbox,"temporary")).Length == 0, "temporary copies cleaned after failures");
    using (var db = new SqliteConnection($"Data Source={file};Pooling=False")) { db.Open(); using var cmd=db.CreateCommand(); cmd.CommandText="UPDATE col SET ver=999"; cmd.ExecuteNonQuery(); }
    Throws<InvalidDataException>(() => importer.Discover(profile), "unsupported schema rejected");
    Check(store.LoadCache("synthetic")?.Signature == "scope-1", "failed import leaves cache intact");
    Throws<InvalidDataException>(() => importer.Discover(Path.Combine(sandbox,"missing")), "missing profile recovery");
    var legacy = Path.Combine(sandbox,"legacy"); Directory.CreateDirectory(legacy);
    using (var db = new SqliteConnection($"Data Source={Path.Combine(legacy,"collection.anki2")};Pooling=False"))
    {
        db.Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
        CREATE TABLE col(ver INTEGER,decks TEXT,models TEXT);
        INSERT INTO col VALUES(11,'{"1":{"id":1,"name":"Language"}}','{"1":{"id":1,"name":"Word","flds":[{"ord":0,"name":"Expression"}]}}');
        CREATE TABLE cards(id INTEGER,nid INTEGER,did INTEGER,odid INTEGER,queue INTEGER,type INTEGER,lapses INTEGER);
        INSERT INTO cards VALUES(1,1,1,0,0,0,0);
        CREATE TABLE notes(id INTEGER,mid INTEGER,flds TEXT); INSERT INTO notes VALUES(1,1,'報う');
        CREATE TABLE revlog(id INTEGER,cid INTEGER,ease INTEGER,time INTEGER,type INTEGER);
        """;
        cmd.ExecuteNonQuery();
    }
    Check(importer.Discover(legacy).NoteTypes[0].Fields[0] == "Expression", "legacy JSON metadata");
    Check(importer.Import(new Settings { ProfileFolder=legacy, DeckRules=[new("Language",true)],Mappings=[new() {NoteTypeId=1,Category="Vocabulary"}] }).Notes[0].Expression == "報う", "legacy full import");
    Console.WriteLine($"All {checkCount} checks passed: metrics, study time, learning patterns, Japanese entries, deck profiles, event scopes, time zones, CSV, insight rotation, cache, and source safety.");
}
finally { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); }
