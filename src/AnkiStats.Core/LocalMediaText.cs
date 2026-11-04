using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AnkiStats.Core;

public sealed record MediaToken(string Surface, string Lemma, string Normalized, string Reading, string Pos, int Count);
public sealed record MorphologyOutput(string Analyzer, List<MediaToken> Tokens);

public static class LocalMediaText
{
    private const int MaxCharacters = 20_000_000;
    public static string Read(string path)
    {
        if (new FileInfo(path).Length > 100_000_000) throw new InvalidDataException("Choose a media file smaller than 100 MB.");
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".epub") return ReadEpub(path);
        if (extension is not (".txt" or ".srt" or ".ass" or ".vtt")) throw new InvalidDataException("Supported local formats: TXT, SRT, ASS, VTT and DRM-free EPUB.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text;
        try { text = File.ReadAllText(path, new UTF8Encoding(false, true)); }
        catch (DecoderFallbackException) { text = File.ReadAllText(path, Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)); }
        if (extension == ".ass")
            text = string.Join('\n', text.Split('\n').Where(l => l.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Split(',', 10)).Where(p => p.Length == 10).Select(p => Regex.Replace(p[9], @"\{[^}]*\}", "").Replace(@"\N", "\n").Replace(@"\n", "\n").Replace(@"\h", " ")));
        else if (extension is ".srt" or ".vtt")
        {
            var captions = new List<string>(); var inCue = false;
            foreach (var line in text.Split('\n'))
            {
                if (line.Contains("-->")) { inCue = true; continue; }
                if (string.IsNullOrWhiteSpace(line)) { inCue = false; continue; }
                if (inCue) captions.Add(line);
            }
            text = string.Join('\n', captions);
        }
        return Clean(text);
    }
    public static string Clean(string text)
    {
        // Ruby annotations, Aozora editorial annotations and presentation tags are not vocabulary.
        text = Regex.Replace(text, @"<(head|script|style)\b[^>]*>.*?</\1>|<!--.*?-->", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(rt|rp)\b[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = Regex.Replace(text, @"《[^》]*》|［＃[^］]*］", "").Replace("｜", "");
        text = WebUtility.HtmlDecode(text).Trim('\uFEFF', ' ', '\r', '\n', '\t');
        if (text.Length == 0 || text.Length > MaxCharacters) throw new InvalidDataException("The file has no usable text or exceeds the 20-million-character limit.");
        return text;
    }
    private static string ReadEpub(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        string ReadEntry(string name)
        {
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException("EPUB references a missing file.");
            if (entry.Length > MaxCharacters * 4L) throw new InvalidDataException("EPUB section is too large.");
            using var stream = entry.Open(); using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var container = XDocument.Parse(ReadEntry("META-INF/container.xml"));
        var packageName = container.Descendants().First(e => e.Name.LocalName == "rootfile").Attribute("full-path")?.Value
            ?? throw new InvalidDataException("EPUB has no package.");
        var package = XDocument.Parse(ReadEntry(packageName));
        var manifest = package.Descendants().Where(e => e.Name.LocalName == "item").ToDictionary(e => (string)e.Attribute("id")!, e => (string)e.Attribute("href")!);
        var packageUri = new Uri("https://epub.invalid/" + packageName);
        var text = new StringBuilder();
        foreach (var section in package.Descendants().Where(e => e.Name.LocalName == "itemref"))
        {
            var id = (string?)section.Attribute("idref") ?? "";
            if (!manifest.TryGetValue(id, out var href)) throw new InvalidDataException("EPUB spine references a missing section.");
            var uri = new Uri(packageUri, href);
            if (uri.Host != "epub.invalid") throw new InvalidDataException("Remote EPUB sections are unsupported.");
            text.AppendLine(Clean(ReadEntry(Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')))));
            if (text.Length > MaxCharacters) throw new InvalidDataException("EPUB text is too large.");
        }
        return Clean(text.ToString());
    }

    public static async Task<MorphologyOutput> Analyze(string text, string python, CancellationToken token = default)
    {
        using var resource = typeof(LocalMediaText).Assembly.GetManifestResourceStream("AnkiStats.Core.media_tokenize.py")!;
        using var reader = new StreamReader(resource);
        var script = await reader.ReadToEndAsync(token);
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("-I"); start.ArgumentList.Add("-X"); start.ArgumentList.Add("utf8"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start) ?? throw new IOException("Could not start Python. Configure a Python executable with SudachiPy and SudachiDict-core installed.");
        using var cancellation = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(token);
        var errors = process.StandardError.ReadToEndAsync(token);
        await process.StandardInput.WriteAsync(text.AsMemory(), token); process.StandardInput.Close();
        await process.WaitForExitAsync(token);
        var error = await errors;
        if (process.ExitCode != 0) throw new InvalidDataException("Local morphology failed. Install SudachiPy and SudachiDict-core into the selected Python environment. " + error);
        return JsonSerializer.Deserialize<MorphologyOutput>(await output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The local analyzer returned no output.");
    }

    public static MediaCorpus Resolve(string title, int type, string edition, string text, MorphologyOutput analysis, CoverageReference reference)
    {
        var index = new Dictionary<string, List<DictionaryEntry>>(StringComparer.Ordinal);
        foreach (var entry in reference.Entries)
            foreach (var form in entry.Spellings.Concat(entry.Readings.Select(r => r.Text)).Distinct())
            {
                if (!index.TryGetValue(form, out var list)) index[form] = list = [];
                list.Add(entry);
            }
        var words = new List<MediaWord>();
        foreach (var token in analysis.Tokens)
        {
            var candidates = new List<DictionaryEntry>();
            foreach (var form in new[] { token.Lemma, token.Normalized, token.Surface }.Distinct())
            {
                if (!index.TryGetValue(form, out var matches)) continue;
                // Inflected surface readings cannot safely constrain a dictionary-form reading.
                candidates = matches.Where(e => token.Lemma != token.Surface || token.Reading.Length == 0 ||
                    e.Readings.Any(r => Kana(r.Text) == Kana(token.Reading) &&
                        (r.AppliesTo.Count == 0 || r.AppliesTo.Contains("*") || r.AppliesTo.Contains(form)))).DistinctBy(e => e.Id).ToList();
                if (candidates.Count > 0) break;
            }
            words.Add(new(candidates.Count == 1 ? candidates[0].Id : null, token.Lemma, token.Reading, token.Count,
                candidates.Count == 1 ? "Resolved" : candidates.Count > 1 ? "Ambiguous: " + string.Join(", ", candidates.Select(c => c.Id)) : "Unmatched"));
        }
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return new("local:" + Guid.NewGuid().ToString("N"), title.Trim(), type, edition.Trim(), "Local text", "", "User-selected local text; no text uploaded.",
            hash, analysis.Analyzer, reference.Metadata.DictionaryVersion, DateTimeOffset.UtcNow, null, words);
    }
    private static string Kana(string text) => new(text.Select(c => c is >= '\u30a1' and <= '\u30f6' ? (char)(c - 0x60) : c).ToArray());
}
