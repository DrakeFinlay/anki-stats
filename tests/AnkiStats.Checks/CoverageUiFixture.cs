using AnkiStats.Core;
using Microsoft.Data.Sqlite;

internal static class CoverageUiFixture
{
    public static void Create(string root, bool mappings = false, bool corrections = false)
    {
        var profile = Path.Combine(root, "synthetic-profile"); Directory.CreateDirectory(profile);
        var path = Path.Combine(profile, "collection.anki2");
        if (File.Exists(path)) throw new IOException("Use an empty coverage fixture folder.");
        File.WriteAllText(Path.Combine(profile, ".ankistats-test-data"), "Synthetic coverage fixture.");
        var source = mappings ? MappingChecks.Collection() : CoverageChecks.Collection();
        if (corrections) source.Notes[0] = new(1, "影響力[えいきょうちょく]", "", "my original meaning", "Vocabulary", "Word");
        var expressions = new[] { "クラス", "万年筆[まんえんひつ]", "女の子[おんあのこ]", "昨夜[ゆうべ・さくや]", "SNS[えす・えぬ・えす]" };
        var meanings = new[] { "class", "fountain pen", "girl", "last night", "social networking service" };
        for (var i = 0; i < expressions.Length; i++)
        {
            source.Notes.Add(new(100 + i, expressions[i], "", meanings[i], "Vocabulary", "Word"));
            source.Cards.Add(new(1100 + i, 100 + i, "日本語::永遠", 2, 2, 0));
        }
        source.Notes.Add(new(105, "はい", "", "yes", "Vocabulary", "Word"));
        source.Cards.Add(new(1105, 105, "日本語::永遠", 2, 2, 0));
        if (mappings)
        {
            source.Notes.AddRange([new(201, "English-only fixture", "", "foreign deck", "Vocabulary", "Word"),
                new(202, "文法-only fixture", "", "foreign grammar deck", "Grammar", "Grammar")]);
            source.Cards.AddRange([new(2011, 201, "English", 2, 2, 0), new(2021, 202, "文法", 2, 2, 0)]);
        }
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE col(ver INTEGER); INSERT INTO col VALUES(18);
                CREATE TABLE decks(id INTEGER PRIMARY KEY,name TEXT);
                CREATE TABLE notetypes(id INTEGER PRIMARY KEY,name TEXT); INSERT INTO notetypes VALUES(1,'Vocabulary'),(2,'Kanji'),(9,'Grammar');
                CREATE TABLE fields(ntid INTEGER,ord INTEGER,name TEXT,PRIMARY KEY(ntid,ord));
                INSERT INTO fields VALUES(1,0,'Expression'),(1,1,'Reading'),(1,2,'Meaning'),(2,0,'Character'),(9,0,'Front'),(9,1,'Reading'),(9,2,'English');
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
                cmd.Parameters.AddWithValue("$id", note.Id); cmd.Parameters.AddWithValue("$type", note.Category == "Kanji" ? 2 : note.Category == "Grammar" ? 9 : 1);
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
            [new() { NoteTypeId = 1, Category = "Vocabulary", Expression = 0, Reading = 1, Meaning = 2 }, new() { NoteTypeId = 2, Category = "Kanji", Expression = 0 },
             new() { NoteTypeId = 9, Category = "Grammar", Expression = 0, Reading = 1, Meaning = 2 }] };
        var store = new LocalStore(root);
        var importer = new CollectionImporter(store.TemporaryRoot);
        if (mappings)
        {
            settings.Profiles = [new() { Id = "jp", Name = "日本語", DeckRules = [new("日本語", true)] },
                new() { Id = "en", Name = "English", DeckRules = [new("English", true)] },
                new() { Id = "grammar", Name = "文法", DeckRules = [new("文法", true)] }];
            settings.ActiveProfileId = "en";
        }
        DeckProfiles.Ensure(settings, importer.Discover(profile));
        store.SaveCache(importer.Import(settings), settings);
        var reference = mappings ? MappingChecks.Reference() : CoverageChecks.Reference();
        if (corrections) reference.Entries.Add(new("90006", ["影響力"], [new("えいきょうりょく", ["*"])], "influence", true, [], new("影響力", "えいきょうりょく")));
        reference.Entries.AddRange(CoverageMatchingChecks.Reference().Entries);
        reference.Entries.AddRange(DefinitionChecks.Reference().Entries);
        new CoverageReferenceStore(Path.Combine(root, "reference")).Save(reference);
        if (mappings)
        {
            var words = new List<MediaWord> { new(null, "の", "の", 3, "Ambiguous", Sentence: "本物の文。"), new(null, "未解決", "", 1, "Unmatched") };
            new MediaLibraryStore(root).Save(new("synthetic:mappings", "Synthetic source mapping fixture", 7, "Synthetic edition", "Local text", "", "Synthetic text",
                MediaAnalysis.Hash(words), "Synthetic analyzer", "synthetic", DateTimeOffset.UtcNow, null, words, MediaShelves.Completed));
        }
    }
}
