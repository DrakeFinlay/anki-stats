using AnkiStats.Core;
using Microsoft.Data.Sqlite;

internal static class LearningUiFixture
{
    public static void Create(string root)
    {
        UiFixture.Create(root);
        var profile = Path.Combine(root, "synthetic-profile");
        using (var db = new SqliteConnection($"Data Source={Path.Combine(profile, "collection.anki2")};Pooling=False"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO decks VALUES(5,'日本語::永遠'),(6,'日本語::Suspended'),(7,'漢字::Relearn'),(8,'漢字::Unused'),(9,'日本語::Seen in Immersion'); INSERT INTO fields VALUES(3,1,'Back');";
            command.ExecuteNonQuery();
            var examples = new[]
            {
                (14, "森", "", "grove", 3, 4, 3, 2, 2),
                (15, "森林[しんりん]", "しんりん", "dense grove; thick grove", 1, 1, 3, 2, 2),
                (16, "深林[しんりん]", "しんりん", "deep grove; thick grove", 1, 1, 4, 2, 2),
                (17, "山", "", "mountain", 3, 4, 1, 2, 2),
                (18, "山川[やまかわ]", "やまかわ", "mountains and rivers", 1, 1, 3, 2, 2),
                (19, "山道[やまみち]", "やまみち", "mountain path", 1, 5, 5, 2, 2),
                (20, "海", "", "sea", 3, 4, 5, 2, 2),
                (21, "海辺[うみべ]", "うみべ", "seashore", 1, 5, 4, 2, 2),
                (22, "未", "", "not yet", 3, 7, 0, 0, 0),
                (23, "火", "", "fire", 3, 8, 8, -1, 2),
                (24, "火山[かざん]", "かざん", "volcano", 1, 1, 3, 2, 2),
                (25, "未学[みがく]", "みがく", "synthetic recovery item", 1, 6, 0, 0, 0),
                (26, "森林[しんりん]", "しんりん", "thick grove", 1, 9, 5, 2, 2),
                (27, "晴れる[はれる]", "はれる", "become clear", 1, 1, 3, 2, 2),
                (28, "晴らす[はらす]", "はらす", "clear away", 1, 1, 4, 2, 2)
            };
            foreach (var (id, expression, reading, meaning, type, deck, lapses, queue, state) in examples)
            {
                command.CommandText = "INSERT INTO notes VALUES($id,$type,$fields); INSERT INTO cards VALUES($id,$id,$deck,0,$queue,$state,0);";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$type", type);
                command.Parameters.AddWithValue("$fields", type == 3 ? expression + '\u001f' + meaning : string.Join('\u001f', expression, reading, meaning));
                command.Parameters.AddWithValue("$deck", deck); command.Parameters.AddWithValue("$queue", queue); command.Parameters.AddWithValue("$state", state);
                command.ExecuteNonQuery();
                if (state == 0) continue;
                for (var answer = 0; answer < 12; answer++)
                {
                    command.CommandText = "INSERT INTO revlog VALUES($id,$card,$rating,1000,1)";
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("$id", new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-5).AddHours(9), TimeSpan.Zero).ToUnixTimeMilliseconds() + id * 100 + answer);
                    command.Parameters.AddWithValue("$card", id); command.Parameters.AddWithValue("$rating", answer < lapses ? 1 : 3); command.ExecuteNonQuery();
                }
            }
        }
        var store = new LocalStore(root);
        var settings = store.LoadSettings()!;
        settings.Mappings.Single(m => m.NoteTypeId == 3).Meaning = 1;
        var importer = new CollectionImporter(store.TemporaryRoot);
        DeckProfiles.Ensure(settings, importer.Discover(profile));
        settings.ActiveProfileId = settings.Profiles.Single(p => p.Name == "日本語 + 漢字").Id;
        store.SaveCache(importer.Import(settings), settings);
    }
}
