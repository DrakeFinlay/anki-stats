using System.Net;
using System.Text;
using System.Text.Json;
using AnkiStats.Core;

internal static class NoteCorrectionChecks
{
    private const string Wrong = "影響力[えいきょうちょく]", Correct = "影響力[えいきょうりょく]";
    private sealed class MockAnki : HttpMessageHandler
    {
        public string Profile = "CorrectionFixture";
        public bool Japanese = true, NoOp, LoseResponse;
        public int Writes;
        public List<string> Names = ["Front", "Reading", "English", "Sentence", "Media"];
        public Dictionary<string, string> Fields = new() { ["Front"] = "<b>" + Wrong + "</b>[sound:keep.mp3]", ["Reading"] = "",
            ["English"] = "my meaning", ["Sentence"] = "my original sentence", ["Media"] = "original media" };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsoluteUri != "http://127.0.0.1:8765/") throw new Exception("Nonlocal request");
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var action = document.RootElement.GetProperty("action").GetString(); var args = document.RootElement.GetProperty("params");
            object? result;
            switch (action)
            {
                case "getActiveProfile": result = Profile; break;
                case "modelNamesAndIds": result = new Dictionary<string, long> { ["Japanese"] = 7 }; break;
                case "modelFieldNames": result = Names; break;
                case "getDecks": result = new Dictionary<string, long[]> { [Japanese ? "日本語::永遠" : "English"] = [11] }; break;
                case "notesInfo": result = new[] { new { noteId = 1, modelName = "Japanese", cards = new[] { 11 },
                    fields = Fields.ToDictionary(p => p.Key, p => new { value = p.Value, order = Names.IndexOf(p.Key) }) } }; break;
                case "updateNoteFields":
                    Writes++;
                    if (!NoOp) foreach (var field in args.GetProperty("note").GetProperty("fields").EnumerateObject()) Fields[field.Name] = field.Value.GetString()!;
                    if (LoseResponse) throw new HttpRequestException("Synthetic lost acknowledgement");
                    result = null; break;
                default: throw new Exception("Unexpected action " + action);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { result, error = (string?)null }), Encoding.UTF8, "application/json") };
        }
    }
    public static void Run(Action<bool, string> check, string sandbox) => RunAsync(check, sandbox).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check, string sandbox)
    {
        var settings = new Settings { ProfileFolder = Path.Combine(sandbox, "CorrectionFixture"), Mappings = [new() { NoteTypeId = 7, Category = "Vocabulary", Expression = 0, Meaning = 2 }] };
        var note = new NoteRecord(1, Wrong, "", "my meaning", "Vocabulary", "Japanese", Wrong);
        var mock = new MockAnki(); using var client = new AnkiConnectClient(handler: mock);
        var source = new ImportedCollection { CollectionKey = settings.CollectionKey, SourceProfileFolder = settings.ProfileFolder,
            Notes = [note], Cards = [new(11, 1, "日本語::永遠", 2, 2, 0)], Metadata = new(18, [], [new(7, "Japanese", mock.Names.ToList())]) };
        var entry = new DictionaryEntry("90001", ["影響力"], [new("えいきょうりょく", ["*"])], "influence", true, [], new("影響力", "えいきょうりょく"));
        var proposal = AnkiNoteCorrection.Suggest(note, entry, false);
        check(proposal.Automatic && proposal.Expression == Correct, "ordinary vocabulary typo proposes the correct annotated expression");
        check(!AnkiNoteCorrection.Suggest(note with { Category = "Grammar", Expression = "の particle", SourceExpression = "の particle" }, entry, false).Automatic
            && !AnkiNoteCorrection.Suggest(note with { Expression = "Short form verb + の", SourceExpression = "Short form verb + の" }, entry, false).Automatic,
            "grammar and descriptive vocabulary fronts never get automatic replacement proposals");
        check(!AnkiNoteCorrection.Suggest(note with { SourceExpression = "影響力[えいきょうりょく・えいきょうちょく]" }, entry, false).Automatic,
            "explicit reading alternatives are not silently collapsed by an automatic correction");
        var plan = await AnkiNoteCorrection.Prepare(client, source, settings, note, proposal.Expression, proposal.Reading);
        check(mock.Writes == 0 && plan.Changes.Count == 1 && plan.Changes[0].After == "<b>" + Correct + "</b>[sound:keep.mp3]",
            "correction preparation only reads and preserves formatting and audio markers");
        await AnkiNoteCorrection.Apply(client, plan);
        check(mock.Writes == 1 && mock.Fields["Front"].Contains(Correct) && mock.Fields["English"] == "my meaning"
            && mock.Fields["Sentence"] == "my original sentence" && mock.Fields["Media"] == "original media",
            "explicit field correction preserves meaning, sentence and media without creating notes");
        check(await AnkiNoteCorrection.Inspect(client, plan) == "Applied", "read-only correction status recognizes applied fields");
        await AnkiNoteCorrection.Apply(client, plan.Reverse());
        check(mock.Fields["Front"] == "<b>" + Wrong + "</b>[sound:keep.mp3]" && await AnkiNoteCorrection.Inspect(client, plan) == "Not applied",
            "guarded Undo restores the exact original field value");
        mock.Fields["English"] = "edited after preview";
        try { await AnkiNoteCorrection.Apply(client, plan); check(false, "stale preview must fail"); }
        catch (InvalidOperationException) { check(mock.Writes == 2, "concurrent note edits block writes rather than overwrite the user's changes"); }
        mock.Fields["English"] = "my meaning"; mock.LoseResponse = true;
        await AnkiNoteCorrection.Apply(client, plan);
        check(mock.Writes == 3 && mock.Fields["Front"].Contains(Correct), "lost write acknowledgement is verified without resending the correction");
        mock.LoseResponse = false;
        try { await AnkiNoteCorrection.Prepare(client, source, settings, note, Correct, ""); check(false, "manually corrected stale source must fail"); }
        catch (InvalidOperationException) { check(mock.Writes == 3, "a manual Anki correction invalidates the old imported snapshot"); }
        var reverse = plan.Reverse(); mock.NoOp = true;
        try { await AnkiNoteCorrection.Apply(client, reverse); check(false, "no-op acknowledgement must fail"); }
        catch (IOException) { check(mock.Writes == 4, "silent no-op Anki writes are not marked successful"); }
        mock.NoOp = false; mock.Profile = "Other";
        try { await AnkiNoteCorrection.Apply(client, reverse); check(false, "wrong profile must fail"); }
        catch (InvalidOperationException) { check(mock.Writes == 4, "corrections reject a switched Anki profile"); }
        mock.Profile = "CorrectionFixture"; mock.Japanese = false;
        try { await AnkiNoteCorrection.Apply(client, reverse); check(false, "foreign deck must fail"); }
        catch (InvalidOperationException) { check(mock.Writes == 4, "corrections reject notes moved outside the Japanese deck tree"); }
        mock.Japanese = true;
        mock.Names.Add("New field");
        try { await AnkiNoteCorrection.Apply(client, reverse); check(false, "changed schema must fail"); }
        catch (InvalidOperationException) { check(mock.Writes == 4, "corrections reject changed live field schemas"); }
        mock.Names.Remove("New field");
        source.IsSynthetic = true;
        try { await AnkiNoteCorrection.Prepare(client, source, settings, note, Correct, ""); check(false, "synthetic correction must fail"); }
        catch (InvalidOperationException) { check(mock.Writes == 4, "synthetic sources never initiate correction writes"); }
        source.IsSynthetic = false;
        mock.Fields["Front"] = "影響力"; mock.Fields["Reading"] = "えいきょうちょく"; settings.Mappings[0].Reading = 1;
        var separate = note with { Expression = "影響力", SourceExpression = "影響力", Reading = "えいきょうちょく" };
        var separateProposal = AnkiNoteCorrection.Suggest(separate, entry, true);
        var separatePlan = await AnkiNoteCorrection.Prepare(client, source, settings, separate, separateProposal.Expression, separateProposal.Reading);
        check(separatePlan.Changes.Count == 1 && separatePlan.Changes[0].Field == "Reading" && separatePlan.AfterNote.Reading == "えいきょうりょく",
            "separate-reading models correct their mapped reading field without rewriting the expression or meaning");
        var store = new LocalStore(Path.Combine(sandbox, "correction-journal"));
        store.SaveCoverageDecisions<AnkiCorrectionLog>(source.CollectionKey, "note-corrections", [new("test", separatePlan, "Pending verification", DateTimeOffset.UtcNow)]);
        check(store.LoadCoverageDecisions<AnkiCorrectionLog>(source.CollectionKey, "note-corrections").Single().Receipt.Changes.Single().Before == "えいきょうちょく",
            "correction receipts persist for inspecting uncertain outcomes without retrying writes");
    }
}
