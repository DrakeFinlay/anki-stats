using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AnkiStats.Core;

namespace AnkiStats.App;

public partial class MainWindow
{
    private sealed record MappingUndo(List<MatchDecision> Notes, List<MediaMappingDecision> Media, List<MappingDeferred> Deferred,
        int Position, MappingReviewItem Item, AnkiCorrectionReceipt? Correction);
    private void ShowMappingReview(List<MappingReviewItem> queue)
    {
        if (vocabularyMatcher is null || queue.Count == 0) { MappingStatus.Text = "No items in this review queue. Choose another filter or refresh mappings."; return; }
        var scope = MappingScope;
        var window = new Window { Owner = this, Title = "Review mappings — Space confirm · 1 manual cleanup", Width = 1180, Height = 800,
            MinWidth = 850, MinHeight = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var layout = new DockPanel { Margin = new Thickness(20) }; window.Content = layout;
        var heading = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var correctionPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; footer.Children.Add(correctionPanel);
        var correctAnki = new CheckBox { Content = "Apply the expression / reading shown below to Anki when confirming (Space)" }; correctionPanel.Children.Add(correctAnki);
        var correctionFields = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        correctionFields.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) }); correctionFields.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var expressionPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; correctionFields.Children.Add(expressionPanel);
        expressionPanel.Children.Add(new TextBlock { Text = "Expression to save (your English meaning is preserved)" });
        var correctedExpression = new TextBox { FontSize = 18 }; expressionPanel.Children.Add(correctedExpression);
        var readingPanel = new StackPanel(); Grid.SetColumn(readingPanel, 1); correctionFields.Children.Add(readingPanel);
        readingPanel.Children.Add(new TextBlock { Text = "Separate reading field (if mapped)" });
        var correctedReading = new TextBox { FontSize = 18 }; readingPanel.Children.Add(correctedReading); correctionPanel.Children.Add(correctionFields);
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) }; footer.Children.Add(feedback);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var confirm = new Button { Content = "Confirm selected (Space)" }; var defer = new Button { Content = "Manual cleanup (1)" };
        var ignore = new Button { Content = "Ignore item" }; var undo = new Button { Content = "Undo", IsEnabled = false }; var close = new Button { Content = "Close" };
        foreach (var button in new[] { confirm, defer, ignore, undo, close }) buttons.Children.Add(button);
        var sides = new Grid(); sides.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); sides.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); layout.Children.Add(sides);
        TextBox Detail() => new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 19, Padding = new Thickness(14), Background = (System.Windows.Media.Brush)FindResource("Panel"), Foreground = System.Windows.Media.Brushes.White };
        var original = Detail(); original.Margin = new Thickness(0, 0, 12, 0); sides.Children.Add(original);
        var right = new DockPanel(); Grid.SetColumn(right, 1); sides.Children.Add(right);
        var search = new StackPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(search, Dock.Top); right.Children.Add(search);
        search.Children.Add(new TextBlock { Text = "Proposed dictionary match · select one, or Ctrl-select several", FontSize = 18, TextWrapping = TextWrapping.Wrap });
        var lookup = new TextBox { FontSize = 18, Margin = new Thickness(0, 8, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetName(lookup, "Mapping lookup or comma-separated JMdict IDs"); search.Children.Add(lookup);
        var lookupButton = new Button { Content = "Search word / apply dictionary IDs", HorizontalAlignment = HorizontalAlignment.Left }; search.Children.Add(lookupButton);
        var candidates = new ListBox { Height = 140, SelectionMode = SelectionMode.Extended, DisplayMemberPath = "Meaning", Margin = new Thickness(0, 8, 0, 8),
            Background = (System.Windows.Media.Brush)FindResource("Panel"), Foreground = System.Windows.Media.Brushes.White };
        System.Windows.Automation.AutomationProperties.SetName(candidates, "Proposed dictionary entries");
        DockPanel.SetDock(candidates, Dock.Top); right.Children.Add(candidates);
        var detail = Detail(); right.Children.Add(detail);
        var history = new Stack<MappingUndo>(); var position = 0; var saving = false;
        correctAnki.Click += (_, _) => window.Focus();
        void ConfigureCorrection()
        {
            correctionPanel.Visibility = position < queue.Count && queue[position].NoteId is not null ? Visibility.Visible : Visibility.Collapsed;
            correctAnki.IsChecked = false;
            if (position >= queue.Count || queue[position].NoteId is not {} id || MappingSource?.Notes.Find(n => n.Id == id) is not {} note) return;
            var separateReading = settings?.Mappings.Any(m => m.Reading >= 0 && MappingSource.Metadata.NoteTypes.Any(t => t.Id == m.NoteTypeId && t.Name == note.NoteType)) == true;
            correctedReading.IsEnabled = separateReading;
            var selected = candidates.SelectedItems.OfType<DictionaryEntry>().ToList();
            var proposal = selected.Count == 1 ? AnkiNoteCorrection.Suggest(note, selected[0], separateReading)
                : new NoteCorrectionProposal(note.SourceExpression.Length > 0 ? note.SourceExpression : note.Expression, note.Reading, false);
            correctedExpression.Text = proposal.Expression; correctedReading.Text = proposal.Reading;
            correctAnki.IsChecked = proposal.Automatic && (proposal.Expression != (note.SourceExpression.Length > 0 ? note.SourceExpression : note.Expression)
                || separateReading && proposal.Reading != note.Reading);
        }
        string Describe(DictionaryEntry entry) => $"JMdict {entry.Id}\n{string.Join(" / ", entry.Spellings)}\n"
            + string.Join("\n", entry.Readings.Select(r => r.Text + (r.AppliesTo.Count > 0 && !r.AppliesTo.Contains("*") ? " [applies to " + string.Join(", ", r.AppliesTo) + "]" : "")))
            + $"\n\n{entry.Meaning}\n\nCommon: {(entry.Common ? "Yes" : "No")} · JLPT: {string.Join(", ", entry.Levels)}\nDictionary: {vocabularyMatcher.Reference.Metadata.DictionaryVersion}";
        void ShowCandidate()
        {
            var selected = candidates.SelectedItems.OfType<DictionaryEntry>().ToList();
            detail.Text = selected.Count == 0 ? "No selected dictionary entry. Search a lookup or enter verified JMdict IDs, or send this item to manual cleanup."
                : string.Join("\n\n────────────\n\n", selected.Select(Describe));
            confirm.IsEnabled = !saving && position < queue.Count && selected.Count > 0;
            if (!saving) ConfigureCorrection();
        }
        void Load()
        {
            undo.IsEnabled = history.Count > 0; defer.IsEnabled = ignore.IsEnabled = position < queue.Count;
            if (position >= queue.Count)
            {
                heading.Text = "Review complete"; original.Text = "This queue is finished. Deferred items remain in the manual cleanup pile.";
                candidates.ItemsSource = null; detail.Text = "Close to return to the dashboard, or Undo the last decision."; confirm.IsEnabled = false; correctionPanel.Visibility = Visibility.Collapsed; return;
            }
            var item = queue[position];
            heading.Text = $"{position + 1:N0} / {queue.Count:N0} · {item.Kind} mapping";
            original.Text = (item.Kind == "Anki" ? "CURRENT ANKI ITEM\n\n" : "ORIGINAL MEDIA SOURCE\n\n")
                + item.Expression + "\n\nReading: " + item.Reading + "\n\n" + item.Meaning + "\n\n" + item.Location
                + (item.Sentence.Length > 0 ? "\n\nSource sentence:\n" + item.Sentence : "") + "\n\nWhy review?\n" + item.Reason;
            lookup.Text = item.Expression;
            candidates.ItemsSource = item.CandidateIds.Where(vocabularyMatcher.Entries.ContainsKey).Select(id => vocabularyMatcher.Entries[id]).ToList();
            candidates.SelectedIndex = candidates.Items.Count > 0 ? 0 : -1; ShowCandidate();
            feedback.Text = "Space confirms the link and writes the displayed expression/reading only when the correction box is checked. Uncheck it for a local link only. 1 defers; Ignore suppresses locally. Undo also reverts a verified field edit if its fields are unchanged.";
            window.Focus();
        }
        async Task Save(string action)
        {
            if (saving || position >= queue.Count) return;
            var ids = candidates.SelectedItems.OfType<DictionaryEntry>().Select(e => e.Id).Distinct().ToList();
            if (action == "confirm" && ids.Count == 0) { feedback.Text = "Select a dictionary entry before confirming."; return; }
            saving = true; confirm.IsEnabled = defer.IsEnabled = ignore.IsEnabled = undo.IsEnabled = close.IsEnabled = false;
            correctionPanel.IsEnabled = sides.IsEnabled = false;
            string? failure = null;
            AnkiCorrectionReceipt? correction = null;
            try
            {
                var item = queue[position];
                var notes = store.LoadCoverageDecisions<MatchDecision>(scope, "matches");
                var media = store.LoadCoverageDecisions<MediaMappingDecision>(scope, "media-mappings");
                var deferred = store.LoadCoverageDecisions<MappingDeferred>(scope, "mapping-cleanup");
                var previous = new MappingUndo(notes.ToList(), media.ToList(), deferred.ToList(), position, item, null);
                if (action == "confirm" && correctAnki.IsChecked == true && item.NoteId is {} target)
                {
                    if (store.IsIsolated || MappingSource?.IsSynthetic == true) throw new InvalidOperationException("Anki correction is disabled in isolated/synthetic sessions. Uncheck the correction box to save a local link only.");
                    var source = MappingSource ?? throw new InvalidOperationException("Refresh mappings first.");
                    var expected = source.Notes.Single(n => n.Id == target);
                    if (VocabularyMatcher.Fingerprint(expected) != item.Fingerprint) throw new InvalidOperationException("This review item changed. Close and refresh mappings.");
                    using var client = new AnkiConnectClient(AnkiApiKey.Password);
                    feedback.Text = "Checking current Anki fields before applying the displayed correction…";
                    correction = await AnkiNoteCorrection.Prepare(client, source, settings!, expected, correctedExpression.Text, correctedReading.Text);
                    await ApplyMappingCorrection(client, correction);
                    item = item with { Fingerprint = VocabularyMatcher.Fingerprint(correction.AfterNote), Expression = correction.AfterNote.SourceExpression, Reading = correction.AfterNote.Reading };
                    queue[position] = item; previous = previous with { Correction = correction };
                }
                deferred.RemoveAll(d => d.Key == item.Key);
                if (action == "defer") deferred.Add(new(item.Key, item.Fingerprint));
                if (item.NoteId is {} noteId)
                {
                    notes.RemoveAll(d => d.NoteId == noteId);
                    notes.Add(action == "confirm" ? new(noteId, item.Fingerprint, ids[0], AdditionalEntryIds: ids.Skip(1).ToList())
                        : new(noteId, item.Fingerprint, null, Exclude: action == "ignore", Origin: action == "defer" ? "Deferred" : "Manual"));
                }
                else
                {
                    media.RemoveAll(d => d.CorpusId == item.CorpusId && d.WordKey == item.WordKey);
                    if (action != "defer") media.Add(new(item.CorpusId!, item.WordKey!, item.Fingerprint, ids, action == "ignore"));
                }
                await Task.Run(() => store.SaveMappingState(scope, notes, media, deferred));
                history.Push(previous); await RecalculateMappings(correction is not null); position++;
            }
            catch (Exception error) { failure = "Could not finish the mapping update: " + error.Message
                + (correction is not null ? " Check Anki field corrections in the dashboard before retrying; Anki may already have changed." : ""); }
            finally { saving = false; correctionPanel.IsEnabled = sides.IsEnabled = true; close.IsEnabled = true; Load(); if (failure is not null) feedback.Text = failure; }
        }
        candidates.SelectionChanged += (_, _) => ShowCandidate();
        lookupButton.Click += (_, _) =>
        {
            if (saving) return;
            var value = lookup.Text.Trim();
            var ids = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var found = ids.Length > 0 && ids.All(vocabularyMatcher.Entries.ContainsKey) ? ids.Select(id => vocabularyMatcher.Entries[id]).ToList()
                : MappingSuggestions.Find(vocabularyMatcher, value, "", "");
            candidates.ItemsSource = found; candidates.SelectedItems.Clear();
            if (ids.Length > 0 && ids.All(vocabularyMatcher.Entries.ContainsKey)) foreach (var entry in found) candidates.SelectedItems.Add(entry);
            else candidates.SelectedIndex = found.Count > 0 ? 0 : -1;
            ShowCandidate();
        };
        confirm.Click += async (_, _) => await Save("confirm"); defer.Click += async (_, _) => await Save("defer"); ignore.Click += async (_, _) => await Save("ignore");
        undo.Click += async (_, _) =>
        {
            if (saving || history.Count == 0) return;
            saving = true;
            string? failure = null;
            try { var previous = history.Peek();
                if (previous.Correction is {} correction)
                {
                    using var client = new AnkiConnectClient(AnkiApiKey.Password);
                    await ApplyMappingCorrection(client, correction.Reverse());
                }
                await Task.Run(() => store.SaveMappingState(scope, previous.Notes, previous.Media, previous.Deferred));
                history.Pop(); position = previous.Position; queue[position] = previous.Item; await RecalculateMappings(previous.Correction is not null); }
            catch (Exception error) { failure = error.Message; }
            finally { saving = false; Load(); if (failure is not null) feedback.Text = failure; }
        };
        close.Click += (_, _) => window.Close(); window.Closing += (_, args) => { if (saving) args.Cancel = true; };
        window.PreviewKeyDown += async (_, args) =>
        {
            if (Keyboard.FocusedElement is TextBoxBase box && !box.IsReadOnly) return;
            if (args.Key == Key.Space && Keyboard.FocusedElement is ToggleButton) return;
            if (args.Key is Key.Space or Key.D1 or Key.NumPad1)
            { args.Handled = true; if (!args.IsRepeat) await Save(args.Key == Key.Space ? "confirm" : "defer"); }
        };
        window.Closed += async (_, _) =>
        {
            try { await UpdateMappings(); }
            catch (Exception error) { MappingStatus.Text = "Could not reload correction history: " + error.Message; }
        };
        Load(); window.ShowDialog();
    }
}
