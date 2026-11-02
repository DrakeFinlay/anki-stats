using AnkiStats.Core;

internal static class ProfileChecks
{
    public static void Run(Action<bool, string> check, string sandbox)
    {
        var metadata = new CollectionMetadata(18,
            [new(1, "日本語", 2), new(2, "日本語::Later", 1), new(3, "漢字", 1), new(4, "English", 1)], []);
        var settings = new Settings { ProfileFolder = sandbox, DeckRules = [new("日本語", true), new("日本語::Later", false), new("English", false)] };
        DeckProfiles.Ensure(settings, metadata);
        check(settings.Profiles.Select(p => p.Name).SequenceEqual(new[] { "日本語", "漢字", "日本語 + 漢字" }), "migration creates three requested profiles");
        check(settings.Profiles.Single(p => p.Id == settings.ActiveProfileId).Name == "日本語", "migration preserves active deck selection");
        check(settings.Profiles.All(p => !p.Includes("日本語::Later")), "migration preserves descendant exclusions");
        check(settings.Includes("漢字") && !settings.Includes("English"), "import includes profile union without unrelated decks");
        var ids = settings.Profiles.Select(p => p.Id).ToList();
        DeckProfiles.Ensure(settings, metadata);
        check(ids.SequenceEqual(settings.Profiles.Select(p => p.Id)), "migration is idempotent and retains profile identities");
        DeckProfiles.Validate(settings, metadata);

        long Stamp(int day, int hour = 12) => new DateTimeOffset(2026, 10, day, hour, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var source = new ImportedCollection { CollectionKey = "profiles", Signature = settings.Signature, TimeZoneId = "UTC", Metadata = metadata,
            Profiles = DeckProfiles.Copy(settings.Profiles), UnresolvedReviews = 2,
            Notes = [new(1, "shared", "", "", "Vocabulary", "Word"), new(2, "字", "", "", "Kanji", "Kanji"), new(3, "excluded", "", "", "Vocabulary", "Word")],
            Cards = [new(1, 1, "日本語", 2, 2, 0), new(2, 1, "漢字", 2, 2, 0), new(3, 2, "漢字", 2, 2, 0), new(4, 3, "日本語::Later", 2, 2, 0), new(5, 3, "English", 2, 2, 0)],
            Reviews = [new(Stamp(1), 1, 3, 1000, 1), new(Stamp(2), 3, 3, 1000, 0), new(Stamp(3), 2, 3, 1000, 2), new(Stamp(3) + 1, 1, 3, 1000, 3),
                new(Stamp(2) + 1, 4, 3, 1000, 1), new(Stamp(2) + 2, 5, 3, 1000, 1), new(Stamp(3) + 2, 3, 0, 1000, 4)] };
        var vocabulary = DeckProfiles.Select(source, settings.Profiles[0]);
        var kanji = DeckProfiles.Select(source, settings.Profiles[1]);
        var combined = DeckProfiles.Select(source, settings.Profiles[2]);
        var now = new DateTimeOffset(2026, 10, 3, 20, 0, 0, TimeSpan.Zero);
        check(vocabulary.Reviews.Count == 2 && vocabulary.Cards.Count == 1, "profile scope follows included cards and excludes child subtree");
        check(kanji.Cards.Count == 2 && kanji.Reviews.Count == 3, "second profile has independent cards and reviews");
        check(combined.Cards.Count == 3 && combined.Reviews.Count == 5, "combined profile selects union exactly once");
        var analysis = Analyzer.Analyze(combined, kind: null, clock: now);
        check(analysis.TotalAnswers == 4 && analysis.Daily.Single(d => d.Day.Day == 3).Answers == 2, "combined daily volume sums both decks and excludes administration");
        check(analysis.Vocabulary.Count == 1 && analysis.Vocabulary[0].Cards == 2 && combined.Notes.Count == 2, "shared notes deduplicate and kanji stays out of vocabulary count");
        check(TimeAnalyzer.Analyze(combined, clock: now).AllHistory.Milliseconds == 4000 && TimeAnalyzer.Analyze(vocabulary, clock: now).AllHistory.Milliseconds == 2000, "recorded time follows active profile and combined totals do not duplicate reviews");
        check(Streaks.Analyze(vocabulary, now).Count == 2, "individual profile has separate streak gaps");
        var streak = Streaks.Analyze(combined, now).Single();
        check(streak.Length == 3 && streak.Answers == 4 && streak.IsCurrent, "either deck keeps combined streak active and answer totals include both");
        check(source.Cards.Count == 5 && source.Reviews.Count == 7, "selecting profiles does not mutate imported union");
        settings.Profiles[2].Enabled = false;
        check(settings.Includes("漢字"), "disabled charts do not remove saved profile data from import");
        var signature = settings.Signature;
        settings.ActiveProfileId = settings.Profiles[1].Id;
        check(settings.Signature == signature, "switching active profile does not invalidate import or preview");
        settings.Profiles[0].DeckRules.Add(new("日本語::Later", true));
        check(settings.Signature != signature, "changing profile rules invalidates preview signature");
        var store = new LocalStore(Path.Combine(sandbox, "profiles-store"));
        store.SaveCache(source, settings);
        var cached = store.LoadCache("profiles")!;
        check(cached.Profiles.Count == 3 && !cached.Profiles[0].Includes("日本語::Later"), "cache retains original profile definitions when settings change");
        var saved = store.LoadSettings()!;
        check(saved.Profiles.Count == 3 && !saved.Profiles[2].Enabled && saved.ActiveProfileId == settings.Profiles[1].Id, "profiles, enabled states and active selection survive restart");
        check(store.LoadCache("other") is null, "profile cache cannot leak across source collections");
        var odd = new Settings { DeckRules = [new("English", true)] };
        DeckProfiles.Ensure(odd, metadata);
        check(odd.Profiles.Count == 4 && odd.Profiles.Single(p => p.Id == odd.ActiveProfileId).Includes("English"), "migration retains an unrelated custom legacy selection");
        var invalid = DeckProfiles.Copy(settings);
        invalid.Profiles.ForEach(p => p.Enabled = false);
        try { DeckProfiles.Validate(invalid, metadata); check(false, "all disabled rejected"); } catch (InvalidDataException) { check(true, "all disabled rejected"); }
        invalid = DeckProfiles.Copy(settings);
        invalid.Profiles[1].Name = invalid.Profiles[0].Name;
        try { DeckProfiles.Validate(invalid, metadata); check(false, "duplicate names rejected"); } catch (InvalidDataException) { check(true, "duplicate names rejected"); }
    }
}
