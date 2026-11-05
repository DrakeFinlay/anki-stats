using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnkiStats.Core;

public sealed record NoteCorrectionProposal(string Expression, string Reading, bool Automatic);
public sealed record NoteFieldChange(string Field, string Before, string After);
public sealed record AnkiCorrectionReceipt(string Profile, long ModelId, List<string> ModelFields, NoteRecord BeforeNote,
    NoteRecord AfterNote, List<NoteFieldChange> Changes, Dictionary<string, string> GuardFields)
{
    public AnkiCorrectionReceipt Reverse() => this with { BeforeNote = AfterNote, AfterNote = BeforeNote,
        Changes = Changes.Select(c => new NoteFieldChange(c.Field, c.After, c.Before)).ToList(),
        GuardFields = GuardFields.ToDictionary(p => p.Key, p => Changes.FirstOrDefault(c => c.Field == p.Key)?.After ?? p.Value) };
}
public sealed record AnkiCorrectionLog(string Id, AnkiCorrectionReceipt Receipt, string State, DateTimeOffset Time)
{
    public long NoteId => Receipt.BeforeNote.Id;
    public string Fields => string.Join(" · ", Receipt.Changes.Select(c => c.Field + ": " + c.Before + " → " + c.After));
}

/// <summary>Explicit field edits only; never opens an Anki database or retries a write.</summary>
public static class AnkiNoteCorrection
{
    public static NoteCorrectionProposal Suggest(NoteRecord note, DictionaryEntry entry, bool separateReading)
    {
        var parsed = VocabularyExpression.Parse(note);
        var source = note.SourceExpression.Length > 0 ? note.SourceExpression : note.Expression;
        var ordinary = note.Category == "Vocabulary" && parsed.Error is null && parsed.Readings.Count <= 1 && parsed.JoinedReading is null
            && Regex.IsMatch(parsed.Word, @"^[一-龯々〆ぁ-ゖァ-ヺー０-９0-9]+$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!ordinary) return new(source, note.Reading, false);
        var written = entry.Spellings.Contains(parsed.Word) || entry.Readings.Any(r => r.Text == parsed.Word)
            ? parsed.Word : entry.StudyForm?.Written ?? entry.Spellings.FirstOrDefault() ?? entry.Readings[0].Text;
        var readings = entry.Readings.Where(r => VocabularyMatcher.Key(written) == VocabularyMatcher.Key(r.Text)
            || entry.Spellings.Count == 0 || r.AppliesTo.Count == 0 || r.AppliesTo.Contains("*") || r.AppliesTo.Contains(written)).ToList();
        var reading = readings.FirstOrDefault(r => parsed.Readings.Contains(VocabularyMatcher.Key(r.Text)))?.Text
            ?? readings.FirstOrDefault(r => r.Text == entry.StudyForm?.Reading)?.Text ?? readings.FirstOrDefault()?.Text;
        if (reading is null) return new(source, note.Reading, false);
        var expression = (!separateReading || source.Contains('[')) && written != reading ? written + "[" + reading + "]" : written;
        return new(expression, separateReading ? reading : note.Reading, true);
    }

    // Preserve wrappers, sound markers and other markup when the original visible
    // value occurs in a single text run. Ambiguous/fragmented markup is not guessed.
    private static string ReplaceText(string raw, string oldText, string newText)
    {
        if (oldText == newText) return raw;
        if (raw == oldText) return WebUtility.HtmlEncode(newText);
        var encodedOld = WebUtility.HtmlEncode(oldText);
        var found = new List<(int Start, int Length)>();
        foreach (Match run in Regex.Matches(raw, @"(^|>)([^<]*)(?=<|$)", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            var text = run.Groups[2];
            foreach (var value in new[] { oldText, encodedOld }.Where(s => s.Length > 0).Distinct())
                for (var p = text.Value.IndexOf(value, StringComparison.Ordinal); p >= 0; p = text.Value.IndexOf(value, p + value.Length, StringComparison.Ordinal))
                    found.Add((text.Index + p, value.Length));
        }
        found = found.Distinct().ToList();
        if (found.Count != 1) throw new InvalidOperationException("This field has complex or ambiguous formatting. Correct it in Anki, refresh mappings, or confirm a local link only.");
        return raw[..found[0].Start] + WebUtility.HtmlEncode(newText) + raw[(found[0].Start + found[0].Length)..];
    }

    private static (NoteType Type, FieldMapping Map) Mapping(ImportedCollection source, Settings settings, NoteRecord expected)
    {
        if (source.IsSynthetic) throw new InvalidOperationException("Anki correction is disabled for synthetic data.");
        if (source.CollectionKey != settings.CollectionKey) throw new InvalidOperationException("The collection changed. Refresh mappings.");
        var type = source.Metadata.NoteTypes.Single(t => t.Name == expected.NoteType);
        var map = settings.Mappings.Single(m => m.NoteTypeId == type.Id && m.Category == expected.Category);
        if (map.Category is not ("Vocabulary" or "Grammar") || map.Expression < 0 || map.Expression >= type.Fields.Count
            || map.Reading >= type.Fields.Count || map.Meaning >= type.Fields.Count
            || map.Reading == map.Expression || map.Expression == map.Meaning || map.Reading >= 0 && map.Reading == map.Meaning)
            throw new InvalidOperationException("Map separate expression, reading and meaning fields before correcting notes.");
        return (type, map);
    }
    private static NoteRecord Project(long id, string model, string category, List<string> names, FieldMapping map, Dictionary<string, string> fields)
    {
        string Field(int index, bool strip = false) => index < 0 || index >= names.Count ? "" : TextNormalizer.Clean(fields[names[index]], strip);
        return new(id, Field(map.Expression, map.StripBracketReadings), Field(map.Reading), Field(map.Meaning), category, model, Field(map.Expression));
    }
    private static async Task<Dictionary<string, string>> Read(AnkiConnectClient client, string profile, long modelId, string model, List<string> fields, long noteId)
    {
        await AnkiConnection.VerifyProfile(client, profile);
        var models = await client.Call<Dictionary<string, long>>("modelNamesAndIds");
        if (!models.TryGetValue(model, out var actual) || actual != modelId
            || !(await client.Call<List<string>>("modelFieldNames", new { modelName = model })).SequenceEqual(fields))
            throw new InvalidOperationException("The Anki note type or field schema changed. Refresh mappings.");
        var info = await client.Call<JsonElement[]>("notesInfo", new { notes = new[] { noteId } });
        if (info.Length != 1 || !info[0].TryGetProperty("noteId", out var id) || id.GetInt64() != noteId || info[0].GetProperty("modelName").GetString() != model)
            throw new InvalidOperationException("The Anki note was removed or changed. Refresh mappings.");
        var cards = info[0].GetProperty("cards").EnumerateArray().Select(c => c.GetInt64()).ToList();
        var decks = await client.Call<Dictionary<string, List<long>>>("getDecks", new { cards });
        if (!decks.Keys.Any(d => d == MappingReview.JapaneseDeck || d.StartsWith(MappingReview.JapaneseDeck + "::", StringComparison.Ordinal)))
            throw new InvalidOperationException("This note no longer has a card in 日本語. Refresh mappings.");
        var values = info[0].GetProperty("fields").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetProperty("value").GetString() ?? "");
        if (!fields.All(values.ContainsKey)) throw new InvalidOperationException("Anki returned incomplete fields.");
        return values;
    }
    public static async Task<AnkiCorrectionReceipt> Prepare(AnkiConnectClient client, ImportedCollection source, Settings settings,
        NoteRecord expected, string expression, string reading)
    {
        var (type, map) = Mapping(source, settings, expected);
        if (string.IsNullOrWhiteSpace(expression)) throw new InvalidOperationException("The corrected expression cannot be blank.");
        var profile = Path.GetFileName(Path.TrimEndingDirectorySeparator(source.SourceProfileFolder));
        var live = await Read(client, profile, type.Id, type.Name, type.Fields, expected.Id);
        var before = Project(expected.Id, type.Name, map.Category, type.Fields, map, live);
        if (VocabularyMatcher.Fingerprint(before) != VocabularyMatcher.Fingerprint(expected))
            throw new InvalidOperationException("This note was edited since the snapshot. Refresh mappings before correcting it.");
        var changes = new List<NoteFieldChange>();
        void Change(int index, string oldText, string newText)
        {
            if (index < 0 || index >= type.Fields.Count) return;
            var name = type.Fields[index]; var updated = ReplaceText(live[name], oldText, newText);
            if (updated != live[name]) changes.Add(new(name, live[name], updated));
        }
        Change(map.Expression, before.SourceExpression, expression.Trim());
        Change(map.Reading, before.Reading, reading.Trim());
        var afterFields = new Dictionary<string, string>(live);
        foreach (var change in changes) afterFields[change.Field] = change.After;
        return new(profile, type.Id, type.Fields, before, Project(expected.Id, type.Name, map.Category, type.Fields, map, afterFields), changes, live);
    }
    public static async Task Apply(AnkiConnectClient client, AnkiCorrectionReceipt receipt)
    {
        if (receipt.Changes.Count == 0) return;
        var note = receipt.BeforeNote;
        var live = await Read(client, receipt.Profile, receipt.ModelId, note.NoteType, receipt.ModelFields, note.Id);
        if (receipt.GuardFields.Any(p => live.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidOperationException("Anki fields changed after the correction preview. Refresh before retrying; nothing was overwritten.");
        await AnkiConnection.VerifyProfile(client, receipt.Profile);
        Exception? writeError = null;
        try { await client.Call<JsonElement>("updateNoteFields", new { note = new { id = note.Id, fields = receipt.Changes.ToDictionary(c => c.Field, c => c.After) } }); }
        catch (Exception error) when (error is IOException or InvalidOperationException) { writeError = error; }
        // A lost acknowledgement or a no-op response must not be mistaken for a
        // successful correction. Read once; never automatically resend the write.
        var after = await Read(client, receipt.Profile, receipt.ModelId, note.NoteType, receipt.ModelFields, note.Id);
        if (receipt.Changes.Any(c => after[c.Field] != c.After))
            throw new IOException("The Anki correction could not be verified. Close this note in Anki's editor and refresh mappings to inspect its current fields before retrying.", writeError);
        if (receipt.GuardFields.Any(p => !receipt.Changes.Any(c => c.Field == p.Key) && after.GetValueOrDefault(p.Key) != p.Value))
            throw new IOException("The corrected fields were saved, but other note fields changed concurrently. Refresh mappings to read the complete current note.");
    }
    public static async Task<string> Inspect(AnkiConnectClient client, AnkiCorrectionReceipt receipt)
    {
        var fields = await Read(client, receipt.Profile, receipt.ModelId, receipt.BeforeNote.NoteType, receipt.ModelFields, receipt.BeforeNote.Id);
        return receipt.Changes.All(c => fields[c.Field] == c.After) ? "Applied"
            : receipt.Changes.All(c => fields[c.Field] == c.Before) ? "Not applied" : "Changed since preview";
    }
}
