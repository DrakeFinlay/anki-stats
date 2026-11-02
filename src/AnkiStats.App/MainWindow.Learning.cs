using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AnkiStats.Core;

namespace AnkiStats.App;

public partial class MainWindow
{
    private LearningAnalysisResult? learning;
    private ImportedCollection? learningSource;
    private bool filteringLearning;
    private async Task UpdateLearningPatterns()
    {
        var source = importedData ?? data;
        if (source is null || data is null) return;
        var today = Streaks.StudyDay(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(source.TimeZoneId));
        if (!ReferenceEquals(source, learningSource) || learning?.Through != today)
        {
            learning = await Task.Run(() => LearningAnalyzer.Analyze(source));
            learningSource = source;
        }
        var ids = data.Cards.Select(c => c.Id).ToHashSet();
        var scoped = learning!.Cards.Where(c => ids.Contains(c.CardId)).ToList();
        LearningScope.Text = $"{data.Scope}\n{scoped.Count(c => c.HistoryFlagged):N0} cards with 3+ historical ordinary-review Again answers · {scoped.Count(c => !c.Known && !c.Excluded && (c.Recovery || c.Suspended)):N0} recovery/suspended cards (overlap possible) · {scoped.Count(c => c.HistoryFlagged && c.RecentLapses >= 3):N0} cards with 3+ recent lapses.\n"
            + $"Recent: {learning.RecentStart:MMM d, yyyy} – {learning.Through:MMM d, yyyy} · 4 AM study dates · snapshot {source.RefreshedAt.ToLocalTime():MMM d, yyyy HH:mm}. Known, immersion and unused cards are excluded from difficulty rankings.";
        FilterLearning();
    }
    private void LearningViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        filteringLearning = true;
        var mode = LearningViewPicker.SelectedIndex;
        LearningKindPicker.ItemsSource = mode == 1 ? new[] { "All comparisons", "Both decks", "Kanji meaning only", "Vocabulary only" }
            : new[] { "All groups", "Same reading", "Character reading", "Written form", "Meaning overlap" };
        LearningKindPicker.SelectedIndex = 0;
        LearningKindPicker.Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
        LearningCohortPicker.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        filteringLearning = false;
        FilterLearning();
    }
    private void LearningFilterChanged(object sender, SelectionChangedEventArgs e) { if (ready && !filteringLearning) FilterLearning(); }
    private void LearningSearchChanged(object sender, TextChangedEventArgs e) { if (ready && !filteringLearning) FilterLearning(); }
    private bool LearningMatches(LearningCard card) => string.IsNullOrWhiteSpace(LearningSearch.Text)
        || $"{card.Word}\n{card.Reading}\n{card.Meaning}\n{card.Deck}".Contains(LearningSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    private void FilterLearning()
    {
        if (learning is null || data is null || filteringLearning) return;
        var mode = LearningViewPicker.SelectedIndex;
        var activeIds = data.Cards.Select(c => c.Id).ToHashSet();
        LearningFindingsGrid.Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
        LearningEvidence.Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(LearningCardsGrid, mode == 0 ? 0 : 2);
        Grid.SetRowSpan(LearningCardsGrid, mode == 0 ? 3 : 1);
        if (mode == 0)
        {
            var cards = learning.Cards.Where(c => activeIds.Contains(c.CardId) && c.Candidate && LearningMatches(c));
            cards = LearningCohortPicker.SelectedIndex switch
            {
                1 => cards.Where(c => c.HistoryFlagged),
                2 => cards.Where(c => c.HistoryFlagged && !c.Recovery && !c.Suspended),
                3 => cards.Where(c => c.Recovery || c.Suspended),
                4 => cards.Where(c => c.HistoryFlagged && c.RecentLapses >= 3),
                5 => cards.Where(c => c.ReadingUnresolved),
                _ => cards
            };
            var rows = cards.OrderByDescending(c => c.RecentLapses).ThenByDescending(c => c.Lapses).ThenBy(c => c.Word).ToList();
            LearningCardsGrid.ItemsSource = rows;
            LearningEvidence.Text = "Difficulty candidates follow the active profile. Recovery/suspended placement is shown separately from demonstrated 3+ lapse history. Double-click a card for supporting reviews.";
            LearningResultCount.Text = $"{rows.Count:N0} cards · historic and current lapse counters can differ; both are shown. Recent Again / answers uses ordinary reviews only.";
            return;
        }
        var byId = learning.Cards.ToDictionary(c => c.CardId);
        IEnumerable<LearningFinding> findings = mode == 1 ? learning.CrossDeck : learning.WordGroups.Select(f =>
        {
            var ids = f.CardIds.Where(activeIds.Contains).ToList();
            return f with { CardIds = ids, FlaggedWords = ids.Select(id => byId[id].Word).Distinct().Count(), RecentLapses = ids.Sum(id => byId[id].RecentLapses) };
        }).Where(f => f.FlaggedWords >= 2);
        if (LearningKindPicker.SelectedIndex > 0 && LearningKindPicker.SelectedItem is string kind) findings = findings.Where(f => f.Kind == kind);
        var query = LearningSearch.Text.Trim();
        if (query.Length > 0) findings = findings.Where(f => f.Pattern.Contains(query, StringComparison.OrdinalIgnoreCase) || f.CardIds.Any(id => LearningMatches(byId[id])));
        var selected = LearningFindingsGrid.SelectedItem as LearningFinding;
        var results = findings.OrderByDescending(f => f.RecentLapses).ThenByDescending(f => f.FlaggedWords).ToList();
        LearningFindingsGrid.ItemsSource = results;
        LearningFindingsGrid.SelectedItem = results.Find(f => selected is not null && f.Kind == selected.Kind && f.Pattern == selected.Pattern) ?? results.FirstOrDefault();
        LearningResultCount.Text = mode == 1 ? $"{results.Count:N0} comparisons · uses both imported 日本語 and 漢字 trees, independent of the active-profile card list. Known words are supporting context, not difficulty candidates. Missing or insufficient history is not mastery."
            : $"{results.Count:N0} candidate groups in the active profile · up to 150 strongest groups per type from imported history. Not a diagnosis of why an answer failed.";
        ShowLearningEvidence();
    }
    private void LearningFindingChanged(object sender, SelectionChangedEventArgs e) { if (ready) ShowLearningEvidence(); }
    private void ShowLearningEvidence()
    {
        if (learning is null || LearningViewPicker.SelectedIndex == 0) return;
        var finding = LearningFindingsGrid.SelectedItem as LearningFinding;
        LearningEvidence.Text = finding is null ? "No supported findings in this selection. Try another profile, category or search. Unresolved readings are not guessed."
            : finding.Pattern + " · " + finding.Confidence + "\n" + finding.Evidence;
        var ids = finding?.CardIds.ToHashSet() ?? [];
        LearningCardsGrid.ItemsSource = learning.Cards.Where(c => ids.Contains(c.CardId)).OrderBy(c => c.Known).ThenByDescending(c => c.RecentLapses).ThenByDescending(c => c.Lapses).ToList();
    }
    private void LearningCardDetails(object sender, MouseButtonEventArgs e)
    {
        if (LearningCardsGrid.SelectedItem is not LearningCard card || learningSource is null) return;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(learningSource.TimeZoneId);
        var reviews = learningSource.Reviews.Where(r => r.CardId == card.CardId && r.Kind == 1 && r.Rating is >= 1 and <= 4 && r.Id <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).OrderByDescending(r => r.Id).Take(30);
        var text = $"{card.Word} · {card.Reading}\n{card.Meaning}\nDeck: {card.Deck}\nStatus: {card.Reason}\nCard ID: {card.CardId} · Note ID: {card.NoteId}\n\n"
            + $"Historical ordinary-review Again: {card.Lapses} / {card.Reviews}\nCurrent Anki lapse counter: {card.StoredLapses}\nLast six months Again / ordinary answers: {card.RecentDisplay}\n"
            + $"Reading: {card.ReadingStatus}\nExplicit character readings: {string.Join(", ", card.CharacterReadings.Select(r => r.Kanji + " → " + r.Reading))}\n\n"
            + "Japanese cards test reading and meaning; history cannot tell which caused a failure. Kanji cards test meaning. Repeated learning/relearning answers do not count as separate lapses. Recovery placement is not itself proof of review failure.\n\nLatest 30 ordinary answers (local clock; 1 = Again):\n"
            + string.Join("\n", reviews.Select(r => $"{TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(r.Id), zone):yyyy-MM-dd HH:mm} · rating {r.Rating}"));
        ShowDetails("Learning evidence · " + card.Word, text);
    }
}
