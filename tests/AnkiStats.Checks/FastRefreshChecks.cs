using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AnkiStats.Core;

internal static class FastRefreshChecks
{
    private sealed record Card(long Id, long Deck, long Modified = 1, int Queue = 2);
    private sealed record Note(string Text, long Modified = 1);

    private sealed class Anki : HttpMessageHandler
    {
        public Dictionary<long, Card> Cards { get; } = [];
        public Dictionary<long, Note> Notes { get; } = [];
        public Dictionary<long, List<ReviewRecord>> Reviews { get; } = [];
        public List<long> CardDetails { get; } = [];
        public List<long> NoteDetails { get; } = [];
        public List<long> HistoryReads { get; } = [];
        public long ResponseBytes { get; private set; }
        public bool ChangeDuringVerification { get; set; }
        private int cardModCalls;

        public void ResetCounters()
        {
            CardDetails.Clear(); NoteDetails.Clear(); HistoryReads.Clear(); ResponseBytes = 0; cardModCalls = 0;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var parameters = json.RootElement.GetProperty("params");
            long[] Ids(string name) => parameters.GetProperty(name).EnumerateArray().Select(i => i.GetInt64()).ToArray();
            object result = json.RootElement.GetProperty("action").GetString() switch
            {
                "version" => 6,
                "getActiveProfile" => "RefreshFixture",
                "deckNamesAndIds" => new Dictionary<string, long> { ["Study"] = 1, ["Other"] = 2 },
                "modelNamesAndIds" => new Dictionary<string, long> { ["Japanese"] = 7 },
                "modelFieldNames" => new[] { "Front", "English" },
                "findCards" => Find(parameters.GetProperty("query").GetString()!),
                "cardsModTime" => CardMods(Ids("cards")),
                "notesModTime" => Ids("notes").Select(i => new { noteId = i, mod = Notes[i].Modified }).ToArray(),
                "cardsInfo" => Details(Ids("cards")),
                "notesInfo" => Fields(Ids("notes")),
                "getReviewsOfCards" => History(Ids("cards")),
                _ => throw new InvalidOperationException("Unexpected or write Anki action")
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { result, error = (string?)null });
            ResponseBytes += bytes.Length;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }

        private long[] Find(string query)
        {
            if (query == "is:buried") return Cards.Values.Where(c => c.Queue is -2 or -3).Select(c => c.Id).ToArray();
            var daysOrDeck = int.Parse(query[(query.IndexOf(':') + 1)..], CultureInfo.InvariantCulture);
            if (query.StartsWith("did:", StringComparison.Ordinal)) return Cards.Values.Where(c => c.Deck == daysOrDeck).Select(c => c.Id).ToArray();
            if (!query.StartsWith("rated:", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected search: " + query);
            var cutoff = DateTimeOffset.UtcNow.AddDays(-daysOrDeck).ToUnixTimeMilliseconds();
            return Reviews.Where(p => Cards.ContainsKey(p.Key) && p.Value.Any(r => r.Id > cutoff)).Select(p => p.Key).ToArray();
        }

        private object CardMods(long[] ids)
        {
            cardModCalls++;
            return ids.Select(i => new { cardId = i, mod = Cards[i].Modified + (ChangeDuringVerification && cardModCalls > 1 ? 1 : 0) }).ToArray();
        }

        private object Details(long[] ids)
        {
            CardDetails.AddRange(ids);
            return ids.Select(i => new { cardId = i, note = i, queue = Cards[i].Queue, type = 2, lapses = 0, mod = Cards[i].Modified }).ToArray();
        }

        private object Fields(long[] ids)
        {
            NoteDetails.AddRange(ids);
            return ids.Select(i => new { noteId = i, modelName = "Japanese", fields = new Dictionary<string, object>
                { ["Front"] = new { value = Notes[i].Text }, ["English"] = new { value = "meaning" } } }).ToArray();
        }

        private object History(long[] ids)
        {
            HistoryReads.AddRange(ids);
            return ids.ToDictionary(i => i, i => Reviews.GetValueOrDefault(i, []).Select(r => new
                { id = r.Id, ease = r.Rating, time = r.Milliseconds, type = r.Kind }).ToArray());
        }
    }

    public static void Run(Action<bool, string> check, string sandbox) => RunAsync(check, sandbox).GetAwaiter().GetResult();

    private static async Task RunAsync(Action<bool, string> check, string sandbox)
    {
        const int size = 16_000, changed = 2_000;
        var settings = new Settings { ProfileFolder = Path.Combine(sandbox, "RefreshFixture"), DeckRules = [new("Study", true)],
            Mappings = [new() { NoteTypeId = 7, Category = "Vocabulary", Expression = 0, Meaning = 1 }] };
        using var anki = new Anki();
        using var client = new AnkiConnectClient(handler: anki);
        var importer = new LiveCollectionImporter(client);
        var old = DateTimeOffset.UtcNow.AddDays(-14).ToUnixTimeMilliseconds();
        for (var id = 1; id <= size + 1; id++)
        {
            anki.Cards[id] = new(id, id <= size ? 1 : 2);
            anki.Notes[id] = new("語" + id);
            anki.Reviews[id] = Enumerable.Range(0, 8).Select(offset => new ReviewRecord(old + id * 8 + offset, id, 3, 1000, 1)).ToList();
        }
        anki.Cards[size] = anki.Cards[size] with { Queue = -2 };

        var timer = Stopwatch.StartNew();
        var seeded = await importer.Refresh(settings, includeMedia: true);
        var fullTime = timer.Elapsed;
        var fullBytes = anki.ResponseBytes;
        check(!seeded.Stats.IsIncremental && seeded.Stats.Cards.Count == size && seeded.Stats.Reviews.Count == size * 8,
            "first live refresh seeds a full baseline");
        check(seeded.Media is { Reviews.Count: 0 } && seeded.Media.Cards.Count == size + 1
            && anki.HistoryReads.Count == size * 2 && !anki.HistoryReads.Contains(size + 1), "shared media projection includes other decks without reading their reviews");

        var store = new LocalStore(Path.Combine(sandbox, "fast-refresh"));
        store.SaveCache(seeded.Stats, settings);
        var saved = store.LoadCache(settings.CollectionKey)!;
        check(saved.LiveIndex is { Cards.Count: size + 1, Notes.Count: size + 1 }, "live change index survives reopening the persisted cache");
        anki.ResetCounters(); timer.Restart();
        var unchanged = await importer.Refresh(settings, saved, includeMedia: true);
        var unchangedTime = timer.Elapsed;
        var unchangedBytes = anki.ResponseBytes;
        check(unchanged.Stats.IsIncremental && Equivalent(unchanged.Stats, seeded.Stats)
            && anki.CardDetails.Count + anki.NoteDetails.Count + anki.HistoryReads.Count == 0,
            "unchanged older histories use only inventory and modification checks");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var id = 1; id <= changed; id++)
        {
            anki.Cards[id] = anki.Cards[id] with { Modified = now / 1000 };
            anki.Reviews[id].Add(new(now + id, id, 3, 1500, 1));
        }
        anki.Notes[1] = new("編集", now / 1000);
        anki.ResetCounters(); timer.Restart();
        var delta = await importer.Refresh(settings, unchanged.Stats, includeMedia: true);
        var deltaTime = timer.Elapsed;
        var deltaBytes = anki.ResponseBytes;
        check(anki.CardDetails.Count == changed && anki.NoteDetails.SequenceEqual([1L]) && anki.HistoryReads.Count == changed * 2,
            "2,000 changed cards fetch 2,000 details and verify only those histories");
        check(deltaBytes < fullBytes / 2 && unchangedBytes < deltaBytes,
            "16,000-card benchmark cuts simulated response payload substantially");
        var rebuilt = await importer.Refresh(settings, includeMedia: true);
        check(Equivalent(delta.Stats, rebuilt.Stats) && Equivalent(delta.Media!, rebuilt.Media!),
            "2,000-card fast refresh matches a full rebuild in stats and media");

        var reviewWithoutStamp = new ReviewRecord(now + size, size - 3, 3, 1000, 1);
        anki.Reviews[size - 3].Add(reviewWithoutStamp);
        anki.Notes[1] = anki.Notes[1] with { Modified = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1 };
        var withReview = await importer.Refresh(settings, delta.Stats, includeMedia: true);
        check(withReview.Stats.Reviews.Contains(reviewWithoutStamp), "recent search finds a new review without a card modification stamp");
        anki.Reviews[size - 3].Remove(reviewWithoutStamp);
        anki.Reviews[1].RemoveAt(8);
        anki.Cards[1] = anki.Cards[1] with { Modified = 1 };
        anki.Notes[1] = anki.Notes[1] with { Text = "再編集" }; // Same-second note edit.
        anki.Cards[size] = anki.Cards[size] with { Queue = 2 }; // Automatic unbury without a new timestamp.
        anki.Cards[size - 1] = anki.Cards[size - 1] with { Deck = 2 };
        anki.Cards[size + 1] = anki.Cards[size + 1] with { Deck = 1 };
        anki.Cards.Remove(size - 2); anki.Notes.Remove(size - 2);
        anki.Cards[size + 2] = new(size + 2, 1); anki.Notes[size + 2] = new("追加");
        var reconciled = await importer.Refresh(settings, withReview.Stats, includeMedia: true);
        var full = await importer.Refresh(settings, includeMedia: true);
        check(Equivalent(reconciled.Stats, full.Stats) && Equivalent(reconciled.Media!, full.Media!),
            "fast refresh reconciles undo, same-second note edits, automatic unbury, adds, deletes and deck moves");

        anki.Reviews[size].RemoveAt(0);
        var fast = await importer.Refresh(settings, reconciled.Stats);
        var repaired = await importer.Import(settings);
        check(fast.Stats.Reviews.Count == repaired.Reviews.Count + 1 && !repaired.IsIncremental,
            "full refresh repairs old review-only changes beyond the fast window");
        anki.ResetCounters();
        var mediaOnly = await importer.ImportMedia(settings);
        check(anki.HistoryReads.Count == 0 && mediaOnly.Cards.Count == size + 1, "manual media comparison requests vocabulary without review history");

        anki.ResetCounters(); anki.ChangeDuringVerification = true;
        var rejected = false;
        try { await importer.Refresh(settings, saved); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && Equivalent(store.LoadCache(settings.CollectionKey)!, seeded.Stats), "failed fast refresh preserves the saved baseline");

        Console.WriteLine($"Simulated AnkiConnect (16,000 cards): full {fullTime.TotalSeconds:F2}s / {fullBytes / 1_000_000d:F2} MB, "
            + $"unchanged {unchangedTime.TotalSeconds:F2}s / {unchangedBytes / 1_000_000d:F2} MB, "
            + $"2,000 changed {deltaTime.TotalSeconds:F2}s / {deltaBytes / 1_000_000d:F2} MB.");
    }

    private static bool Equivalent(ImportedCollection left, ImportedCollection right) =>
        left.Cards.OrderBy(c => c.Id).SequenceEqual(right.Cards.OrderBy(c => c.Id))
        && left.Notes.OrderBy(n => n.Id).SequenceEqual(right.Notes.OrderBy(n => n.Id))
        && left.Reviews.SequenceEqual(right.Reviews);
}
