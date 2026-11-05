using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AnkiStats.Core;

public sealed record MediaTitle(int Id, string Title, int MediaType, string? CoverUrl, string SourceUrl)
{
    public string Kind => MediaType switch { 7 => "Visual novel", 1 => "Anime", _ => "Book" };
}
public sealed record MediaWord(string? EntryId, string Form, string Reading, int Occurrences, string Status, string? SourceEntryId = null, string Sentence = "");
public sealed record MediaCorpus(string Id, string Title, int MediaType, string Edition, string Source,
    string SourceUrl, string Attribution, string ContentHash, string Analyzer, string DictionaryVersion,
    DateTimeOffset RetrievedAt, string? CoverUrl, List<MediaWord> Words, string State = "Currently reading", int Version = 1);
public sealed record MediaWordRow(string EntryId, string Form, string Reading, string Meaning, string Lists,
    int Occurrences, string Status, bool Eligible);
public sealed record MediaSummary(List<MediaWordRow> Words, int Eligible, int Present, int NeedsReview, int Other, int Unresolved)
{
    public int Remaining => Eligible - Present - NeedsReview;
    public string Percent => Eligible == 0 ? "No eligible words" : $"{100d * Present / Eligible:F1}% mined";
}

public static class MediaShelves
{
    public const string Reading = "Currently reading", Planned = "Planned", Completed = "Completed";
    public static IReadOnlyList<string> All { get; } = [Reading, Planned, Completed];
    public static MediaCorpus Move(MediaCorpus corpus, string shelf, IEnumerable<MediaCorpus> library)
    {
        if (!All.Contains(shelf)) throw new ArgumentException("Choose a media shelf.");
        if (shelf == Reading && library.Count(c => c.Id != corpus.Id && c.State == Reading) >= 2)
            throw new InvalidOperationException("Currently reading holds up to two titles. Move one to Planned or Completed first.");
        return corpus with { State = shelf };
    }
}

public static class MediaAnalysis
{
    // Revalidate on every comparison: a reference update can remove or change identities.
    public static MediaSummary Compare(MediaCorpus corpus, CoverageAnalysis coverage)
    {
        var entries = coverage.Reference.Entries.ToDictionary(e => e.Id);
        var pending = coverage.Matches.SelectMany(m => m.ReviewEntryIds).ToHashSet();
        var rows = new List<MediaWordRow>();
        var unresolved = corpus.Words.Count(w => MappingReview.IsUnresolved(w, entries));
        foreach (var group in corpus.Words.Where(w => w.EntryId is not null && entries.ContainsKey(w.EntryId)).GroupBy(w => w.EntryId!))
        {
            var entry = entries[group.Key];
            var eligible = entry.Common || entry.Levels.Count > 0;
            var status = coverage.Database.Contains(entry.Id) ? "In Anki" : pending.Contains(entry.Id) ? "Needs review" : eligible ? "Remaining" : "Not in Anki";
            rows.Add(new(entry.Id, entry.StudyForm?.Written ?? entry.Spellings.FirstOrDefault() ?? entry.Readings[0].Text,
                entry.StudyForm?.Reading ?? entry.Readings[0].Text, entry.Meaning,
                string.Join(" / ", (entry.Common ? new[] { "Common" } : []).Concat(entry.Levels)),
                group.Sum(w => w.Occurrences), status, eligible));
        }
        return new(rows.OrderByDescending(w => w.Eligible).ThenByDescending(w => w.Occurrences).ToList(),
            rows.Count(w => w.Eligible), rows.Count(w => w.Eligible && w.Status == "In Anki"),
            rows.Count(w => w.Eligible && w.Status == "Needs review"), rows.Count(w => !w.Eligible), unresolved);
    }

    public static string Hash(IEnumerable<MediaWord> words) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(words)));
    public static Settings CollectionSettings(Settings current, CollectionMetadata metadata)
    {
        var all = DeckProfiles.Copy(current);
        all.Profiles = []; all.ActiveProfileId = "";
        all.DeckRules = metadata.Decks.Select(d => new DeckRule(d.Name, true)).ToList();
        return all;
    }
}

// App data only. Each title is committed atomically, and failed imports preserve the previous corpus.
public sealed class MediaLibraryStore(string root)
{
    private string Folder => Path.Combine(root, "media");
    private string FileFor(string id) => Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))) + ".json.gz");
    public string ArtworkPath(string id)
    {
        var directory = Path.Combine(Folder, "covers");
        var stable = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))) + ".jpg");
        if (File.Exists(stable)) return stable;
        // Older versions hashed a serialized MediaWord. Keep their artwork reachable
        // when optional word fields (such as Sentence) are added to the corpus schema.
        var legacy = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] {
            new { EntryId = id, Form = "", Reading = "", Occurrences = 0, Status = "", SourceEntryId = (string?)null } })));
        var previous = Path.Combine(directory, legacy + ".jpg");
        return File.Exists(previous) ? previous : stable;
    }
    public List<MediaCorpus> Load()
    {
        if (!Directory.Exists(Folder)) return [];
        return Directory.EnumerateFiles(Folder, "*.json.gz").Select(file =>
        {
            using var stream = File.OpenRead(file);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            var corpus = JsonSerializer.Deserialize<MediaCorpus>(gzip) ?? throw new InvalidDataException("Empty media cache.");
            if (corpus.Version != 1) throw new InvalidDataException("Unsupported media cache version.");
            return corpus;
        }).OrderBy(c => c.Title, StringComparer.Ordinal).ToList();
    }
    public void Save(MediaCorpus corpus)
    {
        Directory.CreateDirectory(Folder);
        var path = FileFor(corpus.Id); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
            using (var gzip = new GZipStream(stream, CompressionLevel.Optimal)) JsonSerializer.Serialize(gzip, corpus);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void SaveText(MediaCorpus corpus, string text)
    {
        Directory.CreateDirectory(Folder);
        var path = FileFor(corpus.Id) + ".text.gz";
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
            using (var gzip = new GZipStream(stream, CompressionLevel.Optimal))
            using (var writer = new StreamWriter(gzip, new System.Text.UTF8Encoding(false))) writer.Write(text);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string? LoadText(MediaCorpus corpus)
    {
        var path = FileFor(corpus.Id) + ".text.gz";
        if (!File.Exists(path)) return null;
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }
}
