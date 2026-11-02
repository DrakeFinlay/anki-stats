using AnkiStats.Core;

internal static class CoverageMatchingChecks
{
    public static CoverageReference Reference() => new(new("synthetic", "2026-11-02", "synthetic", 0, "Synthetic reference"),
        [
            new("3000001", [], [new("クラス", ["*"])], "class", true, ["N5"]),
            new("3000002", ["暮らす"], [new("くらす", ["*"])], "to live", true, ["N3"]),
            new("3000003", ["万年筆"], [new("まんねんひつ", ["*"])], "fountain pen", true, ["N5"]),
            new("3000004", ["女の子"], [new("おんなのこ", ["*"])], "girl", true, ["N5"]),
            new("3000005", ["女の子"], [new("めのこ", ["*"])], "girl; older reading", false, []),
            new("3000006", ["昨夜"], [new("ゆうべ", ["*"])], "last night", true, ["N5"]),
            new("3000007", ["昨夜"], [new("さくや", ["*"])], "last night", true, []),
            new("3000008", ["せい"], [new("せい", ["*"])], "synthetic homophone A", false, []),
            new("3000009", ["せい"], [new("せい", ["*"])], "synthetic homophone B", false, []),
            new("3000010", ["生物"], [new("せいぶつ", ["*"]), new("なまもの", ["*"])], "living thing; raw food", true, ["N4"]),
            new("3000011", ["甲乙"], [new("こうおつ", ["*"])], "synthetic close reading A", false, []),
            new("3000012", ["甲乙"], [new("こうあつ", ["*"])], "synthetic close reading B", false, []),
            new("3000013", ["山"], [new("やま", ["*"])], "mountain", true, []),
            new("3000014", ["川", "河"], [new("かわ", ["川"]), new("かせんか", ["河"])], "restricted reading", false, []),
            new("3000015", ["SNS"], [new("エスエヌエス", ["*"])], "social networking service", false, [])
        ]);
    public static void Run(Action<bool, string> check, string sandbox)
    {
        // Synthetic notes/IDs model independent spellings, homographs, and explicitly studied readings.
        var reference = Reference();
        var matcher = new VocabularyMatcher(reference);
        ImportedCollection Source(string expression, string reading = "") => new()
        {
            CollectionKey = "synthetic-matching", Scope = "Synthetic matching checks",
            Notes = [new(1, expression, reading, "", "Vocabulary", "Synthetic")], Cards = [new(1, 1, "日本語::永遠", 2, 2, 0)]
        };
        CoverageAnalysis Analyze(string expression, string reading = "") => matcher.Analyze(Source(expression, reading));
        var loanword = Analyze("クラス");
        check(loanword.Known.SetEquals(new[] { "3000001" }), "exact katakana form outranks folded hiragana homophone");
        check(Analyze("ｸﾗｽ").Known.SetEquals(loanword.Known), "width normalization retains katakana identity");
        check(Analyze("くらす").Known.SetEquals(new[] { "3000002" }), "hiragana input retains its exact-script reading preference");
        check(loanword.Matches.Single().Expression == "クラス", "extracted expression preserves written script");
        var pen = Analyze("万年筆[まんえんひつ]");
        check(pen.Known.Contains("3000003") && pen.ReadingWarnings == 1 && pen.Unresolved == 0, "exact kanji plus unique one-character reading discrepancy counts with warning");
        var girl = Analyze("女の子[おんあのこ]");
        check(girl.Known.SetEquals(new[] { "3000004" }) && girl.Matches.Single().NeedsReview, "bounded reading discrepancy selects one supported homograph and remains inspectable");
        check(!Analyze("万年筆[ちがうよみ]").Known.Any(), "unrelated reading cannot credit an exact spelling automatically");
        check(!Analyze("万年比[まんねんひつ]").Known.Any(), "near spelling never receives fuzzy dictionary credit");
        check(!Analyze("山[やむ]").Known.Any(), "short reading discrepancies require manual review");
        check(!Analyze("甲乙[こういつ]").Known.Any(), "equally close readings do not credit multiple homographs");
        check(!Analyze("川[かせんけ]").Known.Any(), "reading-discrepancy recovery respects spelling restrictions");
        check(Analyze("万年筆[まねんんひつ]").Known.Contains("3000003"), "single adjacent reading transposition can be flagged and resolved");
        check(Analyze("万年筆[まんねひつ]").Known.Contains("3000003"), "single missing reading character can be flagged and resolved");
        check(Analyze("万年筆[まんねんんひつ]").Known.Contains("3000003"), "single extra reading character can be flagged and resolved");
        check(Analyze("ＳＮＳ[えす・えぬ・えす]").Known.SetEquals(new[] { "3000015" }), "dictionary-backed phonetic joining preserves repeated letter names");
        var multi = Analyze("昨夜[ゆうべ・さくや]");
        check(multi.Known.SetEquals(new[] { "3000006", "3000007" }) && multi.Unresolved == 0, "explicit alternatives credit independently resolved dictionary entries");
        check(multi.Matches.Single().Reading == "ゆうべ / さくや" && multi.Matches.Single().EntryIds.Count == 2, "multiple readings and entry identities remain inspectable");
        check(multi.Counts.Single(c => c.List == "N5").Known == 1, "only classified alternative contributes to its JLPT list");
        check(Analyze("昨夜[ゆうべ／さくや]").Known.SetEquals(multi.Known), "fullwidth reading separator is parsed");
        check(Analyze("昨夜", "ゆうべ / さくや").Known.SetEquals(multi.Known), "mapped reading field supports alternatives");
        check(Analyze("昨夜[ゆうべ, さくや]").Known.SetEquals(multi.Known), "comma-separated reading alternatives are parsed");
        check(Analyze("生物[せいぶつ・なまもの]").Known.Count == 1, "multiple readings of one entry count once");
        var partial = Analyze("昨夜[ゆうべ・せい]");
        check(partial.Known.SetEquals(new[] { "3000006" }) && partial.Unresolved == 1 && partial.Matches.Single().Status == "Partial", "unresolved alternative cannot erase a confirmed reading or inflate its sibling");
        check(!Analyze("せい").Known.Any(), "genuinely ambiguous kana remains uncredited");
        check(!Analyze("昨夜[ゆうべ・not-a-reading]").Known.Any(), "invalid alternative notation cannot silently drop reading evidence");
        var segmented = VocabularyExpression.Parse(new(1, "山[やま]川[かわ]", "", "", "Vocabulary", "Synthetic"));
        check(segmented.Word == "山川" && segmented.Readings.SequenceEqual(new[] { "やまかわ" }), "segment annotations form a full reading");
        var marked = VocabularyExpression.Parse(new(1, "<b>昨夜</b>[ゆうべ・さくや]", "", "", "Vocabulary", "Synthetic"));
        check(marked.Word == "昨夜" && marked.Readings.Count == 2, "presentation markup does not break explicit alternatives");
        var spaced = VocabularyExpression.Parse(new(1, "万年筆  [まんねんひつ]", "", "", "Vocabulary", "Synthetic"));
        check(spaced.Word == "万年筆" && spaced.Readings.SequenceEqual(new[] { "まんねんひつ" }), "whitespace before whole-word furigana is accepted");
        var numbered = VocabularyExpression.Parse(new(1, "１８禁[じゅうはちきん]", "", "", "Vocabulary", "Synthetic"));
        check(numbered.Word == "18禁" && numbered.Readings.SequenceEqual(new[] { "じゅうはちきん" }), "numeric headwords have complete whole-word readings");
        var nonbreaking = VocabularyExpression.Parse(new(1, "万年筆\u00a0[まんねんひつ]", "", "", "Vocabulary", "Synthetic"));
        check(nonbreaking.Word == "万年筆" && nonbreaking.Readings.Count == 1, "nonbreaking annotation spacing is accepted");
        var tooMany = VocabularyExpression.Parse(new(1, string.Concat(Enumerable.Repeat("山[やま・さん]", 6)), "", "", "Vocabulary", "Synthetic"));
        check(tooMany.Error is not null, "reading expansion is bounded");
        // Old saved fingerprints folded the written form. Keep valid manual decisions stable after the fix.
        var saved = new MatchDecision(1, "くらす\u001fくらす", "3000001");
        check(matcher.Analyze(Source("クラス"), [saved]).Matches.Single().Status == "Corrected", "existing single-reading corrections survive script-preservation fix");
        var modern = saved with { Fingerprint = loanword.Matches.Single().Fingerprint };
        check(matcher.Analyze(Source("くらす"), [modern]).Matches.Single().Status == "Stale decision", "new decision fingerprints distinguish changed written script");
        var oldMulti = new MatchDecision(1, "昨夜\u001f", "3000006");
        check(matcher.Analyze(Source("昨夜[ゆうべ・さくや]"), [oldMulti]).Matches.Single().Status == "Corrected", "legacy corrections survive multi-reading parser fix");
        var multiDecision = new MatchDecision(1, multi.Matches.Single().Fingerprint, "3000006", AdditionalEntryIds: ["3000007"]);
        var corrected = matcher.Analyze(Source("昨夜[ゆうべ・さくや]"), [multiDecision]);
        check(corrected.Known.SetEquals(multi.Known), "manual correction can preserve multiple explicit targets");
        var store = new LocalStore(Path.Combine(sandbox, "multi-reading"));
        store.SaveCoverageDecisions("synthetic", "matches", new List<MatchDecision> { multiDecision });
        var reloaded = store.LoadCoverageDecisions<MatchDecision>("synthetic", "matches");
        check(matcher.Analyze(Source("昨夜[ゆうべ・さくや]"), reloaded).Known.SetEquals(multi.Known), "multiple manual entry IDs survive local persistence");
        check(matcher.Analyze(Source("昨夜[ゆうべ]"), reloaded).Matches.Single().Status == "Stale decision", "changed declared readings invalidate saved multi-target correction");
        check(matcher.Analyze(Source("昨夜[ゆうべ・さくや]"), [multiDecision with { AdditionalEntryIds = ["3999999"] }]).Known.Count == 0, "removed ID invalidates multi-target correction rather than partially trusting it");
        var report = CoverageReport.Markdown(girl, "N5", true);
        check(!report.Contains("| 女の子 |") && report.Contains("1 confirmed notes have reading warnings"), "confirmed discrepancy no longer appears absent and report discloses warnings");
        var ambiguousReport = CoverageReport.Markdown(Analyze("女の子"), "N5", true);
        check(ambiguousReport.Contains("Possible matches — resolve before treating as absent") && ambiguousReport.Contains("Possible match; needs review"), "possible dictionary matches are separated from absent evidence");
    }
}
