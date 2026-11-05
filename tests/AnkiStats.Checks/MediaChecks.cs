using System.Net;
using System.Text;
using AnkiStats.Core;

internal static class MediaChecks
{
    public static void Run(Action<bool, string> check, string sandbox)
    {
        var reference = CoverageChecks.Reference();
        var source = CoverageChecks.Collection();
        var coverage = new VocabularyMatcher(reference).Analyze(source, [], []);
        var words = new List<MediaWord> { new("1000001", "食べる", "たべる", 4, "Validated"), new("1000001", "喰べる", "たべる", 2, "Validated"),
            new("1000005", "制", "せい", 1, "Validated"), new("1000008", "甲", "こう", 1, "Validated"), new(null, "謎", "", 1, "Unmatched"), new("removed", "?", "", 1, "Validated") };
        var corpus = new MediaCorpus("test", "Synthetic media", 7, "Unverified", "Test", "", "Synthetic", MediaAnalysis.Hash(words), "Test", "synthetic", DateTimeOffset.UtcNow, null, words);
        var summary = MediaAnalysis.Compare(corpus, coverage);
        check(summary.Eligible == 2 && summary.Words.Single(w => w.EntryId == "1000001").Occurrences == 6, "media deduplicates forms and overlapping Common/JLPT entries");
        check(summary.Present == 1 && summary.NeedsReview == 1 && summary.Remaining == 0, "ambiguous Anki matches are review candidates, not confidently missing");
        check(summary.Other == 1 && summary.Unresolved == 2, "non-list words and missing source IDs stay separate");
        var missingCoverage = new VocabularyMatcher(reference).Analyze(new ImportedCollection()) with
        { Database = ["1000001"], Matches = coverage.Matches.Where(m => m.NoteId == 4).ToList() };
        var completedA = corpus with { Id = "a", Title = "Completed A", State = MediaShelves.Completed, Words = [
            new("1000001", "食べる", "", 10, "Resolved"), new("1000002", "開く", "", 2, "Resolved"), new("1000002", "開く", "", 3, "Resolved"),
            new("1000006", "山", "", 2, "Resolved"), new("2005410", "報う", "", 1, "Resolved"), new("1000004", "生", "", 1, "Resolved")] };
        var completedB = completedA with { Id = "b", Title = "Completed B", Words = [new("1000002", "開く", "", 3, "Resolved"),
            new("1000006", "山", "", 99, "Resolved"), new("2005410", "報う", "", 2, "Resolved"), new("1000008", "甲", "", 1, "Resolved")] };
        var plannedContext = corpus with { Id = "context", Title = "Local context", State = MediaShelves.Planned,
            Words = [new("2005410", "報う", "", 1000, "Resolved", Sentence: "努力に報う。")] };
        var library = new[] { completedA, completedB, completedA, plannedContext };
        var commonMining = MediaMining.Missing(library, missingCoverage, 2, true);
        check(commonMining.Select(w => w.EntryId).SequenceEqual(["1000006", "1000002"]) && commonMining[1].TitleCount == 2 && commonMining[1].Occurrences == 8,
            "completed mining deduplicates title/forms, excludes other shelves and ranks title count then occurrences");
        check(MediaMining.Missing(library, missingCoverage, 1, true).All(w => w.EntryId is not ("1000001" or "1000004")),
            "completed mining excludes existing Anki vocabulary and unresolved possible matches");
        var uncommonMining = MediaMining.Missing(library, missingCoverage, 2, false);
        check(uncommonMining.Count == 1 && uncommonMining[0].EntryId == "2005410" && uncommonMining[0].Occurrences == 3,
            "uncommon mining means outside both Common and JLPT with adjustable title minimum");
        check(uncommonMining[0].Sentence == "努力に報う。" && uncommonMining[0].SentenceSource == "Local context",
            "a completed-media missing word can use a resolved source sentence from another local library script");
        check(MediaMining.Missing(library, missingCoverage, 1, false).Count == 2, "lower minimum reveals words limited to one completed title");
        var chosenTitle = completedA with { State = MediaShelves.Reading, Words = [.. completedA.Words,
            new("2005410", "報う", "", 2, "Resolved", Sentence: "努力に報う。"), new(null, "未解決", "", 1, "Unmatched")] };
        var chosen = MediaMining.Selected(chosenTitle, missingCoverage, ["1000002", "2005410", "1000001", "1000004", "not-in-title", "", "1000002"]);
        check(chosen.Select(w => w.EntryId).ToHashSet().SetEquals(["1000002", "2005410"]) && chosen.Count == 2,
            "word-list selection includes only selected missing identities and excludes existing, possible, unresolved and foreign IDs");
        check(chosen.Any(w => w.Eligible) && chosen.Any(w => !w.Eligible) && chosen.All(w => w.TitleCount == 1 && w.Titles == chosenTitle.Title),
            "word-list selection supports Common/JLPT and other words from an individual reading title without a cross-title threshold");
        check(chosen.Single(w => w.EntryId == "1000002").Occurrences == 5 && chosen.Single(w => w.EntryId == "2005410").Sentence == "努力に報う。",
            "word-list selection deduplicates forms and retains an actual title sentence");
        check(MediaMining.Selected(chosenTitle with { State = MediaShelves.Planned }, missingCoverage, ["1000002"]).Count == 1
            && MediaMining.Selected(chosenTitle, missingCoverage, []).Count == 0, "planned titles support manual selection and empty selection never imports a fallback batch");
        check(MediaAnalysis.Compare(corpus with { Words = [] }, coverage).Percent == "No eligible words", "empty mining pool has no invented percentage");
        var settings = new Settings { ProfileFolder = sandbox, Profiles = [new() { DeckRules = [new("Selected", true)] }] };
        var all = MediaAnalysis.CollectionSettings(settings, new(18, [new(1, "Excluded", 1)], []));
        check(all.Includes("Excluded") && all.Profiles.Count == 0 && settings.Profiles.Count == 1, "media all-deck import does not mutate profile settings");
        var store = new MediaLibraryStore(Path.Combine(sandbox, "media-store"));
        var legacyArtwork = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[] {
            new { EntryId = corpus.Id, Form = "", Reading = "", Occurrences = 0, Status = "", SourceEntryId = (string?)null } })));
        var legacyArtworkPath = Path.Combine(sandbox, "media-store", "media", "covers", legacyArtwork + ".jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyArtworkPath)!); File.WriteAllText(legacyArtworkPath, "Synthetic artwork marker");
        check(store.ArtworkPath(corpus.Id) == legacyArtworkPath, "adding sentence metadata preserves existing cached cover paths");
        var legacyWord = System.Text.Json.JsonSerializer.Deserialize<MediaWord>("""{"EntryId":"1000001","Form":"食べる","Reading":"たべる","Occurrences":1,"Status":"Resolved"}""")!;
        check(legacyWord.Sentence == "", "older vocabulary caches load with blank source sentences");
        store.Save(corpus); store.Save(corpus with { State = "Completed" });
        var saved = store.Load().Single();
        check(saved.State == "Completed" && saved.Words.Count == words.Count && saved.ContentHash == corpus.ContentHash, "media corpus cache and status roundtrip");
        var planned = MediaShelves.Move(corpus, MediaShelves.Planned, [corpus]);
        store.Save(planned);
        check(store.Load().Single().State == MediaShelves.Planned && planned.Words == corpus.Words,
            "planned shelf persists without changing downloaded vocabulary");
        var first = corpus with { Id = "first" }; var second = corpus with { Id = "second" };
        try { MediaShelves.Move(planned, MediaShelves.Reading, [first, second, planned]); check(false, "third reading title should be rejected"); }
        catch (InvalidOperationException) { check(planned.State == MediaShelves.Planned, "reading shelf limit preserves planned title on rejection"); }
        check(MediaShelves.Move(first, MediaShelves.Reading, [first, second]).State == MediaShelves.Reading,
            "existing reading title does not count twice toward the two-title limit");
        check(LocalMediaText.Clean("｜食べる《たべる》［＃注記］<ruby>山<rt>やま</rt></ruby>").Replace(" ", "") == "食べる山", "local cleaning removes ruby and editorial text");
        var srt = Path.Combine(sandbox, "media.srt"); File.WriteAllText(srt, "1\n00:00:01,000 --> 00:00:03,000\n<b>食べる</b>\n");
        check(LocalMediaText.Read(srt) == "食べる", "local subtitles exclude counters, timing and markup");
        var vtt = Path.Combine(sandbox, "media.vtt"); File.WriteAllText(vtt, "WEBVTT\n\nSTYLE\n::cue { color: red; }\n\ncue-id\n00:00:01.000 --> 00:00:03.000\n山\n");
        check(LocalMediaText.Read(vtt) == "山", "WebVTT import excludes headers, cue identifiers and styles");
        var local = LocalMediaText.Resolve("Local", 7, "Test", "食べた 山 開く", new("Synthetic analyzer", [
            new("食べた", "食べる", "食べる", "タベタ", "verb", 1), new("山", "山", "山", "ヤマ", "noun", 2), new("開く", "開く", "開く", "", "verb", 1)]), reference);
        check(local.Words[0].EntryId == "1000001" && local.Words[1].EntryId == "1000006", "local inflections resolve without comparing an inflected reading to a lemma reading");
        check(local.Words[2].EntryId is null && local.Words[2].Status.StartsWith("Ambiguous"), "local homographs never credit all possible dictionary entries");
        var sentenceCorpus = LocalMediaText.Resolve("Context", 7, "Synthetic", "山へ行く。", new("Synthetic", [new("山", "山", "山", "ヤマ", "noun", 1, "山へ行く。")]), reference);
        check(sentenceCorpus.Words.Single().Sentence == "山へ行く。", "local resolved vocabulary retains the actual analyzer source sentence");
        var furiganaReference = reference with { Entries = [.. reference.Entries,
            new("1291170", ["差し込む"], [new("さしこむ", ["*"])], "insert", true, [])] };
        var annotated = JitenClient.Revalidate(corpus with { Words = [new(null, "差[さ]し込[こ]む", "", 1, "Unverified", "1291170")] }, furiganaReference);
        check(annotated.Words[0].EntryId == "1291170" && annotated.Words[0].Reading == "さしこむ", "Jiten multi-segment furigana retains intervening okurigana");
        var kanaReference = reference with { Entries = [.. reference.Entries,
            new("1602040", ["一人ぼっち"], [new("ひとりぼっち", ["一人ぼっち"])], "solitude", false, []),
            new("2827761", ["姦しい", "喧しい"], [new("かしましい", ["姦しい"]), new("かまびすしい", ["喧しい"])], "noisy", false, [])] };
        var kanaValidated = JitenClient.Revalidate(corpus with { Words = [new(null, "ひとりぼっち", "", 1, "Reference mismatch", "1602040"),
            new(null, "かしましい", "", 1, "Reference mismatch", "2827761"), new(null, "喧[かしま]しい", "", 1, "Reference mismatch", "2827761"),
            new(null, "架空人物[かくうじんぶつ]", "", 1, "Reference mismatch", "5999999"), new(null, "別の語", "", 1, "Reference mismatch", "1602040")] }, kanaReference);
        check(kanaValidated.Words[0].EntryId == "1602040" && kanaValidated.Words[1].EntryId == "2827761",
            "Jiten kana headwords validate even when readings restrict their kanji spellings");
        check(kanaValidated.Words[2].EntryId is null && kanaValidated.Words[2].Status.StartsWith("Source reading"),
            "Jiten continues enforcing reading restrictions on written kanji forms");
        check(kanaValidated.Words[3].EntryId is null && kanaValidated.Words[3].Status.Contains("absent from installed JMdict")
            && kanaValidated.Words[4].Status.StartsWith("Source form"), "Jiten distinguishes missing dictionary identities from incompatible forms");
        check(kanaValidated.Analyzer.EndsWith(JitenClient.FormParserVersion, StringComparison.Ordinal), "corrected Jiten validation has a version for cached-list migration");
        var ass = Path.Combine(sandbox, "media.ass"); File.WriteAllText(ass, "[Events]\nDialogue: 0,0:00:01.00,0:00:03.00,Default,,0,0,0,,{\\i1}山\\N食べる");
        check(LocalMediaText.Read(ass) == "山\n食べる", "ASS import excludes timing, style and presentation codes");
        var epub = Path.Combine(sandbox, "media.epub");
        using (var zip = System.IO.Compression.ZipFile.Open(epub, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Entry(string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(content); }
            Entry("META-INF/container.xml", "<container><rootfiles><rootfile full-path='OPS/book.opf'/></rootfiles></container>");
            Entry("OPS/book.opf", "<package><manifest><item id='b' href='b.xhtml'/><item id='a' href='a.xhtml'/></manifest><spine><itemref idref='a'/><itemref idref='b'/></spine></package>");
            Entry("OPS/a.xhtml", "<p>山</p>"); Entry("OPS/b.xhtml", "<p>食べる</p>");
        }
        check(LocalMediaText.Read(epub).Replace("\r", "") == "山\n食べる", "EPUB follows spine order rather than archive order");
        VerifyApi(check, reference).GetAwaiter().GetResult();
    }
    private static async Task VerifyApi(Action<bool, string> check, CoverageReference reference)
    {
        using var http = new HttpClient(new ResponseHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("get-media-decks")) return """{"data":[{"deckId":233,"originalTitle":"Fate/stay night","mediaType":7,"coverName":null}]}""";
            return """{"totalItems":2,"data":{"words":[{"wordId":1000001,"mainReading":{"text":"食[た]べる","readingIndex":0},"occurrences":3},{"wordId":1000006,"mainReading":{"text":"wrong form","readingIndex":0},"occurrences":1}]}}""";
        }));
        var client = new JitenClient(http);
        var title = (await client.Search("Fate/stay night", 7)).Single();
        var corpus = await client.Download(title, "Edition unknown", reference);
        check(corpus.Words[0].EntryId == "1000001" && corpus.Words[0].Reading == "たべる" && corpus.Words[1].EntryId is null, "online import parses segmented furigana and validates IDs/forms/readings against installed JMdict");
        using var broken = new HttpClient(new ResponseHandler(_ => """{"totalItems":2,"data":{"words":[]}}"""));
        try { await new JitenClient(broken).Download(title, "", reference); check(false, "incomplete API should fail"); }
        catch (InvalidDataException) { check(true, "incomplete online lists fail before cache replacement"); }
        using var overlapping = new HttpClient(new ResponseHandler(_ => """{"totalItems":2,"data":{"words":[{"wordId":1000001,"mainReading":{"text":"食べる","readingIndex":0},"occurrences":1}]}}"""));
        try { await new JitenClient(overlapping).Download(title, "", reference); check(false, "overlapping API should fail"); }
        catch (InvalidDataException) { check(true, "overlapping API pages fail instead of inflating coverage"); }
        using var artwork = new HttpClient(new ResponseHandler(request => request.RequestUri!.Host == "api.jiten.moe"
            ? """{"data":{"mainDeck":{"links":[{"url":"https://vndb.org/v11"}]}}}"""
            : """{"results":[{"image":{"url":"https://t.vndb.org/cv/71/89071.jpg","dims":[600,900]}}]}"""));
        check(await new JitenClient(artwork).OriginalVnCover(233) == "https://t.vndb.org/cv/71/89071.jpg",
            "artwork lookup follows a verified VNDB title link and uses the original image URL");
        using var absentArtwork = new HttpClient(new ResponseHandler(_ => """{"data":{"mainDeck":{"links":[]}}}"""));
        check(await new JitenClient(absentArtwork).OriginalVnCover(233) is null, "titles without a VNDB link keep the provider artwork fallback");
    }
    private sealed class ResponseHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
    }
}
