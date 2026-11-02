using AnkiStats.Core;
using Microsoft.Data.Sqlite;

internal static class CoverageUiFixture
{
    public static void Create(string root)
    {
        var profile = Path.Combine(root, "synthetic-profile"); Directory.CreateDirectory(profile);
        var path = Path.Combine(profile, "collection.anki2");
        if (File.Exists(path)) throw new IOException("Use an empty coverage fixture folder.");
        File.WriteAllText(Path.Combine(profile, ".ankistats-test-data"), "Synthetic coverage fixture.");
        var source = CoverageChecks.Collection();
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE col(ver INTEGER); INSERT INTO col VALUES(18);
                CREATE TABLE decks(id INTEGER PRIMARY KEY,name TEXT);
                CREATE TABLE notetypes(id INTEGER PRIMARY KEY,name TEXT); INSERT INTO notetypes VALUES(1,'Vocabulary'),(2,'Kanji');
                CREATE TABLE fields(ntid INTEGER,ord INTEGER,name TEXT,PRIMARY KEY(ntid,ord));
                INSERT INTO fields VALUES(1,0,'Expression'),(1,1,'Reading'),(1,2,'Meaning'),(2,0,'Character');
                CREATE TABLE notes(id INTEGER PRIMARY KEY,mid INTEGER,flds TEXT);
                CREATE TABLE cards(id INTEGER PRIMARY KEY,nid INTEGER,did INTEGER,odid INTEGER,queue INTEGER,type INTEGER,lapses INTEGER);
                CREATE TABLE revlog(id INTEGER PRIMARY KEY,cid INTEGER,ease INTEGER,time INTEGER,type INTEGER);
                """;
            cmd.ExecuteNonQuery();
            var decks = source.Cards.Select(c => c.Deck).Distinct().Select((name, i) => (Name: name, Id: i + 1)).ToList();
            foreach (var deck in decks)
            {
                cmd.CommandText = "INSERT INTO decks VALUES($id,$name)"; cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$id", deck.Id); cmd.Parameters.AddWithValue("$name", deck.Name); cmd.ExecuteNonQuery();
            }
            foreach (var note in source.Notes)
            {
                cmd.CommandText = "INSERT INTO notes VALUES($id,$type,$fields)"; cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$id", note.Id); cmd.Parameters.AddWithValue("$type", note.Category == "Kanji" ? 2 : 1);
                cmd.Parameters.AddWithValue("$fields", string.Join('\u001f', note.Expression, note.Reading, note.Meaning)); cmd.ExecuteNonQuery();
            }
            foreach (var card in source.Cards)
            {
                cmd.CommandText = "INSERT INTO cards VALUES($id,$note,$deck,0,$queue,$type,0)"; cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$id", card.Id); cmd.Parameters.AddWithValue("$note", card.NoteId); cmd.Parameters.AddWithValue("$deck", decks.Single(d => d.Name == card.Deck).Id);
                cmd.Parameters.AddWithValue("$queue", card.Queue); cmd.Parameters.AddWithValue("$type", card.Type); cmd.ExecuteNonQuery();
            }
        }
        var settings = new Settings { ProfileFolder = profile, DeckRules = [new("日本語", true)], Mappings =
            [new() { NoteTypeId = 1, Category = "Vocabulary", Expression = 0, Reading = 1, Meaning = 2 }, new() { NoteTypeId = 2, Category = "Kanji", Expression = 0 }] };
        var store = new LocalStore(root);
        var importer = new CollectionImporter(store.TemporaryRoot);
        DeckProfiles.Ensure(settings, importer.Discover(profile));
        store.SaveCache(importer.Import(settings), settings);
        new CoverageReferenceStore(Path.Combine(root, "reference")).Save(CoverageChecks.Reference());
    }
}
