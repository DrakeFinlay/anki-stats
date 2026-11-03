using System.Net;

namespace AnkiStats.Core;

public sealed record CreationAttempt(string EntryId, string State, long? NoteId = null, string? Fingerprint = null);
public sealed record NewVocabularyNote(string EntryId, string Tier, string Word, string Reading, string Meaning, string Front);
public sealed record CreationPlan(string Profile, long ModelId, List<string> Fields, List<NewVocabularyNote> Notes, int Requested, int Pending);
public sealed record CreationResult(List<CreationAttempt> Added, int Skipped, string? Error);

public static class NoteCreation
{
    public const string Destination = "日本語::勉強してる";
    public const string IdentityTag = "ankistats::jmdict::";
    public static readonly string[] Tiers = ["N5", "N4", "N3", "N2", "N1", "Common"];
    public static HashSet<string> TaggedEntries(IEnumerable<LiveAnkiNote> notes) => notes.SelectMany(n => n.Tags)
        .Where(t => t.StartsWith(IdentityTag, StringComparison.Ordinal)).Select(t => t[IdentityTag.Length..]).ToHashSet();

    public static HashSet<string> LiveExclusions(VocabularyMatcher matcher, AnkiInventory inventory, ImportedCollection imported, Settings settings)
    {
        var vocabularyTypes = settings.Mappings.Where(m => m.Category == "Vocabulary").Select(m =>
            (Mapping: m, Type: imported.Metadata.NoteTypes.Find(t => t.Id == m.NoteTypeId))).Where(p => p.Type is not null)
            .ToDictionary(p => p.Type!.Name);
        var source = new ImportedCollection();
        foreach (var note in inventory.Notes)
        {
            if (!vocabularyTypes.TryGetValue(note.Model, out var mapped)) continue;
            string Value(int index) => index < 0 || index >= mapped.Type!.Fields.Count ? "" : note.Fields.GetValueOrDefault(mapped.Type.Fields[index], "");
            source.Notes.Add(new(note.Id, TextNormalizer.Clean(Value(mapped.Mapping.Expression)), TextNormalizer.Clean(Value(mapped.Mapping.Reading)),
                TextNormalizer.Clean(Value(mapped.Mapping.Meaning)), "Vocabulary", note.Model));
            source.Cards.Add(new(note.Id, note.Id, "Live duplicate check", 0, 0, 0));
        }
        var live = matcher.Analyze(source);
        // An unresolved compatible entry must be reviewed instead of generating a potential duplicate.
        return live.Database.Concat(live.Matches.SelectMany(m => m.ReviewEntryIds)).Concat(TaggedEntries(inventory.Notes)).ToHashSet();
    }

    public static IEnumerable<NewVocabularyNote> Candidates(CoverageAnalysis coverage, ISet<string> excluded, Random? random = null)
    {
        random ??= Random.Shared;
        var blocked = coverage.Database.Concat(coverage.Matches.SelectMany(m => m.ReviewEntryIds)).Concat(excluded).ToHashSet();
        var used = new HashSet<string>();
        foreach (var tier in Tiers)
        {
            var entries = coverage.Reference.Entries.Where(e => !blocked.Contains(e.Id) && !used.Contains(e.Id)
                && (tier == "Common" ? e.Common : e.Levels.Contains(tier))).ToArray();
            random.Shuffle(entries);
            foreach (var entry in entries)
            {
                if (!used.Add(entry.Id) || entry.StudyForm is not {} form || string.IsNullOrWhiteSpace(entry.Meaning)) continue;
                var front = form.Written == form.Reading ? form.Written : form.Written + "[" + form.Reading + "]";
                var parsed = VocabularyExpression.Parse(new(0, front, "", entry.Meaning, "Vocabulary", "Japanese"));
                if (parsed.Error is not null || parsed.Readings.Count != 1 || VocabularyMatcher.Key(parsed.Readings[0]) != VocabularyMatcher.Key(form.Reading)) continue;
                yield return new(entry.Id, tier, form.Written, form.Reading, entry.Meaning, front);
            }
        }
    }
    public static object Payload(NewVocabularyNote note, IEnumerable<string> fieldNames)
    {
        var fields = fieldNames.ToDictionary(f => f, _ => "");
        fields["Front"] = WebUtility.HtmlEncode(note.Front);
        fields["English"] = WebUtility.HtmlEncode(note.Meaning)
            + "<!-- JMdict © EDRDG; https://www.edrdg.org/jmdict/j_jmdict.html ; CC BY-SA 4.0 https://creativecommons.org/licenses/by-sa/4.0/ ; entry " + note.EntryId + " -->";
        return new { deckName = Destination, modelName = "Japanese", fields,
            options = new { allowDuplicate = false, duplicateScope = "collection" }, tags = new[] { "ankistats::generated", IdentityTag + note.EntryId } };
    }
    public static async Task<CreationPlan> Prepare(AnkiConnectClient client, CoverageAnalysis coverage, ImportedCollection imported, Settings settings,
        int requested, List<CreationAttempt> journal, IProgress<string>? progress = null, Random? random = null)
    {
        if (requested <= 0) throw new ArgumentOutOfRangeException(nameof(requested), "Enter a positive number of words.");
        if (coverage.Reference.Metadata.FormSelectionVersion < 1) throw new InvalidOperationException("Update reference data to retain preferred study forms before creating notes.");
        var model = imported.Metadata.NoteTypes.Find(t => t.Name == "Japanese") ?? throw new InvalidOperationException("The imported collection has no Japanese note type.");
        var mapping = settings.Mappings.Find(m => m.NoteTypeId == model.Id && m.Category == "Vocabulary");
        if (mapping is null || model.Fields.ElementAtOrDefault(mapping.Expression) != "Front" || model.Fields.ElementAtOrDefault(mapping.Meaning) != "English")
            throw new InvalidOperationException("Map Japanese Front and English as vocabulary fields before creating notes.");
        var profile = Path.GetFileName(Path.TrimEndingDirectorySeparator(imported.SourceProfileFolder));
        var inventory = await AnkiConnection.ReadInventory(client, profile, model.Id, progress);
        var excluded = LiveExclusions(new VocabularyMatcher(coverage.Reference), inventory, imported, settings);
        excluded.UnionWith(journal.Where(a => a.State is "Added" or "Pending").Select(a => a.EntryId));
        var selected = new List<NewVocabularyNote>(); var fronts = new HashSet<string>();
        foreach (var candidate in Candidates(coverage, excluded, random))
        {
            if (!fronts.Add(candidate.Front)) continue;
            var allowed = await client.Call<bool[]>("canAddNotes", new { notes = new[] { Payload(candidate, inventory.ModelFields) } });
            if (allowed.Length != 1) throw new InvalidDataException("Anki returned an invalid duplicate-check response.");
            if (!allowed[0]) continue;
            selected.Add(candidate);
            if (selected.Count == requested) break;
        }
        if (selected.Count == 0) throw new InvalidOperationException("No eligible missing words remain in N5 → N4 → N3 → N2 → N1 → Common. Possible matches, duplicates and pending attempts are excluded. Check pending additions or review uncertain matches if needed.");
        return new(profile, model.Id, inventory.ModelFields, selected, requested, journal.Count(a => a.State == "Pending"));
    }
    public static async Task<CreationResult> Create(AnkiConnectClient client, CreationPlan plan, CoverageAnalysis coverage, ImportedCollection imported, Settings settings,
        List<CreationAttempt> journal, Action<List<CreationAttempt>> persist, IProgress<string>? progress = null)
    {
        var added = new List<CreationAttempt>(); var skipped = 0;
        try
        {
            var inventory = await AnkiConnection.ReadInventory(client, plan.Profile, plan.ModelId, progress);
            if (!inventory.ModelFields.SequenceEqual(plan.Fields)) throw new InvalidOperationException("Japanese fields changed after preview. Prepare a new batch.");
            var blocked = LiveExclusions(new VocabularyMatcher(coverage.Reference), inventory, imported, settings);
            blocked.UnionWith(journal.Where(a => a.State is "Pending" or "Added").Select(a => a.EntryId));
            foreach (var note in plan.Notes)
            {
                if (blocked.Contains(note.EntryId)) { skipped++; continue; }
                await AnkiConnection.VerifyProfile(client, plan.Profile);
                var payload = Payload(note, plan.Fields);
                var allowed = await client.Call<bool[]>("canAddNotes", new { notes = new[] { payload } });
                if (allowed.Length != 1) throw new InvalidDataException("Anki returned an invalid duplicate-check response.");
                if (!allowed[0]) { skipped++; continue; }
                journal.RemoveAll(a => a.EntryId == note.EntryId);
                var fingerprint = VocabularyMatcher.Fingerprint(new(0, note.Front, "", note.Meaning, "Vocabulary", "Japanese"));
                journal.Add(new(note.EntryId, "Pending", Fingerprint: fingerprint));
                persist(journal); // Persist before sending: never blindly retry a write whose response was lost.
                progress?.Report($"Creating note {added.Count + 1:N0} / {plan.Notes.Count:N0}…");
                var id = await client.Call<long?>("addNote", new { note = payload });
                if (id is null) { journal.RemoveAll(a => a.EntryId == note.EntryId); persist(journal); skipped++; continue; }
                if (id <= 0) throw new InvalidDataException("Anki returned an invalid created-note ID.");
                var receipt = new CreationAttempt(note.EntryId, "Added", id, fingerprint);
                added.Add(receipt); journal.RemoveAll(a => a.EntryId == note.EntryId); journal.Add(receipt); persist(journal);
                blocked.Add(note.EntryId);
            }
            return new(added, skipped, null);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return new(added, skipped, e.Message + " Creation stopped; successful notes remain in Anki. Use Check pending additions before retrying uncertain attempts.");
        }
    }
    public static async Task Reconcile(AnkiConnectClient client, string profile, long modelId, List<CreationAttempt> journal, Action<List<CreationAttempt>> persist)
    {
        // The live read runs through Anki after its earlier writes; no deletion or rollback is attempted.
        var inventory = await AnkiConnection.ReadInventory(client, profile, modelId);
        foreach (var attempt in journal.Where(a => a.State == "Pending").ToList())
        {
            var found = inventory.Notes.Where(n => n.Tags.Contains(IdentityTag + attempt.EntryId)).ToList();
            if (found.Count > 1) throw new InvalidOperationException("More than one note has a pending dictionary identity. Inspect it in Anki.");
            journal.Remove(attempt);
            if (found.Count == 1) journal.Add(new(attempt.EntryId, "Added", found[0].Id, attempt.Fingerprint));
        }
        persist(journal);
    }
}
