using AnkiStats.Core;

internal static class MappingChecks
{
    public static CoverageReference Reference()
    {
        var reference = CoverageChecks.Reference();
        foreach (var (id, form, meaning) in new[] { ("90001", "の", "nominalizing particle"), ("90002", "は", "topic particle"),
            ("90003", "つもり", "intention; plan"), ("90004", "よ", "sentence ending particle"), ("90005", "と", "quotative particle") })
            reference.Entries.Add(new(id, [], [new(form, ["*"])], meaning, true, ["N5"], new(form, form)));
        return reference;
    }
    public static ImportedCollection Collection() => new()
    {
        CollectionKey = "synthetic-mappings", RefreshedAt = DateTimeOffset.UtcNow,
        Metadata = new(18, [], [new(9, "Grammar", ["Front", "Reading", "English"])]),
        Notes = [new(1, "の particle", "", "nominalizing particle", "Grammar", "Grammar"), new(2, "X のは Y", "", "topic nominalization", "Grammar", "Grammar"),
            new(3, "〜つもりだ", "", "intention; plan", "Vocabulary", "Word"), new(4, "Short form verb + の", "", "nominalizing particle", "Grammar", "Grammar"),
            new(5, "Sentence ending in 〜よ", "", "sentence ending particle", "Grammar", "Grammar"), new(6, "〜と", "", "quotative particle", "Grammar", "Grammar"),
            new(7, "食べる[たべる]", "", "eat", "Vocabulary", "Word"), new(8, "custom English label", "", "nominalization", "Grammar", "Grammar")],
        Cards = [new(11, 1, "日本語::永遠", 2, 2, 0), new(12, 1, "日本語::永遠", 2, 2, 0), new(21, 2, "日本語::勉強してる", 2, 2, 0),
            new(31, 3, "日本語::永遠", 2, 2, 0), new(41, 4, "日本語::永遠", 2, 2, 0), new(51, 5, "日本語::永遠", 2, 2, 0),
            new(61, 6, "日本語::永遠", 2, 2, 0), new(71, 7, "日本語::永遠", 2, 2, 0), new(81, 8, "日本語::永遠", 2, 2, 0)]
    };
    public static void Run(Action<bool, string> check, string sandbox)
    {
        var reference = Reference(); var matcher = new VocabularyMatcher(reference); var source = Collection();
        var mixed = Collection();
        mixed.Notes.AddRange([new(201, "English-only fixture", "", "English", "Vocabulary", "Word"),
            new(202, "文法-only fixture", "", "grammar", "Grammar", "Grammar"), new(203, "lookalike root fixture", "", "grammar", "Grammar", "Grammar")]);
        mixed.Cards.AddRange([new(2011, 201, "English::Vocabulary", 2, 2, 0), new(2021, 202, "文法::Particles", 2, 2, 0),
            new(2031, 203, "日本語学習", 2, 2, 0), new(99, 1, "English::Sibling", 2, 2, 0)]);
        var japanese = MappingReview.JapaneseSource(mixed);
        check(japanese.Notes.Count == source.Notes.Count && japanese.Cards.Count == source.Cards.Count
            && japanese.Cards.All(c => c.Deck.StartsWith("日本語::", StringComparison.Ordinal)),
            "mapping queue excludes English, 文法, similarly named roots and outside sibling cards");
        var foreignProfile = new Settings { ProfileFolder = sandbox, ActiveProfileId = "english",
            Profiles = [new() { Id = "english", Name = "English", DeckRules = [new("English", true)] }] };
        var japaneseSettings = MappingReview.JapaneseSettings(foreignProfile);
        check(japaneseSettings.Includes("日本語") && japaneseSettings.Includes("日本語::文法") && !japaneseSettings.Includes("文法")
            && !japaneseSettings.Includes("English") && foreignProfile.ActiveProfileId == "english" && foreignProfile.Profiles.Count == 1,
            "mapping refresh always selects 日本語 descendants without changing the active profile or its rules");
        check(MappingReview.JapaneseSource(new ImportedCollection { Notes = mixed.Notes,
            Cards = mixed.Cards.Where(c => c.Deck.StartsWith("English", StringComparison.Ordinal)).ToList() }).Cards.Count == 0,
            "unavailable Japanese data never falls back to foreign decks");
        var analysis = matcher.Analyze(source);
        check(analysis.Database.SetEquals(["1000001"]), "grammar/label suggestions never automatically credit vocabulary entries");
        check(analysis.Matches.Single(m => m.NoteId == 1).ReviewEntryIds.Contains("90001") && analysis.Matches.Single(m => m.NoteId == 3).ReviewEntryIds.Contains("90003"),
            "particle labels and wave-prefixed grammar phrases produce review candidates");
        check(analysis.Matches.Single(m => m.NoteId == 2).ReviewEntryIds.IsSupersetOf(["90001", "90002"]), "pattern components are proposals requiring explicit approval");
        check(analysis.Matches.Single(m => m.NoteId == 4).ReviewEntryIds.Contains("90001") && analysis.Matches.Single(m => m.NoteId == 5).ReviewEntryIds.Contains("90004")
            && analysis.Matches.Single(m => m.NoteId == 6).ReviewEntryIds.Contains("90005"), "descriptive and placeholder grammar fronts preserve their target suggestions");
        var aliases = new[] { new MappingAlias { Expression = "custom English label", Lookup = "の", NoteType = "Grammar" } };
        var withAliases = matcher.Analyze(source, aliases: aliases);
        check(withAliases.Matches.Single(m => m.NoteId == 8).ReviewEntryIds.Contains("90001") && !withAliases.Database.Contains("90001"), "configured exact aliases suggest without confirming");
        check(MappingSuggestions.Find(matcher, "custom English label extra", "", "", aliases, "Grammar").Count == 0
            && MappingSuggestions.Find(matcher, "custom English label", "", "", aliases, "Other type").Count == 0, "aliases respect exact label and optional note-type scope");
        var decisions = new List<MatchDecision> { new(1, VocabularyMatcher.Fingerprint(source.Notes[0]), "90001"),
            new(2, VocabularyMatcher.Fingerprint(source.Notes[1]), "90001", AdditionalEntryIds: ["90002"]) };
        var confirmed = matcher.Analyze(source, decisions);
        check(confirmed.Database.IsSupersetOf(["90001", "90002"]) && confirmed.Known.Contains("90001") && confirmed.Familiar.Contains("90002"), "confirmed grammar links count toward Word Coverage with existing deck cohort rules");
        check(confirmed.Counts.Single(c => c.List == "N5").Database == 3, "multiple grammar notes and sibling cards do not multiply dictionary coverage");
        var edited = Collection(); edited.Notes[0] = edited.Notes[0] with { Expression = "の particle edited" };
        var stale = matcher.Analyze(edited, [decisions[0]]);
        check(!stale.Database.Contains("90001") && stale.Matches.Single(m => m.NoteId == 1).Status == "Stale decision", "edited source labels invalidate prior grammar approval");
        var deferred = new MatchDecision(7, VocabularyMatcher.Fingerprint(source.Notes[6]), null, Origin: "Deferred");
        var rejected = matcher.Analyze(source, [deferred]);
        check(!rejected.Database.Contains("1000001") && rejected.Matches.Single(m => m.NoteId == 7).ReviewEntryIds.Contains("1000001"), "manual cleanup removes credit while preserving possible-duplicate protection");
        var word = new MediaWord(null, "の", "の", 3, "Ambiguous", Sentence: "本物の文。");
        var corpus = new MediaCorpus("mapping-test", "Synthetic media", 7, "Synthetic", "Local text", "", "Synthetic", "hash1", "analyzer1", "ref1", DateTimeOffset.UtcNow, null, [word, word]);
        var mediaDecision = new MediaMappingDecision(corpus.Id, MappingReview.WordKey(word), MappingReview.SourceFingerprint(corpus, word), ["90001"]);
        var mapped = MappingReview.Apply(corpus, [mediaDecision], matcher.Entries);
        check(mapped.Words.All(w => w.EntryId == "90001") && corpus.Words.All(w => w.EntryId is null), "source mappings apply without overwriting the raw corpus");
        check(MappingReview.Apply(corpus with { ContentHash = "changed" }, [mediaDecision], matcher.Entries).Words.All(w => w.EntryId is null), "changed scripts invalidate saved source mappings");
        var ignored = MappingReview.Apply(corpus, [mediaDecision with { Exclude = true, EntryIds = [] }], matcher.Entries);
        check(MediaAnalysis.Compare(ignored, analysis).Unresolved == 0 && ignored.Words.All(w => w.EntryId is null), "ignored source forms leave unresolved counts without inventing dictionary identities");
        var items = MappingReview.Items(source, analysis, [corpus], [], matcher, aliases);
        check(items.Single(i => i.Key == "note:1").Cards == 2 && items.Count(i => i.Kind == "Media") == 1, "dashboard preserves note/card units and groups source forms within titles");
        check(MappingReview.Items(source, confirmed, [corpus], [mediaDecision], matcher, aliases).Any(i => i.Kind == "Media" && i.Resolved), "confirmed source mappings remain inspectable for reset");
        var completed = mapped with { State = MediaShelves.Completed };
        check(MediaAnalysis.Compare(completed, analysis).Words.Single().Status == "Needs review"
            && MediaAnalysis.Compare(completed, confirmed).Words.Single().Status == "In Anki"
            && !MediaMining.Missing([completed], confirmed, 1, true).Any(w => w.EntryId == "90001"), "confirmed grammar links change uncertain media presence to In Anki and prevent duplicate mining");
        check(MappingReview.Apply(corpus, [mediaDecision with { EntryIds = ["not-installed"] }], matcher.Entries).Words.All(w => w.EntryId is null),
            "removed dictionary identities never keep a source mapping confirmed");
        var ignoredAnalysis = matcher.Analyze(source, [decisions[0] with { Exclude = true, EntryId = null }]);
        check(!ignoredAnalysis.Database.Contains("90001") && ignoredAnalysis.Matches.Single(m => m.NoteId == 1).ReviewEntryIds.Count == 0,
            "ignored grammar notes neither credit coverage nor block missing-word creation by themselves");
        var settings = new Settings { Mappings = [new() { NoteTypeId = 9, Category = "Grammar", Expression = 0, Reading = 1, Meaning = 2 }] };
        var inventory = new AnkiInventory([new(1, "Grammar", new() { ["Front"] = "の particle", ["Reading"] = "", ["English"] = "nominalizing particle" }, [])], ["Front", "English"]);
        check(NoteCreation.LiveExclusions(matcher, inventory, source, settings, [decisions[0]]).Contains("90001"), "live duplicate checks honor confirmed grammar links across decks");
        var store = new LocalStore(Path.Combine(sandbox, "mapping-state"));
        store.SaveMappingState("profile-a", decisions, [mediaDecision], [new("note:7", deferred.Fingerprint)]);
        check(store.LoadCoverageDecisions<MatchDecision>("profile-a", "matches").Count == 2 && store.LoadCoverageDecisions<MediaMappingDecision>("profile-a", "media-mappings").Count == 1
            && store.LoadCoverageDecisions<MappingDeferred>("profile-a", "mapping-cleanup").Count == 1, "mapping state saves note links, source links and cleanup records together");
        check(store.LoadCoverageDecisions<MatchDecision>("profile-b", "matches").Count == 0, "mapping decisions remain isolated by collection");
    }
    private static bool IsSupersetOf<T>(this IEnumerable<T> values, IEnumerable<T> expected) => values.ToHashSet().IsSupersetOf(expected);
}
