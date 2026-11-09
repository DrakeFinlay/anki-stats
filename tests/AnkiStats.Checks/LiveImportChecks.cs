using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AnkiStats.Core;
using Microsoft.Data.Sqlite;

internal static class LiveImportChecks
{
    private sealed class Mock : HttpMessageHandler
    {
        public List<string> Actions { get; } = [];
        public string Profile = "Synthetic";
        public bool WrongProfile, MissingCard, ChangedCard, ChangedNote, ChangedReviews, ChangedFields, Unavailable;
        private int noteModReads, reviewReads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var action = document.RootElement.GetProperty("action").GetString()!;
            var parameters = document.RootElement.GetProperty("params"); Actions.Add(action);
            if (Unavailable) throw new HttpRequestException("AnkiConnect is offline", new SocketException((int)SocketError.ConnectionRefused));
            long[] Ids(string name) => parameters.GetProperty(name).EnumerateArray().Select(i => i.GetInt64()).ToArray();
            object? result = action switch
            {
                "version" => 6,
                "getActiveProfile" => WrongProfile ? "Wrong" : Profile,
                "deckNamesAndIds" => new Dictionary<string, long> { ["Language"] = 1, ["Filtered"] = 2, ["Language::Excluded"] = 3, ["Other"] = 4 },
                "modelNamesAndIds" => new Dictionary<string, long> { ["Japanese"] = 7 },
                "modelFieldNames" => ChangedFields ? new[] { "Renamed", "English" } : new[] { "Front", "English" },
                "findCards" => parameters.GetProperty("query").GetString() switch
                {
                    "did:1" => new long[] { 1, 4 }, "did:2" => [1], "did:3" => [2], "did:4" => [3],
                    "is:buried" => [],
                    string query when query.StartsWith("rated:", StringComparison.Ordinal) => [],
                    _ => throw new Exception("Unexpected query")
                },
                "getDecks" => Ids("cards").GroupBy(i => i == 1 ? "Filtered" : i == 2 ? "Language::Excluded" : i == 3 ? "Other" : "Language").ToDictionary(g => g.Key, g => g.ToArray()),
                "cardsInfo" => Ids("cards").Select(i => MissingCard ? (object)new { } : new { cardId = i, note = 10, queue = -1, type = 2, lapses = 5, mod = 1, deckName = i == 1 ? "Filtered" : "Language" }).ToArray(),
                "cardsModTime" => Ids("cards").Select(i => new { cardId = i, mod = ChangedCard ? 2 : 1 }).ToArray(),
                "notesModTime" => Ids("notes").Select(i => new { noteId = i, mod = ChangedNote && ++noteModReads > 1 ? 2 : 1 }).ToArray(),
                "notesInfo" => Ids("notes").Select(i => new { noteId = i, modelName = "Japanese", fields = new Dictionary<string, object> { ["Front"] = new { value = "<b>晴れる[はれる]</b>", order = 0 }, ["English"] = new { value = "clear up", order = 1 } } }).ToArray(),
                "getReviewsOfCards" => History(Ids("cards")),
                _ => throw new Exception("Unexpected/read-write Anki action: " + action)
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { result, error = (string?)null }), Encoding.UTF8, "application/json") };
        }
        private Dictionary<string, object[]> History(long[] ids)
        {
            reviewReads++;
            return ids.ToDictionary(i => i.ToString(), i => (i == 1
                ? new[] { new { id = 100, ease = 3, time = 1000, type = 1 }, new { id = 101, ease = 0, time = 0, type = 4 }, new { id = 102, ease = 0, time = 0, type = 99 } }
                : [new { id = 103, ease = 2, time = ChangedReviews && reviewReads > 1 ? 2000 : 1000, type = 2 }]).Cast<object>().ToArray());
        }
    }

    public static void Run(Action<bool, string> check, string sandbox, string fixture) => RunAsync(check, sandbox, fixture).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check, string sandbox, string fixture)
    {
        var settings = new Settings { ProfileFolder = Path.Combine(sandbox, "Synthetic"), DeckRules = [new("Language", true), new("Language::Excluded", false)],
            Mappings = [new() { NoteTypeId = 7, Category = "Vocabulary", Expression = 0, Meaning = 1, StripBracketReadings = true, ExpressionName = "Front", MeaningName = "English" }] };
        using var mock = new Mock(); using var client = new AnkiConnectClient(handler: mock);
        var reader = new LiveCollectionImporter(client);
        var metadata = await reader.Discover(settings.ProfileFolder);
        check(metadata.Decks.Single(d => d.Name == "Language").Cards == 2 && metadata.Decks.Single(d => d.Name == "Filtered").Cards == 0,
            "live metadata assigns filtered cards to home decks without duplicate counts");
        var data = await reader.Import(settings);
        check(data.Cards.Count == 2 && data.Cards.All(c => c.Deck == "Language" && c.Queue == -1 && c.LifetimeLapses == 5), "live selection preserves home deck, suspension and lapses");
        check(data.Notes is [{ Expression: "晴れる", SourceExpression: "晴れる[はれる]", Meaning: "clear up" }], "live notes reuse mapping and furigana extraction with sibling deduplication");
        check(data.Reviews.Count == 4 && data.UnknownReviewKinds == 1 && data.Reviews.Any(r => r.Kind == 4), "live reviews retain learning, administrative and unknown kinds for shared analysis rules");
        check(!data.UnresolvedReviewsAvailable && data.ImportMethod == "AnkiConnect", "live orphan count is explicitly unavailable instead of invented zero");
        var profile = new DeckProfile { Name = "Language", DeckRules = [new("Language", true)] };
        var selected = DeckProfiles.Select(data, profile);
        check(!selected.UnresolvedReviewsAvailable && selected.ImportMethod == "AnkiConnect", "deck profile selection retains live import provenance and unavailable counts");
        var store = new LocalStore(Path.Combine(sandbox, "live-cache")); store.SaveCache(data, settings);
        check(store.LoadSettings()!.Signature == settings.Signature && !store.LoadCache(settings.CollectionKey)!.UnresolvedReviewsAvailable, "live import settings and unavailable metadata survive cache roundtrip");
        foreach (var failure in new[] { "profile", "missing", "card", "note", "reviews", "fields" })
        {
            using var handler = new Mock { WrongProfile = failure == "profile", MissingCard = failure == "missing", ChangedCard = failure == "card", ChangedNote = failure == "note", ChangedReviews = failure == "reviews", ChangedFields = failure == "fields" };
            using var failingClient = new AnkiConnectClient(handler: handler);
            var rejected = false;
            try { await new LiveCollectionImporter(failingClient).Import(settings); }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException) { rejected = true; }
            check(rejected, "live import rejects changed/invalid " + failure);
        }
        check(store.LoadCache(settings.CollectionKey)!.Signature == data.Signature, "failed live reads preserve last successful cache");

        Directory.CreateDirectory(settings.ProfileFolder);
        File.Copy(fixture, Path.Combine(settings.ProfileFolder, "collection.anki2"));
        var startupTemp = Path.Combine(sandbox, "startup-temp");
        var startupCache = store.LoadCache(settings.CollectionKey)!;
        Mock? startupAnki = null;
        var changedFields = false;
        var offline = false;
        var startupReader = new CollectionReader(startupTemp, () => new AnkiConnectClient(handler:
            startupAnki = new Mock { ChangedFields = changedFields, Unavailable = offline }));
        check(await startupReader.TryFastRefresh(settings, null, includeMedia: false) is null && startupAnki is null,
            "startup without a baseline skips AnkiConnect and the file snapshot");
        var startup = await startupReader.TryFastRefresh(settings, startupCache, includeMedia: false);
        check(startup?.Stats.IsIncremental == true && startupAnki is not null
            && !startupAnki.Actions.Any(a => a is "cardsInfo" or "notesInfo" or "getReviewsOfCards"),
            "startup with a saved live index requests only fast checks even when the source file is readable");
        changedFields = true;
        check(await startupReader.TryFastRefresh(settings, startupCache, includeMedia: false) is null
            && !startupAnki!.Actions.Any(a => a is "cardsInfo" or "notesInfo" or "getReviewsOfCards"),
            "startup with incompatible fields skips a full rebuild");
        offline = true;
        check(await startupReader.TryFastRefresh(settings, startupCache, includeMedia: false) is null && !Directory.Exists(startupTemp),
            "startup with AnkiConnect offline retains the cache without taking a file snapshot");
        startupCache.LiveIndex = startupCache.LiveIndex! with { ReadStartedAt = DateTimeOffset.UtcNow.AddDays(-29) };
        check(await startupReader.TryFastRefresh(settings, startupCache, includeMedia: false) is null && startupAnki!.Actions.Count == 0,
            "startup with an expired live index skips AnkiConnect reads");

        var offlineProfile = Path.Combine(sandbox, "reader-offline"); Directory.CreateDirectory(offlineProfile);
        var path = Path.Combine(offlineProfile, "collection.anki2"); File.Copy(fixture, path);
        var connections = 0;
        var access = new CollectionReader(Path.Combine(sandbox, "reader-temp"), () => { connections++; return new AnkiConnectClient(handler: new Mock()); });
        await access.Discover(offlineProfile);
        check(connections == 0, "closed collection discovery does not require AnkiConnect");
        using (var locked = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            locked.Open(); using var command = locked.CreateCommand(); command.CommandText = "BEGIN EXCLUSIVE"; command.ExecuteNonQuery();
            // The live profile must match the folder; use a mock that rejects it to
            // prove locked-file fallback cannot silently import a different profile.
            var rejected = false;
            try { await access.Discover(offlineProfile); } catch (InvalidOperationException) { rejected = true; }
            check(connections == 1 && rejected, "exclusive-lock fallback verifies running profile before importing");
            var working = new CollectionReader(Path.Combine(sandbox, "working-reader-temp"), () => new AnkiConnectClient(handler: new Mock { Profile = "reader-offline" }));
            check((await working.Discover(offlineProfile)).Schema == 0, "exclusive-lock fallback successfully reads through the matching running Anki");
            var isolated = new CollectionReader(Path.Combine(sandbox, "isolated-reader-temp"), () => throw new Exception("Isolated session contacted Anki"), allowLive: false);
            var isolatedRejected = false;
            try { await isolated.Discover(offlineProfile); } catch (IOException) { isolatedRejected = true; }
            check(isolatedRejected, "isolated imports never fall back to the user's running Anki");
            command.CommandText = "ROLLBACK"; command.ExecuteNonQuery();
        }
    }
}
