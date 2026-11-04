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
        check(MediaAnalysis.Compare(corpus with { Words = [] }, coverage).Percent == "No eligible words", "empty mining pool has no invented percentage");
        var settings = new Settings { ProfileFolder = sandbox, Profiles = [new() { DeckRules = [new("Selected", true)] }] };
        var all = MediaAnalysis.CollectionSettings(settings, new(18, [new(1, "Excluded", 1)], []));
        check(all.Includes("Excluded") && all.Profiles.Count == 0 && settings.Profiles.Count == 1, "media all-deck import does not mutate profile settings");
        var store = new MediaLibraryStore(Path.Combine(sandbox, "media-store"));
        store.Save(corpus); store.Save(corpus with { State = "Completed" });
        var saved = store.Load().Single();
        check(saved.State == "Completed" && saved.Words.Count == words.Count && saved.ContentHash == corpus.ContentHash, "media corpus cache and status roundtrip");
        check(LocalMediaText.Clean("｜食べる《たべる》［＃注記］<ruby>山<rt>やま</rt></ruby>").Replace(" ", "") == "食べる山", "local cleaning removes ruby and editorial text");
        var srt = Path.Combine(sandbox, "media.srt"); File.WriteAllText(srt, "1\n00:00:01,000 --> 00:00:03,000\n<b>食べる</b>\n");
        check(LocalMediaText.Read(srt) == "食べる", "local subtitles exclude counters, timing and markup");
        var vtt = Path.Combine(sandbox, "media.vtt"); File.WriteAllText(vtt, "WEBVTT\n\nSTYLE\n::cue { color: red; }\n\ncue-id\n00:00:01.000 --> 00:00:03.000\n山\n");
        check(LocalMediaText.Read(vtt) == "山", "WebVTT import excludes headers, cue identifiers and styles");
        var local = LocalMediaText.Resolve("Local", 7, "Test", "食べた 山 開く", new("Synthetic analyzer", [
            new("食べた", "食べる", "食べる", "タベタ", "verb", 1), new("山", "山", "山", "ヤマ", "noun", 2), new("開く", "開く", "開く", "", "verb", 1)]), reference);
        check(local.Words[0].EntryId == "1000001" && local.Words[1].EntryId == "1000006", "local inflections resolve without comparing an inflected reading to a lemma reading");
        check(local.Words[2].EntryId is null && local.Words[2].Status.StartsWith("Ambiguous"), "local homographs never credit all possible dictionary entries");
        var furiganaReference = reference with { Entries = [.. reference.Entries,
            new("1291170", ["差し込む"], [new("さしこむ", ["*"])], "insert", true, [])] };
        var annotated = JitenClient.Revalidate(corpus with { Words = [new(null, "差[さ]し込[こ]む", "", 1, "Unverified", "1291170")] }, furiganaReference);
        check(annotated.Words[0].EntryId == "1291170" && annotated.Words[0].Reading == "さしこむ", "Jiten multi-segment furigana retains intervening okurigana");
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
    }
    private sealed class ResponseHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
    }
}
