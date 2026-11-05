using System.Net;
using System.Text;
using System.Text.Json;
using AnkiStats.Core;

internal static class NoteCreationChecks
{
    private sealed class MockAnki : HttpMessageHandler
    {
        public string Profile = "Synthetic";
        public bool HasDeck = true;
        public long ModelId = 7;
        public bool LoseNextWrite;
        public string[] Fields = ["Front", "English", "Sentence", "Media"];
        public readonly List<string> Actions = [];
        public readonly List<LiveAnkiNote> Notes = [];
        public readonly HashSet<string> RejectFronts = [];
        public JsonElement? LastAdded;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri != "http://127.0.0.1:8765/") throw new Exception("Nonlocal Anki endpoint");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var action = body.RootElement.GetProperty("action").GetString()!;
            var p = body.RootElement.GetProperty("params"); Actions.Add(action);
            object? result = action switch
            {
                "version" => 6,
                "getActiveProfile" => Profile,
                "modelNamesAndIds" => new Dictionary<string, long> { ["Japanese"] = ModelId },
                "deckNames" => HasDeck ? new[] { NoteCreation.Destination } : [],
                "modelFieldNames" => Fields,
                "findNotes" => Notes.Select(n => n.Id).ToArray(),
                "notesInfo" => p.GetProperty("notes").EnumerateArray().Select(id => Notes.Single(n => n.Id == id.GetInt64()))
                    .Select(n => new { noteId = n.Id, modelName = n.Model, tags = n.Tags, fields = n.Fields.ToDictionary(f => f.Key, f => new { value = f.Value, order = 0 }) }).ToArray(),
                "canAddNotes" => p.GetProperty("notes").EnumerateArray().Select(n => !RejectFronts.Contains(n.GetProperty("fields").GetProperty("Front").GetString()!)
                    && !Notes.Any(existing => existing.Fields.GetValueOrDefault("Front") == n.GetProperty("fields").GetProperty("Front").GetString())).ToArray(),
                "addNote" => Add(p.GetProperty("note")),
                _ => throw new Exception("Unexpected Anki action: " + action)
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { result, error = (string?)null }), Encoding.UTF8, "application/json") };
        }
        private long Add(JsonElement note)
        {
            LastAdded = note.Clone(); var id = 10000 + Notes.Count;
            Notes.Add(new(id, "Japanese", note.GetProperty("fields").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetString()!),
                note.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToList()));
            if (LoseNextWrite) { LoseNextWrite = false; throw new HttpRequestException("Synthetic lost response after successful add"); }
            return id;
        }
    }
    public static void Run(Action<bool, string> check, string sandbox) => RunAsync(check, sandbox).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check, string sandbox)
    {
        DictionaryEntry Entry(int n, string tier, bool common = true) => new(n.ToString(), ["語" + (char)('一' + n)], [new("ご" + new string('あ', n), ["*"])], "synthetic meaning " + n, common, tier.Length == 0 ? [] : [tier], new("語" + (char)('一' + n), "ご" + new string('あ', n)));
        var reference = new CoverageReference(new("synthetic", "2026-11-02", "synthetic", 0, "Synthetic reference", FormSelectionVersion: 1),
            Enumerable.Range(1, 6).Select(n => Entry(n, "N5")).Concat(Enumerable.Range(7, 6).Select(n => Entry(n, "N4")))
                .Concat(new[] { Entry(13, "N3"), Entry(14, "N2"), Entry(15, "N1"), Entry(16, "") }).ToList());
        var settings = new Settings { ProfileFolder = Path.Combine(sandbox, "Synthetic"), Mappings = [new() { NoteTypeId = 7, Category = "Vocabulary", Expression = 0, Meaning = 1 }] };
        var imported = new ImportedCollection { CollectionKey = settings.CollectionKey, SourceProfileFolder = settings.ProfileFolder, Scope = "Synthetic scope",
            Metadata = new(18, [], [new(7, "Japanese", ["Front", "English", "Sentence", "Media"])]) };
        var matcher = new VocabularyMatcher(reference); var coverage = matcher.Analyze(imported);
        var mock = new MockAnki(); using var client = new AnkiConnectClient(handler: mock);
        var journal = new List<CreationAttempt>();
        var plan = await NoteCreation.Prepare(client, coverage, imported, settings, 10, journal, random: new Random(123));
        check(plan.Notes.Count == 10 && plan.Notes.Take(6).All(n => n.Tier == "N5") && plan.Notes.Skip(6).All(n => n.Tier == "N4"), "six N5 words spill over to four N4 words");
        check(!mock.Actions.Contains("addNote"), "preview never creates Anki notes");
        check(plan.Notes.Select(n => n.EntryId).Distinct().Count() == 10, "random selection contains no repeated entry IDs");
        var other = await NoteCreation.Prepare(client, coverage, imported, settings, 10, journal, random: new Random(456));
        check(!plan.Notes.Select(n => n.EntryId).SequenceEqual(other.Notes.Select(n => n.EntryId)), "selection shuffles within priority tiers");
        var saved = new LocalStore(Path.Combine(sandbox, "creation-journal"));
        void Persist(List<CreationAttempt> attempts) => saved.SaveCoverageDecisions(imported.CollectionKey, "creation", attempts);
        var result = await NoteCreation.Create(client, plan, coverage, imported, settings, journal, Persist);
        check(result.Added.Count == 10 && result.Error is null, "confirmed batch creates requested number of notes");
        check(mock.LastAdded!.Value.GetProperty("deckName").GetString() == NoteCreation.Destination, "all created notes use fixed study deck");
        var fields = mock.LastAdded.Value.GetProperty("fields");
        check(fields.GetProperty("Sentence").GetString() == "" && fields.GetProperty("Media").GetString() == "", "sentence and media remain blank");
        check(mock.LastAdded.Value.GetProperty("options").GetProperty("allowDuplicate").GetBoolean() == false, "Anki collection duplicate protection stays enabled");
        check(mock.Notes.All(n => n.Tags.Count == 0), "generated notes have no Anki tags");
        check(NoteCreation.RecordedEntries(mock.Notes).SetEquals(result.Added.Select(a => a.EntryId)), "hidden attribution preserves identities on tag-free notes");
        check(NoteCreation.RecordedEntries([new(987, "Japanese", [], [NoteCreation.IdentityTag + "123"])]).Contains("123"), "legacy identity tags remain readable");
        check(mock.LastAdded.Value.GetProperty("fields").GetProperty("English").GetString()!.Contains("CC BY-SA 4.0"), "generated definitions retain hidden source attribution");
        check(mock.Actions.All(a => a is "version" or "getActiveProfile" or "modelNamesAndIds" or "deckNames" or "modelFieldNames" or "findNotes" or "notesInfo" or "canAddNotes" or "addNote"), "creation never adjusts daily limits or existing cards");
        var remaining = await NoteCreation.Prepare(client, coverage, imported, settings, int.MaxValue, journal);
        check(remaining.Notes.Count == 6 && remaining.Notes.Select(n => n.Tier).SequenceEqual(new[] { "N4", "N4", "N3", "N2", "N1", "Common" }), "requests larger than remaining pool return remainder in priority order");
        var completed = await NoteCreation.Create(client, remaining, coverage, imported, settings, journal, Persist);
        check(completed.Added.Count == 6, "remaining eligible pool can be exhausted");
        var exhausted = false; try { await NoteCreation.Prepare(client, coverage, imported, settings, 1, journal); } catch (InvalidOperationException e) { exhausted = e.Message.Contains("No eligible"); }
        check(exhausted, "exhausted pool returns an explicit error");
        check(saved.LoadCoverageDecisions<CreationAttempt>(imported.CollectionKey, "creation").Count == 16, "successful entry identities persist across app restarts");
        var receipt = result.Added[0]; var generated = plan.Notes[0];
        var refreshed = new ImportedCollection { Notes = [new(receipt.NoteId!.Value, generated.Front, "", generated.Meaning, "Vocabulary", "Japanese")],
            Cards = [new(123, receipt.NoteId.Value, NoteCreation.Destination, 0, 0, 0)] };
        var identity = new MatchDecision(receipt.NoteId.Value, receipt.Fingerprint!, receipt.EntryId, Origin: "Generated");
        check(matcher.Analyze(refreshed, [identity]).Matches.Single().Status == "Generated", "created dictionary identity survives an ordinary imported-note analysis");
        refreshed.Notes[0] = refreshed.Notes[0] with { Meaning = "changed target meaning" };
        check(matcher.Analyze(refreshed, [identity]).Matches.Single().Status == "Stale decision", "editing a generated target requires matching review");
        var overlap = reference with { Entries = [Entry(20, "N5") with { Levels = ["N5", "N4"] }] };
        check(NoteCreation.Candidates(new VocabularyMatcher(overlap).Analyze(imported), new HashSet<string>()).Count() == 1, "overlapping JLPT and common memberships count once");
        var badCount = false; try { await NoteCreation.Prepare(client, coverage, imported, settings, 0, []); } catch (ArgumentOutOfRangeException) { badCount = true; }
        check(badCount, "zero count is rejected before selection");
        var wrong = new MockAnki { Profile = "Other" }; using var wrongClient = new AnkiConnectClient(handler: wrong);
        var wrongProfile = false; try { await NoteCreation.Prepare(wrongClient, coverage, imported, settings, 1, []); } catch (InvalidOperationException) { wrongProfile = true; }
        check(wrongProfile && !wrong.Actions.Contains("addNote"), "wrong active profile is rejected before writes");
        var missingDeck = new MockAnki { HasDeck = false }; using var missingDeckClient = new AnkiConnectClient(handler: missingDeck);
        var deckRejected = false; try { await NoteCreation.Prepare(missingDeckClient, coverage, imported, settings, 1, []); } catch (InvalidOperationException) { deckRejected = true; }
        check(deckRejected && !missingDeck.Actions.Contains("addNote"), "missing destination is rejected without creating another deck");
        var lost = new MockAnki { LoseNextWrite = true }; using var lostClient = new AnkiConnectClient(handler: lost);
        var lostJournal = new List<CreationAttempt>();
        var lostPlan = await NoteCreation.Prepare(lostClient, coverage, imported, settings, 2, lostJournal);
        var uncertain = await NoteCreation.Create(lostClient, lostPlan, coverage, imported, settings, lostJournal, _ => { });
        check(uncertain.Error is not null && lostJournal.Single().State == "Pending" && lost.Notes.Count == 1, "lost write response stops the batch and keeps a pending identity");
        var next = await NoteCreation.Prepare(lostClient, coverage, imported, settings, 1, lostJournal);
        check(next.Notes[0].EntryId != lostPlan.Notes[0].EntryId, "uncertain write cannot be selected again");
        var before = lost.Actions.Count(a => a == "addNote");
        await NoteCreation.Reconcile(lostClient, "Synthetic", 7, lostJournal, _ => { });
        check(lostJournal.Single().State == "Added" && lost.Actions.Count(a => a == "addNote") == before && lost.Notes.Single().Tags.Count == 0, "pending reconciliation acknowledges tag-free note through hidden attribution without writing");
        var changed = new MockAnki(); using var changedClient = new AnkiConnectClient(handler: changed);
        var changedPlan = await NoteCreation.Prepare(changedClient, coverage, imported, settings, 1, []);
        changed.Profile = "Other";
        var stopped = await NoteCreation.Create(changedClient, changedPlan, coverage, imported, settings, [], _ => { });
        check(stopped.Added.Count == 0 && stopped.Error is not null && !changed.Actions.Contains("addNote"), "profile change after preview prevents all writes");
        var disk = new MockAnki(); using var diskClient = new AnkiConnectClient(handler: disk);
        var diskPlan = await NoteCreation.Prepare(diskClient, coverage, imported, settings, 1, []);
        var diskResult = await NoteCreation.Create(diskClient, diskPlan, coverage, imported, settings, [], _ => throw new IOException("Synthetic journal failure"));
        check(diskResult.Error is not null && !disk.Actions.Contains("addNote"), "journal must persist before any creation request");
        var racing = new MockAnki(); using var racingClient = new AnkiConnectClient(handler: racing);
        var racePlan = await NoteCreation.Prepare(racingClient, coverage, imported, settings, 1, []);
        racing.Notes.Add(new(888, "Japanese", new() { ["Front"] = racePlan.Notes[0].Front, ["English"] = racePlan.Notes[0].Meaning }, []));
        var raceResult = await NoteCreation.Create(racingClient, racePlan, coverage, imported, settings, [], _ => { });
        check(raceResult.Added.Count == 0 && raceResult.Skipped == 1 && !racing.Actions.Contains("addNote"), "a note added after preview is detected before creation");
        var thanks = new DictionaryEntry("9001", ["有難う"], [new("ありがとう", ["*"])], "thank you; thanks", true, ["N5"], new("ありがとう", "ありがとう"));
        var thanksRef = reference with { Entries = [thanks] };
        var variantInventory = new AnkiInventory([new(777, "Japanese", new() { ["Front"] = "ありがとう", ["English"] = "thanks" }, [])], ["Front", "English"]);
        check(NoteCreation.LiveExclusions(new(thanksRef), variantInventory, imported, settings).Contains("9001"), "live kana variant blocks duplicate kanji-entry creation across decks");
        var ambiguousRef = reference with { Entries = [thanks with { Id = "9002", Spellings = ["生"], Readings = [new("せい", ["*"])] }, thanks with { Id = "9003", Spellings = ["制"], Readings = [new("せい", ["*"])] }] };
        var ambiguousInventory = new AnkiInventory([new(776, "Japanese", new() { ["Front"] = "せい", ["English"] = "" }, [])], ["Front", "English"]);
        check(NoteCreation.LiveExclusions(new(ambiguousRef), ambiguousInventory, imported, settings).IsSupersetOf(new[] { "9002", "9003" }), "ambiguous live notes prevent automatic possible-duplicate creation");
        var stale = coverage with { Reference = reference with { Metadata = reference.Metadata with { FormSelectionVersion = 0 } } };
        var needsUpgrade = false; try { await NoteCreation.Prepare(client, stale, imported, settings, 1, []); } catch (InvalidOperationException) { needsUpgrade = true; }
        check(needsUpgrade, "legacy references cannot silently choose a rare written form");
        var payload = JsonSerializer.SerializeToElement(NoteCreation.Payload(new("99", "N5", "ありがとう", "ありがとう", "thanks <script>", "ありがとう"), ["Front", "English", "Sentence", "Media"]));
        check(payload.GetProperty("fields").GetProperty("Front").GetString() == "ありがとう" && payload.GetProperty("fields").GetProperty("English").GetString()!.Contains("&lt;script&gt;"), "preferred kana stays kana and dictionary HTML is encoded");
        var mediaReference = reference with { Entries = [.. reference.Entries, Entry(17, "", false)] };
        var mediaCoverage = new VocabularyMatcher(mediaReference).Analyze(imported);
        var mediaMock = new MockAnki(); using var mediaClient = new AnkiConnectClient(handler: mediaMock);
        mediaMock.Notes.Add(new(999, "Japanese", new() { ["Front"] = Entry(1, "N5").StudyForm!.Written, ["English"] = Entry(1, "N5").Meaning }, []));
        MediaMiningWord MiningRow(string id, string sentence = "") => new(id, "Ignored display", "", "Ignored display definition", "", 2, 20, "Completed A · Completed B", id != "17", sentence, sentence.Length > 0 ? "Local script" : "");
        var mediaPlan = await NoteCreation.PrepareMedia(mediaClient, mediaCoverage, imported, settings,
            [MiningRow("1"), MiningRow("17", "本当の文 <引用>。"), MiningRow("17"), MiningRow("2")], 2, []);
        check(mediaPlan.Notes.Select(n => n.EntryId).SequenceEqual(["17", "2"]) && !mediaMock.Actions.Contains("addNote"),
            "media preview accepts uncommon entries in media ranking order, deduplicates and skips live Anki entries without writing");
        check(mediaPlan.Notes[0].Meaning == Entry(17, "", false).Meaning && mediaPlan.Notes[0].Sentence == "本当の文 <引用>。" && mediaPlan.Notes[1].Sentence == "",
            "media preparation uses reference definitions and preserves real versus missing sentences");
        var wordListCorpus = new MediaCorpus("selected-title", "Selected VN", 7, "Synthetic", "Local text", "", "Synthetic", "hash", "test", "test",
            DateTimeOffset.UtcNow, null, [new("1", "word 1", "", 1, "Resolved"), new("2", "unselected", "", 1, "Resolved"),
                new("17", "word 17", "", 1, "Resolved", Sentence: "本当の文。")], MediaShelves.Planned);
        var wordListChoices = MediaMining.Selected(wordListCorpus, mediaCoverage, ["1", "17"]);
        var wordListPlan = await NoteCreation.PrepareMedia(mediaClient, mediaCoverage, imported, settings, wordListChoices, wordListChoices.Count, []);
        check(wordListPlan.Notes.Count == 1 && wordListPlan.Notes[0].EntryId == "17" && wordListPlan.Notes[0].Media == "Selected VN"
            && wordListPlan.Notes[0].Sentence == "本当の文。" && !mediaMock.Actions.Contains("addNote"),
            "individual word-list preview skips a live duplicate without substituting unselected entries or creating notes");
        var mediaJournal = new List<CreationAttempt>();
        var mediaResult = await NoteCreation.Create(mediaClient, mediaPlan, mediaCoverage, imported, settings, mediaJournal, _ => { });
        var mediaNote = mediaMock.Notes.Single(n => n.Id == mediaResult.Added[0].NoteId);
        check(mediaResult.Added.Count == 2 && mediaNote.Fields["Sentence"] == "本当の文 &lt;引用&gt;。" && mediaNote.Fields["Media"] == "Local script" && mediaNote.Tags.Count == 0,
            "media creation reuses safe writes, HTML-encodes sentences, stores source title and retains tag-free notes");
        var mediaRetry = false;
        try { await NoteCreation.PrepareMedia(mediaClient, mediaCoverage, imported, settings, [MiningRow("17")], 1, mediaJournal); }
        catch (InvalidOperationException) { mediaRetry = true; }
        check(mediaRetry, "media-generated entries cannot be prepared again after successful creation");
        var noSentenceMock = new MockAnki { Fields = ["Front", "English"] }; using var noSentenceClient = new AnkiConnectClient(handler: noSentenceMock);
        var noSentencePlan = await NoteCreation.PrepareMedia(noSentenceClient, mediaCoverage, imported, settings, [MiningRow("17", "元の文。")], 1, []);
        check(noSentencePlan.Notes[0].Sentence == "" && noSentencePlan.Notes[0].Media == "",
            "media preview reflects blank extra fields when the live note type lacks Sentence and Media");
        var cancelledMock = new MockAnki(); using var cancelledClient = new AnkiConnectClient(handler: cancelledMock);
        using var cancelledToken = new CancellationTokenSource(); cancelledToken.Cancel();
        try { await NoteCreation.PrepareMedia(cancelledClient, mediaCoverage, imported, settings, [MiningRow("17")], 1, [], token: cancelledToken.Token); check(false, "cancelled media preparation should stop"); }
        catch (OperationCanceledException) { check(cancelledMock.Actions.Count == 0, "cancelled media preparation makes no Anki requests"); }
        using var raw = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"dictDate":"2026-11-02","words":[
              {"id":"9001","kanji":[{"text":"有難う","common":false}],"kana":[{"text":"ありがとう","common":true,"appliesToKanji":["*"]}],"sense":[{"misc":["uk"],"gloss":[{"lang":"eng","text":"thanks"}]}]},
              {"id":"9004","kanji":[{"text":"喰べる","common":false},{"text":"食べる","common":true}],"kana":[{"text":"くう","common":false,"appliesToKanji":["喰べる"]},{"text":"たべる","common":true,"appliesToKanji":["食べる"]}],"sense":[{"misc":[],"gloss":[{"lang":"eng","text":"eat"}]}]}
            ]}
            """));
        var preferred = CoverageReferenceStore.Build(raw, []);
        check(preferred.Metadata.FormSelectionVersion == 1 && preferred.Entries[0].StudyForm == new CardStudyForm("ありがとう", "ありがとう"), "reference builder preserves usually-kana spelling preference");
        check(preferred.Entries[1].StudyForm == new CardStudyForm("食べる", "たべる"), "reference builder chooses common written form with compatible reading");
    }
}
