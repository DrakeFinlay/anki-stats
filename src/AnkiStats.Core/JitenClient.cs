using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnkiStats.Core;

// No account, Anki text, collection data or statistics are sent to this provider.
public sealed class JitenClient(HttpClient client)
{
    public const string FormParserVersion = "form parser v2";
    public const string Attribution = "Vocabulary derived by Jiten (https://jiten.moe), CC BY-SA 4.0; dictionary data from JMdict / EDRDG. Analysis is provider-supplied; edition completeness is unverified.";
    private const string Host = "https://api.jiten.moe/";
    public async Task<string?> OriginalVnCover(int deckId, CancellationToken token = default)
    {
        using var detail = await Get($"api/media-deck/{deckId}/detail", token);
        if (!detail.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("mainDeck", out var deck) || !deck.TryGetProperty("links", out var links)) return null;
        string? vnId = null;
        foreach (var link in links.EnumerateArray())
            if (link.TryGetProperty("url", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)
                && uri.Host == "vndb.org" && Regex.IsMatch(uri.AbsolutePath, @"^/v\d+$")) { vnId = uri.AbsolutePath[1..]; break; }
        if (vnId is null) return null;
        using var response = await client.PostAsJsonAsync("https://api.vndb.org/kana/vn",
            new { filters = new[] { "id", "=", vnId }, fields = "id,image.url,image.dims", results = 1 }, token);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        var results = json.RootElement.GetProperty("results");
        if (results.GetArrayLength() != 1 || !results[0].TryGetProperty("image", out var image) || image.ValueKind != JsonValueKind.Object) return null;
        var original = image.GetProperty("url").GetString();
        return Uri.TryCreate(original, UriKind.Absolute, out var imageUri) && imageUri.Scheme == "https"
            && imageUri.Host is "t.vndb.org" or "s.vndb.org" ? original : null;
    }
    public static MediaCorpus Revalidate(MediaCorpus corpus, CoverageReference reference)
    {
        var entries = reference.Entries.ToDictionary(e => e.Id);
        var words = corpus.Words.Select(w => ValidateWord(w.SourceEntryId ?? w.EntryId, w.Form, w.Occurrences, entries)).ToList();
        return corpus with { Words = words, DictionaryVersion = reference.Metadata.DictionaryVersion,
            Analyzer = "Jiten (provider analysis version unavailable); " + FormParserVersion };
    }
    private static MediaWord ValidateWord(string? id, string text, int occurrences, Dictionary<string, DictionaryEntry> entries)
    {
        // Jiten puts furigana on kanji runs. Keep intervening okurigana in the reading:
        // 差[さ]し込[こ]む -> さしこむ; the vocabulary-field parser accepts a different notation.
        var reading = Regex.Replace(text, @"([一-龯々〆ヵヶ]+)\[([^\[\]]+)\]", "$2");
        var headword = Regex.Replace(text, @"\[[^\[\]]+\]", "");
        var parsed = VocabularyExpression.Parse(new(0, headword, "", "", "Vocabulary", "Jiten", headword));
        var kanaReading = Regex.IsMatch(reading, @"^[ぁ-ゖァ-ヺー]+$") ? VocabularyMatcher.Key(reading) : "";
        var entry = id is not null ? entries.GetValueOrDefault(id) : null;
        var formValid = parsed.Error is null && entry is not null
            && (entry.Spellings.Contains(parsed.Word) || entry.Readings.Any(r => r.Text == parsed.Word));
        var readingValid = entry is not null && (!text.Contains('[') && kanaReading.Length == 0
            || kanaReading.Length > 0 && entry.Readings.Any(r => VocabularyMatcher.Key(r.Text) == kanaReading
                // Reading restrictions select kanji spellings, not kana-only headwords.
                && (VocabularyMatcher.Key(parsed.Word) == VocabularyMatcher.Key(r.Text)
                    || r.AppliesTo.Count == 0 || r.AppliesTo.Contains("*") || r.AppliesTo.Contains(parsed.Word))));
        var valid = formValid && readingValid;
        var status = valid ? "Validated" : entry is null ? $"Source ID {id} is absent from installed JMdict (possibly a name or provider-specific entry)"
            : !formValid ? $"Source form does not match installed JMdict entry {id}"
            : $"Source reading does not match installed JMdict entry {id}";
        if (occurrences <= 0) throw new InvalidDataException("Jiten returned an invalid occurrence count.");
        return new(valid ? id : null, text, kanaReading, occurrences, status, id);
    }
    private async Task<JsonDocument> Get(string path, CancellationToken token)
    {
        using var response = await client.GetAsync(Host + path, token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Jiten rate limit reached. Retry later; your existing media cache was retained.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }
    public async Task<List<MediaTitle>> Search(string query, int type, int offset = 0, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Enter a title to search.");
        if (type is not (1 or 4 or 5 or 7 or 8)) throw new ArgumentException("Choose anime, a visual novel, or a book.");
        using var json = await Get($"api/media-deck/get-media-decks?titleFilter={Uri.EscapeDataString(query.Trim())}&mediaType={type}&offset={offset}", token);
        return json.RootElement.GetProperty("data").EnumerateArray().Select(d => new MediaTitle(d.GetProperty("deckId").GetInt32(),
            d.GetProperty("originalTitle").GetString() ?? "Untitled", d.GetProperty("mediaType").GetInt32(),
            d.TryGetProperty("coverName", out var cover) ? cover.GetString() : null,
            $"https://jiten.moe/decks/{d.GetProperty("deckId").GetInt32()}")).ToList();
    }
    public async Task<MediaCorpus> Download(MediaTitle title, string edition, CoverageReference reference,
        IProgress<string>? progress = null, CancellationToken token = default)
    {
        var entries = reference.Entries.ToDictionary(e => e.Id);
        var words = new List<MediaWord>();
        var seen = new HashSet<string>();
        int? expected = null;
        for (var offset = 0; ;)
        {
            // Stay below the anonymous 120 requests/minute limit, including quick consecutive pages.
            await Task.Delay(600, token);
            using var json = await Get($"api/media-deck/{title.Id}/vocabulary?limit=200&offset={offset}&sortBy=chrono", token);
            var root = json.RootElement;
            var total = root.GetProperty("totalItems").GetInt32();
            if (total <= 0 || total > 500000) throw new InvalidDataException("Jiten returned no usable vocabulary, or an unsupported corpus size.");
            expected ??= total;
            if (total != expected) throw new InvalidDataException("The Jiten list changed during download. Retry to get a consistent snapshot.");
            var count = 0;
            foreach (var word in root.GetProperty("data").GetProperty("words").EnumerateArray())
            {
                count++;
                var id = word.GetProperty("wordId").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
                var form = word.GetProperty("mainReading");
                var key = id + ":" + form.GetProperty("readingIndex").GetInt32();
                if (!seen.Add(key)) throw new InvalidDataException("Jiten returned overlapping vocabulary pages. Retry the download.");
                var text = form.GetProperty("text").GetString() ?? "";
                // Validate both identity and form, rather than trusting an arbitrary numeric ID.
                var occurrences = word.GetProperty("occurrences").GetInt32();
                words.Add(ValidateWord(id, text, occurrences, entries));
            }
            if (count == 0) throw new InvalidDataException("Jiten returned an incomplete vocabulary list. The old cache was retained.");
            offset += count;
            progress?.Report($"Downloading {title.Title}: {offset:N0} / {total:N0} vocabulary forms…");
            if (offset >= total)
            {
                if (offset != total) throw new InvalidDataException("Jiten returned inconsistent vocabulary totals.");
                break;
            }
        }
        // Mismatched rows retain their source identity for diagnostics, but never contribute coverage.
        return new("jiten:" + title.Id, title.Title, title.MediaType, edition.Trim(), "Jiten vocabulary", title.SourceUrl,
            Attribution, MediaAnalysis.Hash(words), "Jiten (provider analysis version unavailable); " + FormParserVersion, reference.Metadata.DictionaryVersion,
            DateTimeOffset.UtcNow, title.CoverUrl, words);
    }
}
