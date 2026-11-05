using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AnkiStats.Core;

namespace AnkiStats.App;

public partial class MainWindow
{
    private void ValidateCreationSession()
    {
        if (store.IsIsolated || data?.IsSynthetic == true) throw new InvalidOperationException("Note creation is disabled in isolated/synthetic sessions. Use the normal app with your real profile.");
        if (settings is null || data is null || importedData is null || coverage is null) throw new InvalidOperationException("Connect a collection and install vocabulary reference data first.");
        if (settings.CollectionKey != importedData.CollectionKey || settings.Signature != importedData.Signature)
            throw new InvalidOperationException("Refresh the current settings successfully before creating notes.");
    }
    private async void PreviewNewNotes(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        try
        {
            ValidateCreationSession();
            if (!int.TryParse(NewWordCount.Text, out var count) || count <= 0) throw new InvalidOperationException("Enter a positive whole number of words, for example 10.");
            using var client = new AnkiConnectClient(AnkiApiKey.Password);
            var imported = importedData!; var selected = data!; var selectedCoverage = coverage!; var chosenSettings = settings!;
            var model = imported.Metadata.NoteTypes.Find(t => t.Name == "Japanese") ?? throw new InvalidOperationException("The imported profile has no Japanese note type.");
            var profile = Path.GetFileName(Path.TrimEndingDirectorySeparator(imported.SourceProfileFolder));
            var progress = new Progress<string>(message => CreationStatus.Text = message);
            await AnkiConnection.Verify(client, profile, model.Id);
            if (selectedCoverage.Reference.Metadata.FormSelectionVersion < 1)
            {
                CreationStatus.Text = "Updating preferred study forms from the pinned dictionary snapshot…";
                var reference = await Task.Run(() => ReferenceStore.Download(progress));
                vocabularyMatcher = await Task.Run(() => new VocabularyMatcher(reference));
                await UpdateCoverage(); selectedCoverage = coverage!;
            }
            var journal = await Task.Run(() => store.LoadCoverageDecisions<CreationAttempt>(imported.CollectionKey, "creation"));
            SaveGeneratedMatches(imported.CollectionKey, journal);
            var plan = await Task.Run(() => NoteCreation.Prepare(client, selectedCoverage, imported, chosenSettings, count, journal, progress,
                matchingDecisions: store.LoadCoverageDecisions<MatchDecision>(imported.CollectionKey, "matches")));
            CreationStatus.Text = $"Prepared {plan.Notes.Count:N0} of {count:N0} requested words. Review the batch before creating notes.";
            await ReviewNewNotes(client, plan, selectedCoverage, imported, chosenSettings, journal);
        }
        catch (Exception error) { CreationStatus.Text = error.Message; SetStatus(error.Message, true); }
    });
    private async Task ReviewNewNotes(AnkiConnectClient client, CreationPlan plan, CoverageAnalysis selectedCoverage, ImportedCollection imported,
        Settings chosenSettings, List<CreationAttempt> journal)
    {
        var mediaBatch = plan.Notes.Any(n => n.Media.Length > 0 || n.Tier is "Other" or "Common / JLPT");
        var window = new Window { Owner = this, Title = "Preview new Anki notes", Width = mediaBatch ? 1200 : 950, Height = 700, MinWidth = 700, MinHeight = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var layout = new DockPanel { Margin = new Thickness(20) }; window.Content = layout;
        var header = new TextBlock { Text = $"{plan.Notes.Count:N0} / {plan.Requested:N0} requested words · profile {plan.Profile}\nDestination: {NoteCreation.Destination} · Japanese note type\n"
            + "Front uses the dictionary-preferred form and reading; English contains dictionary glosses. Anki decides how many cards each note generates.\n"
            + (mediaBatch ? $"Source titles use the Media field when available. Sentence is populated only where shown below: {plan.Notes.Count(n => n.Sentence.Length > 0):N0} with a source sentence, {plan.Notes.Count(n => n.Sentence.Length == 0):N0} blank. Other fields remain blank.\n"
                : "Other fields remain blank.\n")
            + (plan.Notes.Count < plan.Requested ? "Only this many eligible words remain after duplicate and possible-match checks.\n" : "")
            + (plan.Pending > 0 ? $"{plan.Pending:N0} pending attempts are excluded; use Check pending additions to reconcile them.\n" : ""), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) }; footer.Children.Add(status);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var create = new Button { Content = $"Create {plan.Notes.Count:N0} new notes" }; buttons.Children.Add(create);
        var cancel = new Button { Content = "Cancel" }; buttons.Children.Add(cancel);
        var details = new TextBox { IsReadOnly = true, Height = 110, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(details, Dock.Bottom); layout.Children.Add(details);
        var grid = new DataGrid { ItemsSource = plan.Notes, SelectionMode = DataGridSelectionMode.Single };
        foreach (var (label, path, width) in new[] { ("Priority", "Tier", 90), ("Front", "Front", 220), ("English", "Meaning", 400) })
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = width });
        if (mediaBatch)
            foreach (var (label, path, width) in new[] { ("Source sentence (blank when unavailable)", "Sentence", 380), ("Media / sentence source", "Media", 220) })
                grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = width });
        grid.SelectionChanged += (_, _) => { if (grid.SelectedItem is NewVocabularyNote note) details.Text = note.Front + "\n" + note.Meaning + "\nJMdict " + note.EntryId
            + (mediaBatch ? "\nSentence: " + (note.Sentence.Length == 0 ? "[blank]" : note.Sentence) + "\nMedia: " + note.Media : ""); };
        grid.SelectedIndex = 0; layout.Children.Add(grid);
        var sending = false;
        window.Closing += (_, args) => { if (sending) args.Cancel = true; };
        cancel.Click += (_, _) => window.Close();
        TaskCompletionSource<CreationResult?> completion = new();
        window.Closed += (_, _) => completion.TrySetResult(null);
        create.Click += async (_, _) =>
        {
            sending = true; create.IsEnabled = cancel.IsEnabled = false;
            try
            {
                var progress = new Progress<string>(message => status.Text = message);
                var result = await Task.Run(() => NoteCreation.Create(client, plan, selectedCoverage, imported, chosenSettings, journal,
                    attempts => store.SaveCoverageDecisions(imported.CollectionKey, "creation", attempts), progress));
                completion.TrySetResult(result); sending = false; window.Close();
            }
            catch (Exception error) { sending = false; create.IsEnabled = false; cancel.IsEnabled = true; status.Text = "Creation stopped. " + error.Message + " Check pending additions before retrying."; }
        };
        window.ShowDialog();
        var outcome = await completion.Task;
        if (outcome is null) return;
        CreationStatus.Text = $"Created {outcome.Added.Count:N0} new notes in {NoteCreation.Destination}; {outcome.Skipped:N0} skipped after rechecking. "
            + (outcome.Error ?? "Daily limits were not changed.");
        var identityError = false;
        try { SaveGeneratedMatches(imported.CollectionKey, journal); }
        catch (Exception error) { identityError = true; CreationStatus.Text += " Local match identities could not be saved: " + error.Message + " Notes already created remain in Anki. Use Check pending additions to recover their identities."; }
        // Refresh even after a partial/uncertain batch: an interrupted response may have
        // reached Anki. Keep creation receipts and errors distinct from refresh failures.
        var refreshError = false;
        try
        {
            await RefreshInternal();
            CreationStatus.Text += " Statistics and coverage refreshed.";
        }
        catch (Exception error)
        {
            refreshError = true;
            CreationStatus.Text += " Automatic refresh failed: " + error.Message
                + " Created notes remain in Anki. Previous statistics are retained; use Refresh to retry.";
        }
        SetStatus(CreationStatus.Text, outcome.Error is not null || identityError || refreshError);
    }
    private async void CheckPendingAdditions(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        try
        {
            ValidateCreationSession(); var imported = importedData!;
            var model = imported.Metadata.NoteTypes.Single(t => t.Name == "Japanese");
            var journal = store.LoadCoverageDecisions<CreationAttempt>(imported.CollectionKey, "creation");
            using var client = new AnkiConnectClient(AnkiApiKey.Password);
            await Task.Run(() => NoteCreation.Reconcile(client, Path.GetFileName(Path.TrimEndingDirectorySeparator(imported.SourceProfileFolder)), model.Id, journal,
                attempts => store.SaveCoverageDecisions(imported.CollectionKey, "creation", attempts)));
            SaveGeneratedMatches(imported.CollectionKey, journal);
            CreationStatus.Text = "Pending additions checked against live Anki. Existing notes with recorded dictionary identities are acknowledged; entries not found can be requested again. No notes were added or deleted.";
        }
        catch (Exception error) { CreationStatus.Text = error.Message; SetStatus(error.Message, true); }
    });
    private void SaveGeneratedMatches(string collection, List<CreationAttempt> journal)
    {
        var matches = store.LoadCoverageDecisions<MatchDecision>(collection, "matches");
        var changed = false;
        foreach (var attempt in journal.Where(a => a.State == "Added" && a.NoteId is not null && a.Fingerprint is not null))
            if (matches.All(m => m.NoteId != attempt.NoteId))
            {
                matches.Add(new(attempt.NoteId!.Value, attempt.Fingerprint!, attempt.EntryId, Origin: "Generated"));
                changed = true;
            }
        if (changed) store.SaveCoverageDecisions(collection, "matches", matches);
    }
}
