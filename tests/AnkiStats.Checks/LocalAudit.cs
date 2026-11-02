using System.Security.Cryptography;
using AnkiStats.Core;

internal static class LocalAudit
{
    public static void Run(string profile, string deckRoot, bool connect)
    {
        var temp = Path.Combine(Path.GetTempPath(), "AnkiStatsAudit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(profile,"collection.anki2");
            var hash = SHA256.HashData(File.ReadAllBytes(source));
            var importer = new CollectionImporter(temp);
            var meta = importer.Discover(profile);
            var settings = new Settings { ProfileFolder=profile, DeckRules=[new(deckRoot,true)],
                Mappings=meta.NoteTypes.Select(t => new FieldMapping { NoteTypeId=t.Id, Category=t.Name.Contains("Conjug",StringComparison.OrdinalIgnoreCase)?"Grammar":t.Name.Contains("Kanji",StringComparison.OrdinalIgnoreCase)?"Kanji":"Vocabulary", Expression=0, ExpressionName=t.Fields[0], Meaning=t.Fields.FindIndex(f => f is "English" or "Meaning" or "Back"), MeaningName=t.Fields.FirstOrDefault(f=>f is "English" or "Meaning" or "Back")??"" }).ToList() };
            var data = importer.Import(settings);
            var all = Analyzer.Analyze(data,kind:null);
            var ordinary = Analyzer.Analyze(data);
            Console.WriteLine($"Included cards: {data.Cards.Count:N0}; notes: {data.Notes.Count:N0}; distinct entries: {all.Vocabulary.Count:N0}; skipped: {all.Skipped.Count:N0}");
            Console.WriteLine($"All rated answers: {all.TotalAnswers:N0}; ordinary review answers: {ordinary.TotalAnswers:N0}; learning/relearning/filtered difference: {all.TotalAnswers-ordinary.TotalAnswers:N0}");
            foreach(var g in data.Notes.GroupBy(n=>n.NoteType)) Console.WriteLine($"Included note type: {g.Key} · {g.Count():N0}");
            foreach(var g in data.Reviews.GroupBy(r=>r.Kind)) Console.WriteLine($"Event kind {g.Key}: {g.Count():N0}");
            Console.WriteLine($"Active dates (all rated answers): {all.Daily.Count:N0}; ordinary review dates: {ordinary.Daily.Count:N0}");
            if(!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source)))) throw new Exception("Source integrity check failed.");
            Console.WriteLine("Source hash unchanged. No card contents emitted.");
            if(connect)
            {
                var usedTypes = data.Notes.Select(n=>n.NoteType).ToHashSet();
                foreach(var m in settings.Mappings) if(!usedTypes.Contains(meta.NoteTypes.Single(t=>t.Id==m.NoteTypeId).Name)) m.Category="Ignored";
                data = importer.Import(settings);
                new LocalStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnkiStats")).SaveCache(data,settings);
                Console.WriteLine("Validated selection saved to normal per-user app storage.");
            }
        }
        finally { if(Directory.Exists(temp)) Directory.Delete(temp,true); }
    }
}
