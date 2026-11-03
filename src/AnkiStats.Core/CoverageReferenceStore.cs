using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace AnkiStats.Core;

/// <summary>Versioned replaceable reference data, separate from personal matching decisions.</summary>
public sealed class CoverageReferenceStore(string root)
{
    public const string DictionaryVersion = "3.6.2+20261026175559";
    public const string JlptRevision = "0d1d3410bec90bd4098a7c72de820543cb4f707c";
    public const string Attribution = "Dictionary data © EDRDG, via jmdict-simplified (https://www.edrdg.org/jmdict/j_jmdict.html; https://github.com/scriptin/jmdict-simplified). "
        + "JLPT data © OpenJLPT contributors, derived from Jonathan Waller’s lists and JMdict (https://github.com/evanclan/OpenJLPT; https://www.tanos.co.uk/jlpt/). "
        + "Reference data and derived word lists: CC BY-SA 4.0 (https://creativecommons.org/licenses/by-sa/4.0/). "
        + "English glosses are condensed; example sentences are omitted. Corrected OpenJLPT labels can differ from Jisho; JLPT lists are unofficial.";
    private static readonly string[] Hashes = [
        "C25F1594482FCB55D02F66609584367A04136AF9E4CFAA15C207EB41500BC0D3",
        "09325EB676EC26044E9019AFB5DDD460A394A0D8CB51B0B34D30D03124C131FF",
        "4C7053D1B61F71CA4004252CAFDF89439C65531900E7BAB6C53078802ACF3C5B",
        "17CA6BCBBA9D212C3C9D38C38CECC2ADEF0FD8718CB7157BB48DD9D3A1724948",
        "B38512A6DC1E06A3615F377B3E58184C4A85B6F769C2E3F58F2A4EF0E1810927",
        "CBB221C9A3A92108FCD02FB088D9AFACC70BC8C96570D5730425DE0A49DAD5F3"];
    private string PathName => Path.Combine(root, "coverage-reference-v1.json.gz");
    public CoverageReference? Load()
    {
        if (!File.Exists(PathName)) return null;
        using var file = File.OpenRead(PathName);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var reference = JsonSerializer.Deserialize<CoverageReference>(gzip) ?? throw new InvalidDataException("Empty vocabulary reference bundle. Download it again.");
        Validate(reference);
        return reference;
    }
    public static void Validate(CoverageReference reference)
    {
        if (reference.Entries.Count == 0 || reference.Entries.Select(e => e.Id).Distinct().Count() != reference.Entries.Count
            || reference.Entries.Any(e => !long.TryParse(e.Id, out _) || e.Readings.Count == 0 || e.Readings.Any(r => string.IsNullOrWhiteSpace(r.Text))
                || e.Levels.Any(l => l is not ("N1" or "N2" or "N3" or "N4" or "N5"))))
            throw new InvalidDataException("Invalid vocabulary reference data. The previous reference is unchanged.");
    }
    public void Save(CoverageReference reference)
    {
        Validate(reference);
        Directory.CreateDirectory(root);
        var temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary))
            using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize)) JsonSerializer.Serialize(gzip, reference);
            File.Move(temporary, PathName, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<CoverageReference> Download(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        // Fixed public URLs only. No collection data, vocabulary, IDs, or personal paths are transmitted.
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AnkiStats/1B");
        progress?.Report("Downloading the versioned Japanese reference dictionary…");
        var encoded = Uri.EscapeDataString(DictionaryVersion);
        var zipBytes = await client.GetByteArrayAsync($"https://github.com/scriptin/jmdict-simplified/releases/download/{encoded}/jmdict-eng-{encoded}.json.zip", cancellationToken);
        Verify(zipBytes, 0);
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var member = zip.Entries.Single(e => e.Name == "jmdict-eng-3.6.2.json");
        using var dictionary = member.Open();
        var lists = new List<byte[]>();
        for (var n = 1; n <= 5; n++)
        {
            progress?.Report($"Downloading OpenJLPT N{n}…");
            var bytes = await client.GetByteArrayAsync($"https://raw.githubusercontent.com/evanclan/OpenJLPT/{JlptRevision}/data/json/vocab/n{n}.json", cancellationToken);
            Verify(bytes, n); lists.Add(bytes);
        }
        progress?.Report("Indexing dictionary entries and vocabulary classifications locally…");
        var reference = Build(dictionary, lists);
        Save(reference);
        return reference;
    }
    private static void Verify(byte[] bytes, int index)
    {
        if (Convert.ToHexString(SHA256.HashData(bytes)) != Hashes[index])
            throw new InvalidDataException("The downloaded reference checksum differs from the pinned version. The previous reference is unchanged.");
    }
    public static CoverageReference Build(Stream dictionary, IEnumerable<byte[]> jlptLists)
    {
        using var json = JsonDocument.Parse(dictionary);
        var entries = new List<DictionaryEntry>();
        foreach (var word in json.RootElement.GetProperty("words").EnumerateArray())
        {
            var written = word.GetProperty("kanji").EnumerateArray().ToList();
            var kana = word.GetProperty("kana").EnumerateArray().ToList();
            var glosses = word.GetProperty("sense").EnumerateArray().SelectMany(s => s.GetProperty("gloss").EnumerateArray())
                .Where(g => g.GetProperty("lang").GetString() == "eng").Select(g => g.GetProperty("text").GetString()!).Distinct().ToList();
            var mainSense = word.GetProperty("sense").EnumerateArray().FirstOrDefault();
            var usuallyKana = mainSense.ValueKind == JsonValueKind.Object && mainSense.TryGetProperty("misc", out var misc) && misc.EnumerateArray().Any(m => m.GetString() == "uk");
            var orderedKana = kana.OrderByDescending(k => k.GetProperty("common").GetBoolean()).ToList();
            var commonWritten = written.Where(k => k.GetProperty("common").GetBoolean()).ToList();
            var useKana = usuallyKana || written.Count == 0 || (commonWritten.Count == 0 && kana.Any(k => k.GetProperty("common").GetBoolean()));
            string writtenForm; string readingForm;
            if (useKana) writtenForm = readingForm = orderedKana[0].GetProperty("text").GetString()!;
            else
            {
                writtenForm = (commonWritten.FirstOrDefault().ValueKind == JsonValueKind.Object ? commonWritten[0] : written[0]).GetProperty("text").GetString()!;
                var compatible = orderedKana.FirstOrDefault(k => k.GetProperty("appliesToKanji").EnumerateArray().Any(a => a.GetString() is "*" || a.GetString() == writtenForm));
                if (compatible.ValueKind == JsonValueKind.Object) readingForm = compatible.GetProperty("text").GetString()!;
                else writtenForm = readingForm = orderedKana[0].GetProperty("text").GetString()!;
            }
            entries.Add(new(word.GetProperty("id").GetString()!, written.Select(k => k.GetProperty("text").GetString()!).ToList(),
                kana.Select(k => new DictionaryReading(k.GetProperty("text").GetString()!, k.GetProperty("appliesToKanji").EnumerateArray().Select(a => a.GetString()!).ToList())).ToList(),
                string.Join("; ", glosses), written.Concat(kana).Any(k => k.GetProperty("common").GetBoolean()), [], new(writtenForm, readingForm)));
        }
        var byId = entries.ToDictionary(e => e.Id);
        var unlinked = 0;
        foreach (var bytes in jlptLists)
        {
            using var list = JsonDocument.Parse(bytes);
            foreach (var item in list.RootElement.EnumerateArray())
            {
                // Do not guess links for the source's unresolved rows: disclose them beside the denominator.
                if (!item.TryGetProperty("jmdict_id", out var id) || !byId.TryGetValue(id.ToString(), out var entry)) { unlinked++; continue; }
                var level = item.GetProperty("level").GetString()!;
                if (!entry.Levels.Contains(level)) entry.Levels.Add(level);
            }
        }
        return new(new(DictionaryVersion, json.RootElement.GetProperty("dictDate").GetString()!, JlptRevision, unlinked, Attribution, FormSelectionVersion: 1), entries);
    }
}
