using AnkiStats.Core;

internal static class DefinitionChecks
{
    public static CoverageReference Reference() => new(new("synthetic", "2026-11-02", "synthetic", 0, "Synthetic reference"),
        [
            new("4000001", [], [new("はい", ["*"]), new("へい", ["*"]), new("はーい", ["*"]), new("はーーい", ["*"])], "yes; that is correct; understood; okay", true, ["N5"]),
            new("4000002", ["灰"], [new("はい", ["*"])], "ash; ashes", true, []),
            new("4000003", ["肺"], [new("はい", ["*"])], "lung", true, []),
            new("4000004", ["有難う", "有り難う"], [new("ありがとう", ["*"])], "thank you; thanks", true, ["N5"]),
            new("4000005", ["対"], [new("たい", ["*"])], "pair; couple", false, []),
            new("4000006", ["双"], [new("たい", ["*"])], "pair; two matching things", false, [])
        ]);
    public static void Run(Action<bool, string> check)
    {
        var reference = Reference();
        var matcher = new VocabularyMatcher(reference);
        ImportedCollection Source(string expression, string meaning) => new()
        {
            Notes = [new(1, expression, "", meaning, "Vocabulary", "Synthetic")],
            Cards = [new(1, 1, "日本語::永遠", 2, 2, 0)]
        };
        CoverageAnalysis Analyze(string expression, string meaning) => matcher.Analyze(Source(expression, meaning));
        var yes = Analyze("はい", "yes");
        check(yes.Known.SetEquals(new[] { "4000001" }) && yes.Unresolved == 0, "definition resolves yes without crediting ash or lung homophones");
        check(yes.Matches.Single().Reason.Contains("English definition"), "definition-based decision explains its evidence");
        foreach (var form in new[] { "はい", "へい", "はーい", "はーーい" })
            check(Analyze(form, "yes").Known.SetEquals(yes.Known), "any single accepted reading suffices independently: " + form);
        check(Analyze("はい", "a lung").Known.SetEquals(new[] { "4000003" }), "definition selects a different homophone when supported");
        check(Analyze("はい", "yes; okay").Known.Count == 1, "multiple matching glosses never multiply entry coverage");
        check(Analyze("はい", "").Known.Count == 0, "missing definition does not resolve ambiguous kana");
        check(Analyze("はい", "unknown paraphrase").Known.Count == 0, "unsupported definition cannot guess a homophone");
        check(Analyze("たい", "pair").Known.Count == 0, "shared definitions do not break a genuine tie");
        var kana = Analyze("ありがとう", "thanks");
        var kanji = Analyze("有難う[ありがとう]", "thank you");
        check(kana.Known.SetEquals(kanji.Known) && kana.Known.Count == 1, "kana and uncommon kanji independently credit the same entry");
        var duplicates = Source("ありがとう", "thanks");
        duplicates.Notes.Add(new(2, "有難う[ありがとう]", "", "thank you", "Vocabulary", "Synthetic"));
        duplicates.Cards.Add(new(2, 2, "日本語::永遠", 2, 2, 0));
        check(matcher.Analyze(duplicates).Known.Count == 1, "having both orthographic variants still counts once");
        var mismatch = Analyze("灰[はい]", "yes");
        check(mismatch.DefinitionWarnings == 1 && mismatch.Matches.Single().NeedsReview, "unverified definition on unique Japanese form is visible for review");
        check(!mismatch.Known.Contains("4000001"), "English alone cannot override incompatible written Japanese");
        check(DefinitionMatcher.Score("To eat", "to eat; to dine") == 3, "gloss normalization supports case and infinitive markers");
        check(DefinitionMatcher.Score("little girl", "girl; daughter") > 0, "short descriptive modifiers permit supporting phrase evidence");
        check(DefinitionMatcher.Score("ash", "flash") == 0, "definition matching respects word boundaries");
        check(DefinitionMatcher.Score("not good", "good") == 0 && DefinitionMatcher.Score("(not) good", "good") == 0, "negation is not discarded as a qualifier");
        check(DefinitionMatcher.Score("doesn’t work", "work") == 0 && DefinitionMatcher.Score("can't work", "work") == 0, "contracted negation cannot support a positive gloss");
        check(DefinitionMatcher.Score("thing", "thing") == 0, "generic gloss alone cannot disambiguate entries");
        check(DefinitionMatcher.Score("bank (river)", "river bank") > DefinitionMatcher.Score("bank (river)", "bank (financial institution)"), "parenthetical qualifiers distinguish meanings rather than being discarded");
        check(DefinitionMatcher.Fingerprint("bank (river)") != DefinitionMatcher.Fingerprint("bank (financial institution)"), "saved definition fingerprint retains semantic qualifiers");
        var saved = new MatchDecision(1, yes.Matches.Single().Fingerprint, "4000001");
        check(matcher.Analyze(Source("はい", "lung"), [saved]).Matches.Single().Status == "Stale decision", "definition changes invalidate new saved corrections");
        var legacy = saved with { Fingerprint = "v2\u001fはい\u001fはい" };
        check(matcher.Analyze(Source("はい", "yes"), [legacy]).Matches.Single().Status == "Corrected", "existing v2 manual decisions remain compatible");
        var report = CoverageReport.Markdown(matcher.Analyze(new()), "N5", true);
        check(report.Contains("| ありがとう | 有難う / 有り難う |"), "report leads with a kana lookup and marks written alternatives as optional");
        check(report.Contains("Other accepted forms (optional)") && report.Contains("One accepted spelling or reading is enough"), "report does not imply every variant is required");
        check(!CoverageReport.Markdown(yes, "N5", true).Contains("| 4000001 |"), "resolved yes is removed from missing report without other variants in Anki");
    }
}
