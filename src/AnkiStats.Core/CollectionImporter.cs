using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AnkiStats.Core;

public sealed class CollectionImporter(string temporaryRoot)
{
    public T WithPrivateCopy<T>(string profileFolder, Func<SqliteConnection, T> read)
    {
        var sourcePath = Path.Combine(profileFolder, "collection.anki2");
        if (!File.Exists(sourcePath)) throw new InvalidDataException("No collection.anki2 was found. Choose an Anki profile folder in Settings.");
        Directory.CreateDirectory(temporaryRoot);
        var temp = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            // Open only an ordinary read handle on the source. SQLite never opens the source.
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                foreach (var suffix in new[] { "-wal", "-journal" })
                    if (File.Exists(sourcePath + suffix) && new FileInfo(sourcePath + suffix).Length > 0)
                        throw new IOException("The collection has an active journal. Open Anki and close it cleanly, then refresh again.");
                using var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(destination);
                destination.Flush(true);
            }
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            db.Open();
            // Metadata is scanned and ordered in memory. No text-index lookup depends on
            // this fallback's equivalence to Anki's Unicode case-folding implementation.
            db.CreateCollation("unicase", (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a, b));
            using (var cmd = db.CreateCommand()) { cmd.CommandText = "PRAGMA query_only=ON"; cmd.ExecuteNonQuery(); }
            return read(db);
        }
        catch (UnauthorizedAccessException e) { throw new IOException("Access to the profile was denied. Choose a readable profile folder.", e); }
        catch (SqliteException e) { throw new InvalidDataException("The collection could not be read. It may be damaged or use an unsupported Anki schema. The previous results are safe.", e); }
        catch (IOException e) when (!e.Message.StartsWith("The collection has", StringComparison.Ordinal))
        { throw new IOException("The collection could not be copied safely. Close Anki completely, check folder access and available disk space, then refresh.", e); }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                if (File.Exists(temp + suffix)) File.Delete(temp + suffix);
        }
    }

    public CollectionMetadata Discover(string profileFolder) => WithPrivateCopy(profileFolder, ReadMetadata);

    public static CollectionMetadata ReadMetadata(SqliteConnection db)
    {
        using var schema = db.CreateCommand();
        schema.CommandText = "SELECT ver FROM col";
        var version = Convert.ToInt32(schema.ExecuteScalar());
        if (version is not (11 or 18)) throw new InvalidDataException($"Collection schema {version} is not supported. This build supports schemas 11 and 18; update the app before refreshing this collection.");
        var decks = new List<Deck>();
        var types = new List<NoteType>();
        if (version == 18)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT id,name FROM decks NOT INDEXED";
            using (var r = cmd.ExecuteReader()) while (r.Read()) decks.Add(new(r.GetInt64(0), r.GetString(1).Replace("\u001f", "::"), 0));
            cmd.CommandText = "SELECT id,name FROM notetypes NOT INDEXED";
            using (var r = cmd.ExecuteReader()) while (r.Read()) types.Add(new(r.GetInt64(0), r.GetString(1), []));
            // Scan the primary key, avoiding Anki's text-name index.
            cmd.CommandText = "SELECT ntid,ord,name FROM fields ORDER BY ntid,ord";
            using (var r = cmd.ExecuteReader()) while (r.Read())
            {
                var type = types.Find(t => t.Id == r.GetInt64(0));
                if (type is null) continue;
                var ordinal = r.GetInt32(1);
                while (type.Fields.Count <= ordinal) type.Fields.Add("");
                type.Fields[ordinal] = r.GetString(2);
            }
        }
        else
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT decks,models FROM col";
            using var r = cmd.ExecuteReader(); r.Read();
            using var dj = JsonDocument.Parse(r.GetString(0));
            foreach (var item in dj.RootElement.EnumerateObject()) { var d = item.Value; decks.Add(new(d.GetProperty("id").GetInt64(), d.GetProperty("name").GetString()!, 0)); }
            using var mj = JsonDocument.Parse(r.GetString(1));
            foreach (var item in mj.RootElement.EnumerateObject())
            {
                var m = item.Value;
                types.Add(new(m.GetProperty("id").GetInt64(), m.GetProperty("name").GetString()!, m.GetProperty("flds").EnumerateArray().OrderBy(f => f.GetProperty("ord").GetInt32()).Select(f => f.GetProperty("name").GetString()!).ToList()));
            }
        }
        using var counts = db.CreateCommand();
        counts.CommandText = "SELECT CASE WHEN odid != 0 THEN odid ELSE did END,COUNT(*) FROM cards GROUP BY 1";
        var countMap = new Dictionary<long, int>();
        using (var r = counts.ExecuteReader()) while (r.Read()) countMap[r.GetInt64(0)] = r.GetInt32(1);
        return new(version, decks.Select(d => d with { Cards = countMap.GetValueOrDefault(d.Id) }).OrderBy(d => d.Name, StringComparer.Ordinal).ToList(), types.OrderBy(t => t.Name, StringComparer.Ordinal).ToList());
    }

    public ImportedCollection Import(Settings settings, IProgress<string>? progress = null) => WithPrivateCopy(settings.ProfileFolder, db =>
    {
        progress?.Report("Reading deck and field definitions…");
        var metadata = ReadMetadata(db);
        var mappings = settings.Mappings.ToDictionary(m => m.NoteTypeId);
        foreach (var m in settings.Mappings.Where(m => m.Category != "Ignored"))
        {
            var type = metadata.NoteTypes.Find(t => t.Id == m.NoteTypeId) ?? throw new InvalidDataException("A mapped note type no longer exists. Inspect the profile again in Settings.");
            if (m.Expression < 0 || m.Expression >= type.Fields.Count || m.Reading < -1 || m.Reading >= type.Fields.Count || m.Meaning < -1 || m.Meaning >= type.Fields.Count)
                throw new InvalidDataException("A field mapping is no longer valid. Inspect the profile and update its mappings.");
            foreach (var (index, name) in new[] { (m.Expression, m.ExpressionName), (m.Reading, m.ReadingName), (m.Meaning, m.MeaningName) })
                if (index >= 0 && name.Length > 0 && type.Fields[index] != name)
                    throw new InvalidDataException("A mapped field was renamed or reordered. Inspect the profile, update its mappings, and preview again.");
        }
        var data = new ImportedCollection { Profiles = DeckProfiles.Copy(settings.Profiles), Metadata = metadata, CollectionKey = settings.CollectionKey, Signature = settings.Signature, TimeZoneId = settings.TimeZoneId,
            Scope = settings.Profiles.Count > 0 ? "Saved deck profiles: " + string.Join(", ", settings.Profiles.Select(p => p.Name))
                : string.Join(", ", settings.DeckRules.Select(r => (r.Include ? "+ " : "− ") + r.Name)), SourceProfileFolder = settings.ProfileFolder,
            IsSynthetic = File.Exists(Path.Combine(settings.ProfileFolder, ".ankistats-test-data")), RefreshedAt = DateTimeOffset.UtcNow };
        var deckNames = metadata.Decks.Where(d => settings.Includes(d.Name)).ToDictionary(d => d.Id, d => d.Name);
        var selectedNotes = new HashSet<long>();
        var candidateCards = new List<CardRecord>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,nid,did,odid,queue,type,lapses FROM cards";
        using (var r = cmd.ExecuteReader()) while (r.Read())
        {
            var deckId = r.GetInt64(3) != 0 ? r.GetInt64(3) : r.GetInt64(2);
            if (!deckNames.TryGetValue(deckId, out var name)) continue;
            selectedNotes.Add(r.GetInt64(1));
            candidateCards.Add(new(r.GetInt64(0), r.GetInt64(1), name, r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)));
        }
        progress?.Report("Extracting selected vocabulary…");
        cmd.CommandText = "SELECT id,mid,flds FROM notes";
        using (var r = cmd.ExecuteReader()) while (r.Read())
        {
            var id = r.GetInt64(0); var mid = r.GetInt64(1);
            if (!selectedNotes.Contains(id) || !mappings.TryGetValue(mid, out var m) || m.Category == "Ignored") continue;
            var fields = r.GetString(2).Split('\u001f');
            string Field(int index, bool expression = false) => index < 0 || index >= fields.Length ? "" : TextNormalizer.Clean(fields[index], expression && m.StripBracketReadings);
            data.Notes.Add(new(id, Field(m.Expression, true), Field(m.Reading), Field(m.Meaning), m.Category, metadata.NoteTypes.First(t => t.Id == mid).Name, Field(m.Expression)));
        }
        var noteIds = data.Notes.Select(n => n.Id).ToHashSet();
        data.Cards = candidateCards.Where(c => noteIds.Contains(c.NoteId)).ToList();
        var cardIds = data.Cards.Select(c => c.Id).ToHashSet();
        progress?.Report("Reading review history…");
        cmd.CommandText = "SELECT id,cid,ease,time,type FROM revlog";
        using (var r = cmd.ExecuteReader()) while (r.Read())
        {
            if (!cardIds.Contains(r.GetInt64(1))) continue;
            var kind = r.GetInt32(4);
            if (kind is < 0 or > 5) data.UnknownReviewKinds++;
            data.Reviews.Add(new(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.GetInt32(3), kind));
        }
        cmd.CommandText = "SELECT COUNT(*) FROM revlog r LEFT JOIN cards c ON c.id=r.cid WHERE c.id IS NULL";
        data.UnresolvedReviews = Convert.ToInt32(cmd.ExecuteScalar());
        return data;
    });
}
