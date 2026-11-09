using System.Text.Json;

namespace AnkiStats.Core;

/// <summary>Read-only AnkiConnect import for Anki's exclusively locked collection.</summary>
public sealed class LiveCollectionImporter(AnkiConnectClient client)
{
    private const int IndexVersion = 1;
    private const int RecentReviewDays = 2;
    private sealed record Inventory(CollectionMetadata Metadata, Dictionary<long, string> HomeDecks);

    private static InvalidDataException Changed() => new("Anki changed during the live import. Let the current operation finish, then refresh again. Previous results are retained.");

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
            // Anki's exact did: search includes both physical and original deck IDs.
            foreach (var id in await client.Call<long[]>("findCards", new { query = "did:" + deck.Value }))
            {
                if (!membership.TryGetValue(id, out var names)) membership[id] = names = [];
                if (names.Contains(deck.Key) || names.Count == 2) throw Changed();
                names.Add(deck.Key);
            }
        }
        var home = membership.Where(c => c.Value.Count == 1).ToDictionary(c => c.Key, c => c.Value[0]);
        foreach (var batch in membership.Where(c => c.Value.Count == 2).Select(c => c.Key).Chunk(500))
        {
            var expected = batch.ToHashSet();
            var current = await client.Call<Dictionary<string, long[]>>("getDecks", new { cards = batch });
            foreach (var deck in current)
                foreach (var id in deck.Value)
                {
                    if (!expected.Remove(id) || !membership[id].Contains(deck.Key)) throw Changed();
                    home.Add(id, membership[id].Single(n => n != deck.Key));
                }
            if (expected.Count > 0) throw Changed();
        }
        await Verify(folder);
        var counts = home.Values.CountBy(n => n).ToDictionary();
        return new(new(0, decks.Select(d => new Deck(d.Value, d.Key, counts.GetValueOrDefault(d.Key))).OrderBy(d => d.Name, StringComparer.Ordinal).ToList(),
            types.OrderBy(t => t.Name, StringComparer.Ordinal).ToList()), home);
    }

    public async Task<CollectionMetadata> Discover(string profileFolder) => (await InventoryFor(profileFolder)).Metadata;

    public async Task<ImportedCollection> Import(Settings settings, IProgress<string>? progress = null) =>
        (await Refresh(settings, progress: progress)).Stats;

    public async Task<ImportedCollection> ImportMedia(Settings settings) =>
        (await Read(settings, null, false, mediaOnly: true, progress: null))!.Stats;

    public async Task<CollectionRefresh> Refresh(Settings settings, ImportedCollection? previous = null,
        bool includeMedia = false, IProgress<string>? progress = null) =>
        (await Read(settings, previous, includeMedia, mediaOnly: false, progress))!;

    internal Task<CollectionRefresh?> TryFastRefresh(Settings settings, ImportedCollection previous,
        bool includeMedia, IProgress<string>? progress) => Read(settings, previous, includeMedia, mediaOnly: false, progress, fastOnly: true);

    private async Task<CollectionRefresh?> Read(Settings settings, ImportedCollection? previous, bool includeMedia,
        bool mediaOnly, IProgress<string>? progress, bool fastOnly = false)
    {
        var started = DateTimeOffset.UtcNow;
        var index = previous?.LiveIndex;
        // A long gap uses a full baseline. Keep the rated search within a bounded range.
        if (index?.Version != IndexVersion || previous!.CollectionKey != settings.CollectionKey || previous.Signature != settings.Signature
            || started < index.ReadStartedAt || started - index.ReadStartedAt > TimeSpan.FromDays(28))
            index = null;
        if (fastOnly && index is null) return null;

        progress?.Report("Checking live deck membership and field definitions…");
        var inventory = await InventoryFor(settings.ProfileFolder);
        if (mediaOnly) settings = MediaAnalysis.CollectionSettings(settings, inventory.Metadata);
        if (index is not null && !SameNoteTypes(previous!.Metadata, inventory.Metadata))
        {
            if (fastOnly) return null;
            index = null;
        }

        var selectedDecks = inventory.Metadata.Decks.Where(d => settings.Includes(d.Name)).Select(d => d.Name).ToHashSet();
        var expectedDecks = inventory.HomeDecks.Where(c => includeMedia || selectedDecks.Contains(c.Value)).ToDictionary();
        Dictionary<long, long>? initialCardMods = null;
        var cards = new Dictionary<long, LiveCard>();
        var changedCards = expectedDecks.Keys.ToArray();
        var buried = new HashSet<long>();
        // Modification times have second precision. Re-fetch rows near the prior
        // read boundary even when the timestamp is equal.
        var overlap = index?.ReadStartedAt.AddSeconds(-2).ToUnixTimeSeconds() ?? long.MaxValue;
        if (index is not null)
        {
            progress?.Report($"Checking modification times for {expectedDecks.Count:N0} live cards…");
            initialCardMods = await ReadMods(expectedDecks.Keys, "cardsModTime", "cards", "cardId");
            // Anki can unbury cards at rollover without advancing their modification time.
            buried = await FindCards("is:buried", expectedDecks.ContainsKey);
            foreach (var (id, mod) in initialCardMods)
                if (index.Cards.TryGetValue(id, out var cached) && cached.Modified == mod && mod < overlap
                    && cached.Card.Deck == expectedDecks[id] && buried.Contains(id) == IsBuried(cached.Card))
                    cards.Add(id, cached);
            changedCards = expectedDecks.Keys.Where(id => !cards.ContainsKey(id)).ToArray();
        }
        await ReadCards(cards, changedCards, expectedDecks, initialCardMods, progress);
        if (index is not null && changedCards.Any(id => buried.Contains(id) != IsBuried(cards[id].Card))) throw Changed();

        progress?.Report("Checking live note changes…");
        var noteMods = await ReadMods(cards.Values.Select(c => c.Card.NoteId).Distinct(), "notesModTime", "notes", "noteId");
        var notes = new Dictionary<long, LiveNote>();
        if (index is not null)
            foreach (var (id, mod) in noteMods)
                if (index.Notes.TryGetValue(id, out var cached) && cached.Modified == mod && mod < overlap)
                    notes.Add(id, cached);
        await ReadNotes(notes, noteMods, inventory.Metadata, progress);

        var stats = await Task.Run(() => Project(settings, inventory.Metadata, cards, notes, progress));
        var statsIds = stats.Cards.Select(c => c.Id).ToHashSet();
        var reviewIds = new HashSet<long>();
        var recent = new HashSet<long>();
        var recentQuery = "";
        if (!mediaOnly)
        {
            if (index is null) reviewIds = statsIds;
            else
            {
                var days = RecentReviewDays + (int)Math.Ceiling((started - index.ReadStartedAt).TotalDays);
                recentQuery = FormattableString.Invariant($"rated:{days}");
                recent = await FindCards(recentQuery, statsIds.Contains);
                var priorStatsIds = previous!.Cards.Select(c => c.Id).ToHashSet();
                var cachedReviewCutoff = index.ReadStartedAt.AddDays(-RecentReviewDays).ToUnixTimeMilliseconds();
                reviewIds.UnionWith(changedCards.Where(statsIds.Contains));
                reviewIds.UnionWith(statsIds.Where(id => !priorStatsIds.Contains(id)));
                reviewIds.UnionWith(recent);
                // An undone review can be absent from the current rated search.
                reviewIds.UnionWith(previous.Reviews.Where(r => r.Id >= cachedReviewCutoff && statsIds.Contains(r.CardId)).Select(r => r.CardId));
            }
        }
        var histories = await ReadReviews(reviewIds, progress);

        progress?.Report("Verifying the live Anki read…");
        var after = await InventoryFor(settings.ProfileFolder);
        if (!SameNoteTypes(after.Metadata, inventory.Metadata) || !after.Metadata.Decks.SequenceEqual(inventory.Metadata.Decks)
            || !SameEntries(inventory.HomeDecks, after.HomeDecks)) throw Changed();
        var cardModsAfter = await ReadMods(cards.Keys, "cardsModTime", "cards", "cardId");
        if (cardModsAfter.Any(c => cards[c.Key].Modified != c.Value)) throw Changed();
        var noteModsAfter = await ReadMods(noteMods.Keys, "notesModTime", "notes", "noteId");
        if (!SameEntries(noteMods, noteModsAfter)) throw Changed();
        await ReadReviews(reviewIds, expected: histories);
        if (index is not null && (!recent.SetEquals(await FindCards(recentQuery, statsIds.Contains))
            || !buried.SetEquals(await FindCards("is:buried", expectedDecks.ContainsKey)))) throw Changed();
        await Verify(settings.ProfileFolder);

        return await Task.Run(() =>
        {
            var reviews = histories.Values.SelectMany(r => r);
            if (index is not null)
                reviews = reviews.Concat(previous!.Reviews.Where(r => statsIds.Contains(r.CardId) && !reviewIds.Contains(r.CardId)));
            stats.Reviews = reviews.OrderBy(r => r.Id).ToList();
            stats.UnknownReviewKinds = stats.Reviews.Count(r => r.Kind is < 0 or > 5);
            stats.RefreshedAt = DateTimeOffset.UtcNow;
            stats.IsIncremental = index is not null;
            if (!mediaOnly) stats.LiveIndex = new(IndexVersion, started, cards, notes);

            var media = includeMedia ? Project(MediaAnalysis.CollectionSettings(settings, inventory.Metadata),
                inventory.Metadata, cards, notes) : null;
            return new CollectionRefresh(stats, media);
        });
    }

    private async Task<HashSet<long>> FindCards(string query, Func<long, bool> includes)
    {
        var ids = await client.Call<long[]>("findCards", new { query });
        return ids.Where(includes).ToHashSet();
    }

    private static bool IsBuried(CardRecord card) => card.Queue is -2 or -3;

    private static bool SameNoteTypes(CollectionMetadata left, CollectionMetadata right) =>
        left.NoteTypes.Count == right.NoteTypes.Count && left.NoteTypes.Zip(right.NoteTypes).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name && pair.First.Fields.SequenceEqual(pair.Second.Fields));

    private static bool SameEntries<T>(Dictionary<long, T> left, Dictionary<long, T> right) where T : IEquatable<T> =>
        left.Count == right.Count && left.All(p => right.TryGetValue(p.Key, out var value) && p.Value.Equals(value));

    private async Task<Dictionary<long, long>> ReadMods(IEnumerable<long> ids, string action, string parameter, string idProperty)
    {
        var result = new Dictionary<long, long>();
        foreach (var batch in ids.Chunk(500))
        {
            var remaining = batch.ToHashSet();
            var mods = await client.Call<JsonElement[]>(action, new Dictionary<string, long[]> { [parameter] = batch });
            foreach (var item in mods)
            {
                if (!item.TryGetProperty(idProperty, out var id) || !remaining.Remove(id.GetInt64())) throw Changed();
                result.Add(id.GetInt64(), item.GetProperty("mod").GetInt64());
            }
            if (remaining.Count > 0) throw Changed();
        }
        return result;
    }

    private async Task ReadCards(Dictionary<long, LiveCard> cards, long[] ids, Dictionary<long, string> decks,
        Dictionary<long, long>? expectedMods, IProgress<string>? progress)
    {
        var selective = true;
        var read = 0;
        foreach (var batch in ids.Chunk(200))
        {
            progress?.Report($"Reading live card details… {read:N0} / {ids.Length:N0}");
            JsonElement[] info;
            try
            {
                info = selective ? await client.Call<JsonElement[]>("cardsInfo",
                    new { cards = batch, fields = new[] { "cardId", "note", "queue", "type", "lapses", "mod" } })
                    : await client.Call<JsonElement[]>("cardsInfo", new { cards = batch });
            }
            catch (InvalidOperationException error) when (selective && error.Message.Contains("unexpected keyword argument") && error.Message.Contains("fields"))
            {
                selective = false;
                info = await client.Call<JsonElement[]>("cardsInfo", new { cards = batch });
            }
            var remaining = batch.ToHashSet();
            foreach (var item in info)
            {
                if (!item.TryGetProperty("cardId", out var key) || !remaining.Remove(key.GetInt64())) throw Changed();
                var id = key.GetInt64();
                var mod = item.GetProperty("mod").GetInt64();
                if (expectedMods is not null && expectedMods[id] != mod) throw Changed();
                cards.Add(id, new(new(id, item.GetProperty("note").GetInt64(), decks[id],
                    item.GetProperty("queue").GetInt32(), item.GetProperty("type").GetInt32(), item.GetProperty("lapses").GetInt32()), mod));
            }
            if (remaining.Count > 0) throw Changed();
            read += batch.Length;
        }
    }

    private async Task ReadNotes(Dictionary<long, LiveNote> notes, Dictionary<long, long> mods, CollectionMetadata metadata,
        IProgress<string>? progress)
    {
        var types = metadata.NoteTypes.ToDictionary(t => t.Name);
        var changed = mods.Keys.Where(id => !notes.ContainsKey(id)).ToArray();
        var read = 0;
        foreach (var batch in changed.Chunk(500))
        {
            progress?.Report($"Reading live note fields… {read:N0} / {changed.Length:N0}");
            var info = await client.Call<JsonElement[]>("notesInfo", new { notes = batch });
            var remaining = batch.ToHashSet();
            foreach (var item in info)
            {
                if (!item.TryGetProperty("noteId", out var key) || !remaining.Remove(key.GetInt64())) throw Changed();
                var id = key.GetInt64();
                if (!types.TryGetValue(item.GetProperty("modelName").GetString() ?? "", out var type)) throw Changed();
                var fields = item.GetProperty("fields");
                if (type.Fields.Any(f => !fields.TryGetProperty(f, out _))) throw Changed();
                notes.Add(id, new(new(id, type.Id, string.Join('\u001f',
                    type.Fields.Select(f => fields.GetProperty(f).GetProperty("value").GetString() ?? ""))), mods[id]));
            }
            if (remaining.Count > 0) throw Changed();
            read += batch.Length;
        }
    }

    private async Task<Dictionary<long, ReviewRecord[]>> ReadReviews(HashSet<long> ids, IProgress<string>? progress = null,
        Dictionary<long, ReviewRecord[]>? expected = null)
    {
        var result = new Dictionary<long, ReviewRecord[]>();
        foreach (var batch in ids.Chunk(200))
        {
            progress?.Report($"Reading live review history… {result.Count:N0} / {ids.Count:N0} cards");
            var history = await client.Call<Dictionary<string, JsonElement[]>>("getReviewsOfCards", new { cards = batch });
            var remaining = batch.ToHashSet();
            foreach (var entry in history)
            {
                if (!long.TryParse(entry.Key, out var id) || !remaining.Remove(id)) throw Changed();
                var rows = entry.Value.Select(r => new ReviewRecord(r.GetProperty("id").GetInt64(), id,
                    r.GetProperty("ease").GetInt32(), r.GetProperty("time").GetInt32(), r.GetProperty("type").GetInt32()))
                    .OrderBy(r => r.Id).ToArray();
                if (expected is null) result.Add(id, rows);
                else if (!rows.SequenceEqual(expected[id])) throw Changed();
            }
            if (remaining.Count > 0) throw Changed();
        }
        return result;
    }

    private static ImportedCollection Project(Settings settings, CollectionMetadata metadata, Dictionary<long, LiveCard> cards,
        Dictionary<long, LiveNote> notes, IProgress<string>? progress = null)
    {
        var selectedDecks = metadata.Decks.Where(d => settings.Includes(d.Name)).Select(d => d.Name).ToHashSet();
        var result = CollectionImporter.Project(settings, metadata, cards.Values.Select(c => c.Card).Where(c => selectedDecks.Contains(c.Deck)),
            notes.Values.Select(n => n.Note), progress);
        // AnkiConnect cannot enumerate review rows whose cards have been deleted.
        result.UnresolvedReviewsAvailable = false;
        result.ImportMethod = "AnkiConnect";
        return result;
    }
}
