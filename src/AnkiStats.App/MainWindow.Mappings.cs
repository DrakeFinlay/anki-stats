using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AnkiStats.Core;

namespace AnkiStats.App;

public partial class MainWindow
{
    private ImportedCollection? mappingCollection;
    private CoverageAnalysis? mappingAnalysis;
    private List<MappingReviewItem> mappingItems = [];
    private List<MediaMappingDecision> mediaMappingDecisions = [];
    private List<MappingDeferred> mappingDeferred = [];
    private readonly ObservableCollection<MappingAlias> mappingAliasRows = [];
    private string mappingAliasScope = "";
    private string MappingScope => settings?.CollectionKey ?? importedData?.CollectionKey ?? "media-library";
    private ImportedCollection? MappingSnapshot => mappingCollection?.CollectionKey == MappingScope
        && (importedData is null || mappingCollection.RefreshedAt >= importedData.RefreshedAt) ? mappingCollection
        : importedData?.CollectionKey == MappingScope ? importedData : null;
    private ImportedCollection? MappingSource => MappingSnapshot is {} snapshot ? MappingReview.JapaneseSource(snapshot) : null;
    private MediaCorpus MappedCorpus(MediaCorpus corpus) => vocabularyMatcher is null ? corpus : MappingReview.Apply(corpus, mediaMappingDecisions, vocabularyMatcher.Entries);
    private List<MappingAlias> LoadMappingAliases(string collection) => store.LoadCoverageDecisions<MappingAlias>(collection, "mapping-aliases");
    private bool IsDeferred(MappingReviewItem item) => !item.Resolved && mappingDeferred.Any(d => d.Key == item.Key && d.Fingerprint == item.Fingerprint);

    private async Task UpdateMappings()
    {
        if (vocabularyMatcher is null || MappingSource is not {} source)
        {
            mappingItems = []; mappingAnalysis = null; mediaMappingDecisions = []; mappingDeferred = [];
            MappingCorrectionGrid.ItemsSource = null;
            MappingPossibleCards.Text = MappingSourceCount.Text = MappingCleanupCount.Text = "0";
            MappingNoteCount.Text = "No collection analyzed"; ApplyMappingFilter();
            MappingsScope.Text = "Connect a collection and install Word Coverage reference data first."; return;
        }
        var scope = source.CollectionKey;
        var decisions = store.LoadCoverageDecisions<MatchDecision>(scope, "matches");
        var aliases = LoadMappingAliases(scope);
        mediaMappingDecisions = store.LoadCoverageDecisions<MediaMappingDecision>(scope, "media-mappings");
        mappingDeferred = store.LoadCoverageDecisions<MappingDeferred>(scope, "mapping-cleanup");
        MappingCorrectionGrid.ItemsSource = store.LoadCoverageDecisions<AnkiCorrectionLog>(scope, "note-corrections").OrderByDescending(l => l.Time).ToList();
        if (mappingAliasScope != scope)
        {
            mappingAliasRows.Clear(); foreach (var rule in aliases) mappingAliasRows.Add(rule);
            MappingAliasGrid.ItemsSource = mappingAliasRows; mappingAliasScope = scope;
        }
        mappingAnalysis = await Task.Run(() => vocabularyMatcher.Analyze(source, decisions, aliases: aliases));
        mappingItems = await Task.Run(() => MappingReview.Items(source, mappingAnalysis, mediaLibrary, mediaMappingDecisions, vocabularyMatcher, aliases));
        var pending = mappingItems.Where(i => !i.Resolved).ToList();
        MappingPossibleCards.Text = pending.Where(i => i.Kind == "Anki" && i.CandidateIds.Count > 0).Sum(i => i.Cards).ToString("N0");
        MappingNoteCount.Text = $"{pending.Count(i => i.Kind == "Anki"):N0} uncertain notes · {pending.Where(i => i.Kind == "Anki").Sum(i => i.Cards):N0} supporting cards total";
        MappingSourceCount.Text = pending.Count(i => i.Kind == "Media").ToString("N0");
        MappingCleanupCount.Text = mappingItems.Count(IsDeferred).ToString("N0");
        MappingsScope.Text = $"Anki mappings: 日本語 and its subdecks only, independent of the active profile · snapshot {source.RefreshedAt.ToLocalTime():g}. "
            + "Refresh 日本語 mappings to read the complete deck tree. Confirmed links contribute to Word Coverage. Media forms have their own optional queue.";
        ApplyMappingFilter();
    }
    private void MappingFilterChanged(object sender, RoutedEventArgs e) { if (ready) ApplyMappingFilter(); }
    private bool MappingKindMatches(MappingReviewItem item) => MappingQueueKind.SelectedIndex == 1 ? item.Kind == "Media" : item.Kind == "Anki";
    private void ApplyMappingFilter()
    {
        var query = MappingSearch.Text.Trim();
        var items = mappingItems.Where(MappingKindMatches).Where(i => query.Length == 0 || $"{i.Expression}\n{i.Reading}\n{i.Meaning}\n{i.Location}".Contains(query, StringComparison.OrdinalIgnoreCase));
        MappingCleanupGrid.ItemsSource = items.Where(IsDeferred).ToList();
        MappingsGrid.ItemsSource = items.Where(i => !i.Resolved || MappingShowResolved.IsChecked == true).ToList();
    }
    private async Task RecalculateMappings(bool noteChanged = false)
    {
        if (noteChanged) await AnalyzeAndDisplay();
        else { await UpdateCoverage(); await UpdateMappings(); }
        if (HasMediaComparison && mediaComparisonCollection is {} mediaSource && mediaSource.CollectionKey == MappingScope)
            mediaCoverage = await Task.Run(() => vocabularyMatcher!.Analyze(mediaSource,
                store.LoadCoverageDecisions<MatchDecision>(MappingScope, "matches"), aliases: LoadMappingAliases(MappingScope)));
        DrawMedia();
    }
    private async void RefreshMappingsClick(object sender, RoutedEventArgs e) => await MediaAction(async token =>
    {
        if (settings is null) throw new InvalidOperationException("Connect an Anki collection first.");
        await RequireMediaReference();
        var scoped = MappingReview.JapaneseSettings(settings);
        var collection = await ReaderFor(scoped.ProfileFolder).Import(scoped);
        token.ThrowIfCancellationRequested(); mappingCollection = collection;
        await UpdateMappings(); MappingStatus.Text = "Mappings refreshed from a read-only 日本語 deck snapshot. Other deck trees were excluded.";
    });
    private async void SaveMappingAliases(object sender, RoutedEventArgs e) => await MediaAction(async _ =>
    {
        await RequireMediaReference();
        MappingAliasGrid.CommitEdit(DataGridEditingUnit.Cell, true); MappingAliasGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var rules = mappingAliasRows.Where(r => r.Expression.Length + r.Lookup.Length + r.Reading.Length + r.NoteType.Length > 0).ToList();
        if (rules.Any(r => string.IsNullOrWhiteSpace(r.Expression) || string.IsNullOrWhiteSpace(r.Lookup)
            || vocabularyMatcher!.Find(r.Lookup.Trim(), r.Reading.Trim()).Count == 0))
            throw new InvalidOperationException("Each alias needs an exact source expression and a lookup/reading present in the installed dictionary.");
        if (rules.GroupBy(r => r.Expression.Normalize().Trim().ToUpperInvariant() + "\u001f" + r.NoteType).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Keep only one alias per exact expression and note type.");
        store.SaveCoverageDecisions(MappingScope, "mapping-aliases", rules);
        await RecalculateMappings(); MappingStatus.Text = "Alias suggestions saved locally. They do not confirm any matches automatically.";
    });
    private void LaunchMappings(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        ShowMappingReview(mappingItems.Where(i => !i.Resolved && !IsDeferred(i) && MappingKindMatches(i)).ToList());
    }
    private void ReviewCleanupMapping(object sender, RoutedEventArgs e)
    { if (!busy && MappingCleanupGrid.SelectedItem is MappingReviewItem item) ShowMappingReview([item]); }
    private void ReviewSelectedMapping(object sender, RoutedEventArgs e)
    { if (!busy && MappingsGrid.SelectedItem is MappingReviewItem item) ShowMappingReview([item]); }
    private async void ResetMappingClick(object sender, RoutedEventArgs e) => await MediaAction(async _ =>
    {
        if (MappingsGrid.SelectedItem is not MappingReviewItem item) return;
        var notes = store.LoadCoverageDecisions<MatchDecision>(MappingScope, "matches");
        var media = store.LoadCoverageDecisions<MediaMappingDecision>(MappingScope, "media-mappings");
        var deferred = store.LoadCoverageDecisions<MappingDeferred>(MappingScope, "mapping-cleanup");
        notes.RemoveAll(d => item.NoteId == d.NoteId); media.RemoveAll(d => d.CorpusId == item.CorpusId && d.WordKey == item.WordKey);
        deferred.RemoveAll(d => d.Key == item.Key); store.SaveMappingState(MappingScope, notes, media, deferred);
        await RecalculateMappings(); MappingStatus.Text = "Saved decision reset. Anki was not modified.";
    });
}
