using AnkiStats.Core;
using Microsoft.Data.Sqlite;

internal static class UiFixture
{
    public static void Create(string root)
    {
        Directory.CreateDirectory(root);
        var profile = Path.Combine(root, "synthetic-profile"); Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, ".ankistats-test-data"), "Synthetic test collection. Not personal study data.");
        var file = Path.Combine(profile, "collection.anki2");
        if (File.Exists(file)) throw new IOException("Use an empty UI fixture folder.");
        using (var db = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = """
            CREATE TABLE col(ver INTEGER); INSERT INTO col VALUES(18);
            CREATE TABLE decks(id INTEGER PRIMARY KEY,name TEXT); INSERT INTO decks VALUES(1,'Japanese'),(2,'Japanese::Grammar'),(3,'Later');
            CREATE TABLE notetypes(id INTEGER PRIMARY KEY,name TEXT); INSERT INTO notetypes VALUES(1,'Vocabulary'),(2,'Conjugation');
            CREATE TABLE fields(ntid INTEGER,ord INTEGER,name TEXT,PRIMARY KEY(ntid,ord));
            INSERT INTO fields VALUES(1,0,'Expression'),(1,1,'Reading'),(1,2,'Meaning'),(2,0,'Prompt');
            CREATE TABLE notes(id INTEGER PRIMARY KEY,mid INTEGER,flds TEXT);
            CREATE TABLE cards(id INTEGER PRIMARY KEY,nid INTEGER,did INTEGER,odid INTEGER,queue INTEGER,type INTEGER,lapses INTEGER);
            CREATE TABLE revlog(id INTEGER PRIMARY KEY,cid INTEGER,ease INTEGER,time INTEGER,type INTEGER);
            """;
            cmd.ExecuteNonQuery();
            var words = new[] { ("報う", "むくう", "to reward"), ("報いる", "むくいる", "to repay; to reward"), ("報われる", "むくわれる", "to be rewarded"), ("学ぶ", "まなぶ", "to learn"), ("続ける", "つづける", "to continue"), ("言葉", "ことば", "word; language"), ("昨日", "きのう", "yesterday"), ("明日", "あした", "tomorrow"), ("本", "ほん", "book"), ("読む", "よむ", "to read"), ("日本語", "にほんご", "Japanese language"), ("時間", "じかん", "time") };
            for (var i = 0; i < words.Length; i++)
            {
                cmd.CommandText = "INSERT INTO notes VALUES($id,1,$fields); INSERT INTO cards VALUES($id,$id,1,0,2,2,$lapses);";
                cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("$id",i+1); cmd.Parameters.AddWithValue("$fields",string.Join('\u001f',words[i].Item1,words[i].Item2,words[i].Item3)); cmd.Parameters.AddWithValue("$lapses",i%4); cmd.ExecuteNonQuery();
            }
            using var tx = db.BeginTransaction(); cmd.Transaction = tx;
            var random = new Random(42);
            var today = DateTimeOffset.UtcNow.Date;
            var ordinal = 0;
            for (var day = 799; day >= 0; day--)
            {
                if (day is 45 or 90) continue; // Complete gaps for historical streak selection.
                var count = (day % 15) switch
                {
                    1 => 499, 2 => 500, 3 => 999, 4 => 1000, 5 => 1500, 6 => 2500,
                    _ => random.Next(20,160)
                };
                for (var i = 0; i < count; i++)
                {
                    cmd.CommandText = "INSERT INTO revlog VALUES($id,$card,$rating,$time,$type)"; cmd.Parameters.Clear();
                    var date = new DateTimeOffset(today.AddDays(-day).AddHours(random.Next(12,20)),TimeSpan.Zero);
                    cmd.Parameters.AddWithValue("$id",date.ToUnixTimeMilliseconds()+ordinal++); cmd.Parameters.AddWithValue("$card",random.Next(1,words.Length+1)); cmd.Parameters.AddWithValue("$rating",random.Next(0,10)==0?1:3); cmd.Parameters.AddWithValue("$time",random.Next(700,9000)); cmd.Parameters.AddWithValue("$type",random.Next(0,15)==0?0:1); cmd.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
        var settings = new Settings { ProfileFolder=profile, DeckRules=[new("Japanese",true)], Mappings=[new() { NoteTypeId=1,Category="Vocabulary",Expression=0,Reading=1,Meaning=2 },new() { NoteTypeId=2,Category="Grammar",Expression=0 }] };
        var store = new LocalStore(root);
        store.SaveCache(new CollectionImporter(store.TemporaryRoot).Import(settings),settings);
    }
}
