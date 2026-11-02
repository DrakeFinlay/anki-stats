using System.Text.Json;
using AnkiStats.Core;

internal static class LearningChecks
{
    public static void Run(Action<bool, string> check)
    {
        var source = new ImportedCollection { TimeZoneId = "UTC", RefreshedAt = DateTimeOffset.Parse("2026-11-02T12:00Z") };
        var clock = source.RefreshedAt;
        void Card(long id, string expression, string meaning, string deck, string category = "Vocabulary", int queue = 2, int type = 2, int stored = 0)
        {
            source.Notes.Add(new(id, expression, "", meaning, category, "Synthetic"));
            source.Cards.Add(new(id, id, deck, queue, type, stored));
        }
        var ordinal = 0;
        void Answers(long id, int again, int successes = 0, int kind = 1, string date = "2026-10-01T12:00Z")
        {
            var stamp = DateTimeOffset.Parse(date).ToUnixTimeMilliseconds();
            for (var i = 0; i < again + successes; i++) source.Reviews.Add(new(stamp + ordinal++, id, i < again ? 1 : 3, 1000, kind));
        }
        Card(1, "森", "grove", "漢字", "Kanji"); Answers(1, 3, 7);
        Card(2, "森林[しんりん]", "dense grove; thick grove", "日本語::勉強してる"); Answers(2, 3, 8);
        Card(3, "深林[しんりん]", "deep grove; thick grove", "日本語::勉強してる"); Answers(3, 3, 6);
        Card(4, "山道[やまみち]", "mountain path", "日本語::永遠"); Answers(4, 12, 2);
        Card(5, "山川[やまかわ]", "mountains and rivers", "日本語::勉強してる"); Answers(5, 3, 9);
        Card(6, "山", "mountain", "漢字", "Kanji"); Answers(6, 2, 8);
        Card(7, "海", "sea", "漢字", "Kanji"); Answers(7, 4, 9);
        Card(8, "海辺[うみべ]", "seashore", "日本語::永遠"); Answers(8, 6, 10);
        Card(9, "火", "fire", "漢字::Unused", "Kanji", queue: -1); Answers(9, 10, 5);
        Card(10, "火山[かざん]", "volcano", "日本語::勉強してる"); Answers(10, 3, 8);
        Card(11, "未学[みがく]", "synthetic recovery item", "日本語::Suspended", type: 0, queue: 0);
        Card(12, "未", "not yet", "漢字::Relearn", "Kanji", type: 0, queue: 0);
        Card(13, "休む[やすむ]", "rest", "日本語::勉強してる", queue: -1);
        Card(14, "森林[しんりん]", "thick grove", "日本語::Seen In Immersion"); Answers(14, 20, 1);
        Card(15, "文法", "grammar", "日本語", "Grammar"); Answers(15, 20, 1);
        Card(16, "晴れる[はれる]", "become clear", "日本語::勉強してる"); Answers(16, 3, 4);
        Card(17, "晴らす[はらす]", "clear away", "日本語::勉強してる"); Answers(17, 3, 5);
        Card(18, "古道[こどう]", "old path", "日本語::勉強してる", stored: 100); Answers(18, 20, kind: 0); Answers(18, 20, kind: 2); Answers(18, 20, kind: 3); Answers(18, 20, kind: 4);
        Card(19, "新道[しんどう]", "new path", "日本語::勉強してる"); Answers(19, 3, date: "2026-01-01T12:00Z"); Answers(19, 5, date: "2026-11-03T12:00Z");
        Card(20, "雪[ゆき]", "snow", "日本語::勉強してる"); Answers(20, 1, date: "2026-05-02T03:59Z"); Answers(20, 1, date: "2026-05-02T04:00Z"); Answers(20, 1, date: "2026-10-02T12:00Z");
        // A second card on the same word cannot manufacture a two-word pattern.
        source.Cards.Add(new(21, 16, "日本語::勉強してる", 2, 2, 0)); Answers(21, 3, 5);
        var result = LearningAnalyzer.Analyze(source, clock: clock);
        LearningCard Row(long id) => result.Cards.Single(c => c.CardId == id);
        check(Row(1).HistoryFlagged && Row(1).Lapses == 3 && Row(1).StoredLapses == 0, "learning threshold uses recorded ordinary Again rather than current lapse counter");
        check(!Row(18).HistoryFlagged && Row(18).Lapses == 0 && Row(18).StoredLapses == 100, "learning, relearning, filtered and admin Again events do not count as separate lapses");
        check(Row(4).Known && !Row(4).Candidate && !Row(4).HistoryFlagged, "known deck overrides heavy historical difficulty");
        check(Row(9).Excluded && !Row(9).Candidate && !result.CrossDeck.Any(f => f.Pattern == "火"), "unused kanji excluded from ranking and comparison evidence");
        check(Row(11).Candidate && Row(11).Recovery && !Row(11).HistoryFlagged && Row(11).Reason.Contains("new"), "new vocabulary recovery is placement evidence rather than inferred failure");
        check(Row(12).Recovery && Row(12).Candidate && Row(12).Lapses == 0, "Kanji Relearn treated as recovery");
        check(Row(13).Suspended && Row(13).Candidate && !Row(13).HistoryFlagged, "suspended state is distinct from three demonstrated lapses");
        check(Row(14).Excluded && !Row(14).Candidate && result.Cards.All(c => c.CardId != 15), "immersion ignored case-insensitively and grammar excluded");
        check(Row(19).Lapses == 3 && Row(19).RecentLapses == 0, "future reviews excluded and old difficulties distinguished from recent lapses");
        check(Row(20).Lapses == 3 && Row(20).RecentLapses == 2 && result.RecentStart == new DateOnly(2026, 5, 2), "recent six-month lapse counts honor the 4 AM boundary");
        check(result.CrossDeck.Any(f => f.Pattern == "森" && f.Kind == "Both decks" && f.CardIds.Contains(1) && f.CardIds.Contains(2)), "cross-deck both-side difficulty includes supporting card ids");
        check(result.CrossDeck.Any(f => f.Pattern == "山" && f.Kind == "Vocabulary only" && f.CardIds.Contains(4)), "below-threshold meaning history and known vocabulary provide context");
        check(result.CrossDeck.Any(f => f.Pattern == "海" && f.Kind == "Kanji meaning only" && f.CardIds.Contains(8)), "kanji difficulty versus known vocabulary comparison");
        check(!result.CrossDeck.Any(f => f.Pattern == "未"), "recovery with no ordinary history is not falsely classified as mastery or repeated failure");
        check(result.WordGroups.Any(f => f.Kind == "Same reading" && f.Pattern == "しんりん" && f.FlaggedWords == 2), "explicit whole-word homophones grouped");
        check(result.WordGroups.Any(f => f.Kind == "Written form" && f.Pattern == "□林"), "written-form groups identify one-character differences");
        check(result.WordGroups.Any(f => f.Kind == "Meaning overlap" && f.Pattern == "thick grove"), "meaning groups use exact stored-gloss evidence");
        check(result.WordGroups.Any(f => f.Kind == "Character reading" && f.Pattern == "晴 → は" && f.FlaggedWords == 2), "single-kanji reading families count distinct words instead of reverse cards");
        check(result.WordGroups.All(f => !f.CardIds.Contains(4) && !f.CardIds.Contains(9) && !f.CardIds.Contains(14)), "known and ignored cards never enter difficulty word groups");
        check(Row(2).Reading == "しんりん" && Row(2).ReadingUnresolved && Row(2).CharacterReadings.Count == 0, "whole compound reading does not guess constituent kanji readings");
        check(!Row(16).ReadingUnresolved && Row(16).CharacterReadings.Single() == new CharacterReading("晴", "は"), "okurigana permits explicit single-kanji attribution");
        check(LearningAnalyzer.Readings("晴[は]れる").Characters.Single().Reading == "は", "inline per-character furigana supported");
        check(LearningAnalyzer.Readings("お森[もり]").Unresolved, "mismatched kana alignment left unresolved");
        check(LearningAnalyzer.Readings("森林", "しんりん").Unresolved, "explicit full-word reading does not imply character alignment");
        check(!LearningAnalyzer.Readings("森", "もり").Unresolved && LearningAnalyzer.Readings("森", "もり").Characters.Single().Reading == "もり", "explicit single-kanji word reading is directly attributable");
        check(LearningAnalyzer.Readings("もり").FullReading == "もり" && !LearningAnalyzer.Readings("もり").Unresolved, "kana-only written forms need no kanji alignment");
        var preserved = DeckProfiles.Copy(source);
        preserved.Notes = [new(1, "晴れる", "", "become clear", "Vocabulary", "Synthetic", "晴れる[はれる]")];
        preserved.Cards = [new(1, 1, "日本語", 2, 2, 0)];
        check(!LearningAnalyzer.Analyze(preserved, clock: clock).Cards.Single().ReadingUnresolved, "preserved source expression supports readings after inventory bracket stripping");
        var legacy = JsonSerializer.Deserialize<NoteRecord>("{\"Id\":1,\"Expression\":\"晴れる[はれる]\",\"Reading\":\"\",\"Meaning\":\"clear\",\"Category\":\"Vocabulary\",\"NoteType\":\"Synthetic\"}")!;
        preserved.Notes = [legacy];
        check(!LearningAnalyzer.Analyze(preserved, clock: clock).Cards.Single().ReadingUnresolved, "legacy cache without source expression remains readable");
        check(LearningAnalyzer.Analyze(new() { TimeZoneId = "UTC" }, clock: clock).Cards.Count == 0, "empty difficulty analysis");
        check(source.Cards.Count == 21 && source.Reviews.Count > 0 && Row(4).Lapses == 12, "analysis does not modify source history or erase excluded-card evidence");
    }
}
