using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AnkiStats.Core;
using Microsoft.Win32;

namespace AnkiStats.App;

public partial class MainWindow
{
    private VocabularyMatcher? vocabularyMatcher;
    private CoverageAnalysis? coverage;
    private CoverageReferenceStore ReferenceStore => new(Path.Combine(store.Root, "reference"));

    private async Task UpdateCoverage()
    {
        if (data is null) return;
        try
        {
            if (vocabularyMatcher is null)
            {
                var reference = await Task.Run(() => ReferenceStore.Load());
                if (reference is not null) vocabularyMatcher = await Task.Run(() => new VocabularyMatcher(reference));
            }
            if (vocabularyMatcher is null)
            {
                coverage = null;
                CoverageGrid.ItemsSource = CoverageMatchGrid.ItemsSource = CoverageFamilyGrid.ItemsSource = null;
                CoverageOverview.Text = "Reference data is not installed. Open Word Coverage to download it once.";
                return;
            }
            var source = data;
            coverage = await Task.Run(() =>
            {
                // Recover acknowledged identities if the app closed after journaling a successful write.
                SaveGeneratedMatches(source.CollectionKey, store.LoadCoverageDecisions<CreationAttempt>(source.CollectionKey, "creation"));
                return vocabularyMatcher.Analyze(source,
                    store.LoadCoverageDecisions<MatchDecision>(source.CollectionKey, "matches"),
                    store.LoadCoverageDecisions<FamilyDecision>(source.CollectionKey, "families"), aliases: LoadMappingAliases(source.CollectionKey));
            });
            var selectedList = (CoverageGrid.SelectedItem as CoverageCount)?.List;
            CoverageGrid.ItemsSource = coverage.Counts;
            CoverageGrid.SelectedItem = coverage.Counts.Find(c => c.List == selectedList) ?? coverage.Counts[0];
            var overall = coverage.Counts.Single(c => c.List == "All entries");
            var common = coverage.Counts.Single(c => c.List == "Common");
            var vocabularyNotes = coverage.Matches.Select(m => m.NoteId).ToHashSet();
            var databaseCards = source.Cards.Count(c => vocabularyNotes.Contains(c.NoteId));
            CoverageOverview.Text = $"Dictionary entries in this profile: {overall.DatabaseDisplay}\n"
                + $"Common words: {common.CombinedDisplay} known or familiar\n"
                + $"{coverage.Known.Count:N0} known entries · {coverage.Familiar.Count:N0} familiar · {coverage.Combined.Count:N0} combined · {coverage.Database.Count:N0} in imported database";
            CoverageStatus.Text = $"{source.Scope}\n{coverage.Known.Count:N0} known entries · {coverage.Familiar.Count:N0} familiar · {coverage.Combined.Count:N0} in their union · {coverage.Database.Count:N0} in database. "
                + "Overlapping entries count once in Combined; Known and Familiar can overlap.\n"
                + $"Mapped vocabulary/grammar in database: {databaseCards:N0} cards / {vocabularyNotes.Count:N0} notes, including unresolved and Suspended-deck items. Grammar contributes only through confirmed links. Coverage below counts dictionary entries.\n"
                + $"JMdict {coverage.Reference.Metadata.DictionaryDate} · OpenJLPT corrected lists · {coverage.Reference.Metadata.UnlinkedJlptItems:N0} unlinked source rows excluded. "
                + "All entries uses the entire installed English JMdict snapshot; spelling/reading variants count once per entry. Other denominators use the selected reference list; Through N3 is the N5/N4/N3 union. Exact live Jisho parity is not claimed.";
            CoverageFamilySummary.Text = $"{coverage.Families.Count:N0} families across {coverage.Combined.Count:N0} confirmed known/familiar entries; {coverage.Families.Count(f => f.Entries.Count > 1):N0} groups contain multiple entries. "
                + $"{coverage.Unresolved:N0} notes have unresolved readings/matches; only their confirmed portions count. Family counts are partial while matches or relationships remain unresolved.";
            CoverageFamilyGrid.ItemsSource = coverage.Families.OrderByDescending(f => f.Entries.Count).ThenBy(f => f.Key).ToList();
            ApplyCoverageFilter();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            coverage = null;
            CoverageGrid.ItemsSource = CoverageMatchGrid.ItemsSource = CoverageFamilyGrid.ItemsSource = null;
            CoverageOverview.Text = "Vocabulary coverage is unavailable; see Word Coverage for recovery.";
            CoverageMatchSummary.Text = CoverageFamilySummary.Text = "Coverage unavailable until the reference data or saved decisions can be read.";
            CoverageStatus.Text = "Reference data or matching decisions could not be loaded. " + e.Message + " Reinstall the reference data if it is damaged.";
        }
    }

    private async void DownloadCoverageReference(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var progress = new Progress<string>(message => { CoverageStatus.Text = message; SetStatus(message); });
        // Download/parse off the UI thread; the previous bundle survives any failed download.
        var reference = await Task.Run(() => ReferenceStore.Download(progress));
        vocabularyMatcher = await Task.Run(() => new VocabularyMatcher(reference));
        await UpdateCoverage();
        await UpdateMappings();
        SetStatus("Reference data installed. Coverage now works offline; Anki and personal matching decisions were not modified.");
    });

    private void ShowCoverageSources(object sender, RoutedEventArgs e)
    {
        var meta = vocabularyMatcher?.Reference.Metadata;
        ShowDetails("Vocabulary reference sources and definitions", $"JMdict: {meta?.DictionaryVersion ?? CoverageReferenceStore.DictionaryVersion}\n"
            + $"Dictionary date: {meta?.DictionaryDate ?? "Not installed"}\nOpenJLPT revision: {meta?.JlptVersion ?? CoverageReferenceStore.JlptRevision}\n\n"
            + "Identity: one JMdict entry, including its accepted spellings and readings, counts once per list. Ambiguous kana-only forms never credit all candidates. "
            + "Reading-to-spelling restrictions are respected. Source JLPT rows without a linked, available JMdict ID are excluded and counted explicitly. "
            + "Levels are independent memberships; cumulative rows are unions. No review-date filter is applied.\n\n"
            + VocabularyMatcher.MatchingRules + "\n\n"
            + VocabularyMatcher.DefinitionRules + "\n\n"
            + "Known: cards in 日本語::永遠 and descendants. Familiar: 日本語::勉強してる and descendants. Suspended-deck cards cannot establish either category. "
            + "Other imported vocabulary, including Suspended, is database-only. These categories use deck placement, not Anki queue state or inferred mastery. "
            + "All metrics use the active profile's imported selection, not excluded decks or the entire source collection.\n\n"
            + "Jisho alignment: the JMdict common rule follows jmdict-simplified’s documented news1/ichi1/spec1/spec2/gai1 rule. "
            + "OpenJLPT’s corrected classifications are used by agreement, including where Jisho differs. These are reference-list coverage statistics, not official JLPT or text-comprehension estimates.\n\n"
            + "Updates: this app pins a verified snapshot. Reinstall reuses that snapshot. A future app release can pin a newer dictionary/list revision; personal decisions remain separate. "
            + "Deleted dictionary IDs and changed note expressions/readings require review rather than automatic reassignment.\n\n"
            + CoverageReferenceStore.Attribution);
    }

    private void CoverageFilterChanged(object sender, RoutedEventArgs e) { if (ready) ApplyCoverageFilter(); }
    private void ApplyCoverageFilter()
    {
        if (coverage is null) return;
        var search = CoverageSearch.Text.Trim();
        var rows = coverage.Matches.Where(m => CoverageMatchFilter.SelectedIndex switch
        {
            1 => m.NeedsReview,
            2 => m.Known || m.Familiar,
            3 => !m.Known && !m.Familiar,
            _ => true
        }).Where(m => search.Length == 0 || $"{m.Expression}\n{m.Reading}\n{m.Meaning}\n{m.Status}\n{m.Decks}\n{m.EntryIdsDisplay}".Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        CoverageMatchGrid.ItemsSource = rows;
        CoverageMatchSummary.Text = $"{rows.Count:N0} of {coverage.Matches.Count:N0} mapped vocabulary/grammar notes · {coverage.Unresolved:N0} unresolved · {coverage.ReadingWarnings:N0} reading warnings · {coverage.DefinitionWarnings:N0} definition warnings. "
            + "Corrections persist per collection and note across imports and profile changes. No Anki notes or tags are changed.";
    }

    private void ExportMissingCoverage(object sender, RoutedEventArgs e)
    {
        if (coverage is null || CoverageGrid.SelectedItem is not CoverageCount row) { SetStatus("Install reference data and select a coverage row first."); return; }
        var absentDatabase = (string)((Button)sender).Tag == "database";
        var dialog = new SaveFileDialog { Filter = "Markdown report (*.md)|*.md", FileName = $"{row.List.Replace(' ', '-')}-{(absentDatabase ? "absent-database" : "not-covered")}.md" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, CoverageReport.Markdown(coverage, row.List, absentDatabase), new UTF8Encoding(false)); SetStatus("Missing-word report saved with reference versions, scope, and database status."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { SetStatus("The report could not be saved. " + error.Message, true); }
    }

    private void ReviewCoverageMatchDoubleClick(object sender, MouseButtonEventArgs e) => ReviewCoverageMatch(sender, e);
    private void CoverageFamilyDetails(object sender, MouseButtonEventArgs e)
    {
        if (CoverageFamilyGrid.SelectedItem is StemFamily family)
            ShowDetails(family.Name, family.Rule + "\n\n" + string.Join("\n\n", family.Entries.Select(entry =>
                $"{string.Join(" / ", entry.Spellings)} · {string.Join(" / ", entry.Readings.Select(r => r.Text))}\nJMdict {entry.Id}\n{entry.Meaning}")));
    }
    private async void ReviewCoverageMatch(object sender, RoutedEventArgs e)
    {
        if (busy || coverage is null || CoverageMatchGrid.SelectedItem is not VocabularyMatch row || data is null) return;
        var collection = data.CollectionKey;
        var decisions = store.LoadCoverageDecisions<MatchDecision>(collection, "matches");
        var families = store.LoadCoverageDecisions<FamilyDecision>(collection, "families");
        var window = new Window { Owner = this, Title = "Review Anki dictionary match", Width = 780, Height = 680, MinWidth = 620, MinHeight = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) };
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = $"{row.Expression} · {row.Reading}\n{row.Cohort} · {row.Status}\n{row.Meaning}\n{row.Decks}\nNote {row.NoteId}\n{row.Reason}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "Candidate entries (select to fill the ID):" });
        var choices = new ListBox { Height = 160, Margin = new Thickness(0, 8, 0, 12),
            Background = (System.Windows.Media.Brush)FindResource("Panel"), Foreground = System.Windows.Media.Brushes.White };
        choices.Resources[SystemColors.HighlightBrushKey] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 77, 110));
        foreach (var entry in row.Candidates)
            choices.Items.Add(new ListBoxItem { Tag = entry.Id, Content = new TextBlock { Text = $"{entry.Id} · {string.Join(" / ", entry.Spellings)} · {string.Join(" / ", entry.Readings.Select(r => r.Text))}\n{entry.Meaning}", TextWrapping = TextWrapping.Wrap } });
        panel.Children.Add(choices);
        panel.Children.Add(new TextBlock { Text = "JMdict entry IDs (comma-separated for explicitly studied alternatives):", TextWrapping = TextWrapping.Wrap });
        var idBox = new TextBox { Text = row.EntryIdsDisplay, Margin = new Thickness(0, 6, 0, 12) };
        panel.Children.Add(idBox);
        choices.SelectionChanged += (_, _) => { if (choices.SelectedItem is ListBoxItem item) idBox.Text = (string)item.Tag; };
        panel.Children.Add(new TextBlock { Text = "Optional family label (single-entry matches only). Assign the same label to related entries to group them. Blank preserves the default rule; dictionary coverage is unaffected.", TextWrapping = TextWrapping.Wrap });
        var familyBox = new TextBox { Text = families.Find(f => f.EntryId == row.EntryId)?.Family ?? "", Margin = new Thickness(0, 6, 0, 12), IsEnabled = row.EntryIds.Count <= 1 };
        panel.Children.Add(familyBox);
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(feedback);
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        string? action = null;
        foreach (var label in new[] { "Save match", "Exclude", "Reset decision", "Cancel" })
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 10, 10) };
            button.Click += (_, _) =>
            {
                if (label == "Save match")
                {
                    var ids = idBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
                    if (ids.Count == 0 || ids.Any(id => !vocabularyMatcher!.Entries.ContainsKey(id))) { feedback.Text = "Enter entry IDs present in the installed reference, separated with commas."; return; }
                    // Make a manually entered ID reviewable before accepting it.
                    var description = string.Join("\n\n", ids.Select(id => vocabularyMatcher!.Entries[id]).Select(entry => $"{entry.Id}: {string.Join(" / ", entry.Spellings)} · {string.Join(" / ", entry.Readings.Select(r => r.Text))}\n{entry.Meaning}"));
                    if (feedback.Text != description) { feedback.Text = description; button.Content = "Confirm this match"; return; }
                }
                action = label; window.DialogResult = label != "Cancel";
            };
            buttons.Children.Add(button);
        }
        idBox.TextChanged += (_, _) => { feedback.Text = ""; familyBox.Text = families.Find(f => f.EntryId == idBox.Text.Trim())?.Family ?? ""; familyBox.IsEnabled = idBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length <= 1; };
        if (window.ShowDialog() != true) return;
        await Guard(async () =>
        {
            decisions.RemoveAll(d => d.NoteId == row.NoteId);
            if (action == "Save match")
            {
                var ids = idBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
                decisions.Add(new(row.NoteId, row.Fingerprint, ids[0], AdditionalEntryIds: ids.Skip(1).ToList()));
                // A family label edits a single dictionary entry, independently of multi-reading matching decisions.
                if (ids.Count == 1)
                {
                    families.RemoveAll(f => f.EntryId == ids[0]);
                    if (!string.IsNullOrWhiteSpace(familyBox.Text)) families.Add(new(ids[0], familyBox.Text.Trim()));
                }
            }
            else if (action == "Exclude") decisions.Add(new(row.NoteId, row.Fingerprint, null, true));
            await Task.Run(() =>
            {
                store.SaveCoverageDecisions(collection, "matches", decisions);
                store.SaveCoverageDecisions(collection, "families", families);
            });
            await RecalculateMappings();
            SetStatus("Local matching decision saved; Anki was not modified.");
        });
    }
}
