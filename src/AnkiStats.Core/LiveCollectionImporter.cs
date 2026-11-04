using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AnkiStats.Core;

/// <summary>Read-only AnkiConnect import for Anki's exclusively locked collection.</summary>
public sealed class LiveCollectionImporter(AnkiConnectClient client)
{
    private sealed record Inventory(CollectionMetadata Metadata, Dictionary<long, string> HomeDecks);
    private static InvalidDataException Changed() => new("Anki changed during the live import. Let the current operation finish, then Refresh again. Previous results are retained.");
    private async Task Verify(string folder)
    {
        if (await client.Call<int>("version") < 6) throw new InvalidOperationException("Live import requires AnkiConnect API version 6 or later.");
        await AnkiConnection.VerifyProfile(client, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)));
    }

    private async Task<Inventory> InventoryFor(string folder)
    {
        await Verify(folder);
        var decks = await client.Call<Dictionary<string, long>>("deckNamesAndIds");
        var models = await client.Call<Dictionary<string, long>>("modelNamesAndIds");
        var types = new List<NoteType>();
        foreach (var model in models)
            types.Add(new(model.Value, model.Key, await client.Call<List<string>>("modelFieldNames", new { modelName = model.Key })));
        var membership = new Dictionary<long, List<string>>();
        foreach (var deck in decks)
        {
            // did: matches exactly this ID (no descendants), including a filtered
            // card's original deck. IDs avoid escaping/wildcards in deck names.
            foreach (var id in await client.Call<long[]>("findCards", new { query = "did:" + deck.Value }))
            {
                if (!membership.TryGetValue(id, out var names)) membership[id] = names = [];
                if (!names.Contains(deck.Key)) names.Add(deck.Key);
            }
        }
        var home = new Dictionary<long, string>();
        foreach (var batch in membership.Keys.Chunk(500))
        {
            var current = await client.Call<Dictionary<string, long[]>>("getDecks", new { cards = batch });
            foreach (var deck in current)
                foreach (var id in deck.Value)
                {
                    if (!batch.Contains(id) || !membership.TryGetValue(id, out var names)) throw Changed();
                    var original = names.Where(n => n != deck.Key).ToList();
                    if (original.Count > 1 || !names.Contains(deck.Key) || !home.TryAdd(id, original.SingleOrDefault() ?? deck.Key)) throw Changed();
                }
            if (batch.Any(id => !home.ContainsKey(id))) throw Changed();
        }
        await Verify(folder);
        // API metadata does not expose the source SQLite schema version.
        return new(new(0, decks.Select(d => new Deck(d.Value, d.Key, home.Values.Count(n => n == d.Key))).OrderBy(d => d.Name, StringComparer.Ordinal).ToList(),
            types.OrderBy(t => t.Name, StringComparer.Ordinal).ToList()), home);
    }

    public async Task<CollectionMetadata> Discover(string profileFolder) => (await InventoryFor(profileFolder)).Metadata;

    public async Task<ImportedCollection> Import(Settings settings, IProgress<string>? progress = null)
    {
        progress?.Report("Reading the running Anki collection through AnkiConnect…");
        var inventory = await InventoryFor(settings.ProfileFolder);
        var selected = inventory.HomeDecks.Where(c => settings.Includes(c.Value)).Select(c => c.Key).ToArray();
        var noteIds = new HashSet<long>();
        var cards = new List<JsonElement>();
        var reviews = new List<ReviewRecord>();
        foreach (var batch in selected.Chunk(200))
        {
            progress?.Report($"Reading live cards and review history… {cards.Count:N0} / {selected.Length:N0}");
            JsonElement[] info;
            try { info = await client.Call<JsonElement[]>("cardsInfo", new { cards = batch, fields = new[] { "cardId", "note", "queue", "type", "lapses", "mod", "deckName" } }); }
            catch (InvalidOperationException error) when (error.Message.Contains("unexpected keyword argument") && error.Message.Contains("fields"))
            { info = await client.Call<JsonElement[]>("cardsInfo", new { cards = batch }); }
            if (info.Length != batch.Length || !info.Select(c => c.TryGetProperty("cardId", out var id) ? id.GetInt64() : 0).ToHashSet().SetEquals(batch)) throw Changed();
            foreach (var card in info) { cards.Add(card); noteIds.Add(card.GetProperty("note").GetInt64()); }
            var history = await client.Call<Dictionary<string, JsonElement[]>>("getReviewsOfCards", new { cards = batch });
            if (!history.Keys.Select(long.Parse).ToHashSet().SetEquals(batch)) throw Changed();
            foreach (var entry in history)
                foreach (var review in entry.Value)
                    reviews.Add(new(review.GetProperty("id").GetInt64(), long.Parse(entry.Key), review.GetProperty("ease").GetInt32(), review.GetProperty("time").GetInt32(), review.GetProperty("type").GetInt32()));
        }
        var notes = new List<JsonElement>();
        var noteMods = await NoteMods(noteIds);
        foreach (var batch in noteIds.Chunk(500))
        {
            var info = await client.Call<JsonElement[]>("notesInfo", new { notes = batch });
            if (info.Length != batch.Length || !info.Select(n => n.TryGetProperty("noteId", out var id) ? id.GetInt64() : 0).ToHashSet().SetEquals(batch)) throw Changed();
            notes.AddRange(info);
        }
        // Reject detectable edits/profile switches instead of replacing a good cache
        // with a mixture of deck scopes or scheduling states from different reads.
        var after = await InventoryFor(settings.ProfileFolder);
        var noteModsAfter = await NoteMods(noteIds);
        if (JsonSerializer.Serialize(after.Metadata) != JsonSerializer.Serialize(inventory.Metadata)
            || after.HomeDecks.Count != inventory.HomeDecks.Count || after.HomeDecks.Any(c => !inventory.HomeDecks.TryGetValue(c.Key, out var name) || name != c.Value)
            || noteMods.Count != noteModsAfter.Count || noteMods.Any(n => !noteModsAfter.TryGetValue(n.Key, out var mod) || mod != n.Value)) throw Changed();
        foreach (var batch in cards.Chunk(500))
        {
            var mods = await client.Call<JsonElement[]>("cardsModTime", new { cards = batch.Select(c => c.GetProperty("cardId").GetInt64()).ToArray() });
            var expected = batch.ToDictionary(c => c.GetProperty("cardId").GetInt64(), c => c.GetProperty("mod").GetInt64());
            if (mods.Length != batch.Length || !mods.Select(c => c.TryGetProperty("cardId", out var id) ? id.GetInt64() : 0).ToHashSet().SetEquals(expected.Keys)
                || mods.Any(c => !c.TryGetProperty("cardId", out var id) || !expected.TryGetValue(id.GetInt64(), out var mod) || c.GetProperty("mod").GetInt64() != mod)) throw Changed();
        }
        var expectedReviews = reviews.GroupBy(r => r.CardId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id).ToArray());
        foreach (var batch in selected.Chunk(200))
        {
            var history = await client.Call<Dictionary<string, JsonElement[]>>("getReviewsOfCards", new { cards = batch });
            if (!history.Keys.Select(long.Parse).ToHashSet().SetEquals(batch)) throw Changed();
            foreach (var entry in history)
            {
                var id = long.Parse(entry.Key);
                var current = entry.Value.Select(r => new ReviewRecord(r.GetProperty("id").GetInt64(), id, r.GetProperty("ease").GetInt32(), r.GetProperty("time").GetInt32(), r.GetProperty("type").GetInt32())).OrderBy(r => r.Id);
                if (!current.SequenceEqual(expectedReviews.GetValueOrDefault(id) ?? [])) throw Changed();
            }
        }
        await Verify(settings.ProfileFolder);
        return await Task.Run(() => Project(settings, inventory, cards, notes, reviews));
    }

    private async Task<Dictionary<long, long>> NoteMods(IEnumerable<long> ids)
    {
        var result = new Dictionary<long, long>();
        foreach (var batch in ids.Chunk(500))
        {
            var mods = await client.Call<JsonElement[]>("notesModTime", new { notes = batch });
            if (mods.Length != batch.Length) throw Changed();
            foreach (var item in mods)
                if (!item.TryGetProperty("noteId", out var id) || !batch.Contains(id.GetInt64()) || !result.TryAdd(id.GetInt64(), item.GetProperty("mod").GetInt64())) throw Changed();
        }
        return result;
    }

    private static ImportedCollection Project(Settings settings, Inventory inventory, List<JsonElement> cards, List<JsonElement> notes, List<ReviewRecord> reviews)
    {
        // An app-owned in-memory schema reuses the existing mapping validation,
        // text extraction, filtered-deck scope, and review-kind rules unchanged.
        using var db = new SqliteConnection("Data Source=:memory:"); db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE col(ver INTEGER); INSERT INTO col VALUES(18); CREATE TABLE decks(id INTEGER,name TEXT); CREATE TABLE notetypes(id INTEGER,name TEXT); CREATE TABLE fields(ntid INTEGER,ord INTEGER,name TEXT); CREATE TABLE cards(id INTEGER,nid INTEGER,did INTEGER,odid INTEGER,queue INTEGER,type INTEGER,lapses INTEGER); CREATE TABLE notes(id INTEGER,mid INTEGER,flds TEXT); CREATE TABLE revlog(id INTEGER,cid INTEGER,ease INTEGER,time INTEGER,type INTEGER);";
        command.ExecuteNonQuery();
        using var transaction = db.BeginTransaction(); command.Transaction = transaction;
        void Insert(string sql, params object[] values)
        {
            command.CommandText = sql; command.Parameters.Clear();
            for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("$p" + i, values[i]);
            command.ExecuteNonQuery();
        }
        foreach (var deck in inventory.Metadata.Decks) Insert("INSERT INTO decks VALUES($p0,$p1)", deck.Id, deck.Name);
        foreach (var type in inventory.Metadata.NoteTypes)
        {
            Insert("INSERT INTO notetypes VALUES($p0,$p1)", type.Id, type.Name);
            for (var i = 0; i < type.Fields.Count; i++) Insert("INSERT INTO fields VALUES($p0,$p1,$p2)", type.Id, i, type.Fields[i]);
        }
        // Include all cards for metadata counts; only selected cards need scheduling.
        var details = cards.ToDictionary(c => c.GetProperty("cardId").GetInt64());
        var deckIds = inventory.Metadata.Decks.ToDictionary(d => d.Name, d => d.Id);
        foreach (var card in inventory.HomeDecks)
        {
            var present = details.TryGetValue(card.Key, out var detail);
            Insert("INSERT INTO cards VALUES($p0,$p1,$p2,0,$p3,$p4,$p5)", card.Key, present ? detail.GetProperty("note").GetInt64() : 0L, deckIds[card.Value],
                present ? detail.GetProperty("queue").GetInt32() : 0, present ? detail.GetProperty("type").GetInt32() : 0, present ? detail.GetProperty("lapses").GetInt32() : 0);
        }
        foreach (var note in notes)
        {
            var type = inventory.Metadata.NoteTypes.SingleOrDefault(t => t.Name == note.GetProperty("modelName").GetString()) ?? throw Changed();
            var fields = note.GetProperty("fields");
            if (type.Fields.Any(f => !fields.TryGetProperty(f, out _))) throw Changed();
            Insert("INSERT INTO notes VALUES($p0,$p1,$p2)", note.GetProperty("noteId").GetInt64(), type.Id,
                string.Join('\u001f', type.Fields.Select(f => fields.GetProperty(f).GetProperty("value").GetString() ?? "")));
        }
        foreach (var review in reviews) Insert("INSERT INTO revlog VALUES($p0,$p1,$p2,$p3,$p4)", review.Id, review.CardId, review.Rating, review.Milliseconds, review.Kind);
        transaction.Commit();
        var result = CollectionImporter.ReadCollection(db, settings);
        result.Metadata = inventory.Metadata;
        // AnkiConnect cannot enumerate review rows whose cards have been deleted.
        result.UnresolvedReviewsAvailable = false;
        result.ImportMethod = "AnkiConnect";
        return result;
    }
}
