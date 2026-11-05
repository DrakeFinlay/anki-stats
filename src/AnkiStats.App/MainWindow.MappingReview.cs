using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AnkiStats.Core;

namespace AnkiStats.App;

public partial class MainWindow
{
    private sealed record MappingUndo(List<MatchDecision> Notes, List<MediaMappingDecision> Media, List<MappingDeferred> Deferred, int Position);
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
        string Describe(DictionaryEntry entry) => $"JMdict {entry.Id}\n{string.Join(" / ", entry.Spellings)}\n"
            + string.Join("\n", entry.Readings.Select(r => r.Text + (r.AppliesTo.Count > 0 && !r.AppliesTo.Contains("*") ? " [applies to " + string.Join(", ", r.AppliesTo) + "]" : "")))
            + $"\n\n{entry.Meaning}\n\nCommon: {(entry.Common ? "Yes" : "No")} · JLPT: {string.Join(", ", entry.Levels)}\nDictionary: {vocabularyMatcher.Reference.Metadata.DictionaryVersion}";
        void ShowCandidate()
        {
            var selected = candidates.SelectedItems.OfType<DictionaryEntry>().ToList();
            detail.Text = selected.Count == 0 ? "No selected dictionary entry. Search a lookup or enter verified JMdict IDs, or send this item to manual cleanup."
                : string.Join("\n\n────────────\n\n", selected.Select(Describe));
            confirm.IsEnabled = !saving && position < queue.Count && selected.Count > 0;
        }
        void Load()
        {
            undo.IsEnabled = history.Count > 0; defer.IsEnabled = ignore.IsEnabled = position < queue.Count;
            if (position >= queue.Count)
            {
                heading.Text = "Review complete"; original.Text = "This queue is finished. Deferred items remain in the manual cleanup pile.";
                candidates.ItemsSource = null; detail.Text = "Close to return to the dashboard, or Undo the last decision."; confirm.IsEnabled = false; return;
            }
            var item = queue[position];
            heading.Text = $"{position + 1:N0} / {queue.Count:N0} · {item.Kind} mapping";
            original.Text = (item.Kind == "Anki" ? "CURRENT ANKI ITEM\n\n" : "ORIGINAL MEDIA SOURCE\n\n")
                + item.Expression + "\n\nReading: " + item.Reading + "\n\n" + item.Meaning + "\n\n" + item.Location
                + (item.Sentence.Length > 0 ? "\n\nSource sentence:\n" + item.Sentence : "") + "\n\nWhy review?\n" + item.Reason;
            lookup.Text = item.Expression;
            candidates.ItemsSource = item.CandidateIds.Where(vocabularyMatcher.Entries.ContainsKey).Select(id => vocabularyMatcher.Entries[id]).ToList();
            candidates.SelectedIndex = candidates.Items.Count > 0 ? 0 : -1; ShowCandidate();
            feedback.Text = "Space confirms only the selected entry/entries. 1 defers without confirming. Ignore suppresses this item locally. Anki is not edited.";
            window.Focus();
        }
        async Task Save(string action)
        {
            if (saving || position >= queue.Count) return;
            var ids = candidates.SelectedItems.OfType<DictionaryEntry>().Select(e => e.Id).Distinct().ToList();
            if (action == "confirm" && ids.Count == 0) { feedback.Text = "Select a dictionary entry before confirming."; return; }
            saving = true; confirm.IsEnabled = defer.IsEnabled = ignore.IsEnabled = undo.IsEnabled = close.IsEnabled = false;
            string? failure = null;
            try
            {
                var item = queue[position];
                var notes = store.LoadCoverageDecisions<MatchDecision>(scope, "matches");
                var media = store.LoadCoverageDecisions<MediaMappingDecision>(scope, "media-mappings");
                var deferred = store.LoadCoverageDecisions<MappingDeferred>(scope, "mapping-cleanup");
                var previous = new MappingUndo(notes.ToList(), media.ToList(), deferred.ToList(), position);
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
                history.Push(previous); await RecalculateMappings(); position++;
            }
            catch (Exception error) { failure = "Could not finish the mapping update: " + error.Message; }
            finally { saving = false; close.IsEnabled = true; Load(); if (failure is not null) feedback.Text = failure; }
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
            try { var previous = history.Peek(); await Task.Run(() => store.SaveMappingState(scope, previous.Notes, previous.Media, previous.Deferred));
                history.Pop(); position = previous.Position; await RecalculateMappings(); }
            catch (Exception error) { failure = error.Message; }
            finally { saving = false; Load(); if (failure is not null) feedback.Text = failure; }
        };
        close.Click += (_, _) => window.Close(); window.Closing += (_, args) => { if (saving) args.Cancel = true; };
        window.PreviewKeyDown += async (_, args) =>
        {
            if (Keyboard.FocusedElement is TextBoxBase box && !box.IsReadOnly) return;
            if (args.Key is Key.Space or Key.D1 or Key.NumPad1)
            { args.Handled = true; if (!args.IsRepeat) await Save(args.Key == Key.Space ? "confirm" : "defer"); }
        };
        Load(); window.ShowDialog();
    }
}
