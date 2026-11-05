using System.Windows;
using AnkiStats.Core;

namespace AnkiStats.App;

public partial class MainWindow
{
    private async Task ApplyMappingCorrection(AnkiConnectClient client, AnkiCorrectionReceipt receipt)
    {
        if (store.IsIsolated || MappingSource?.IsSynthetic == true) throw new InvalidOperationException("Anki correction is disabled in isolated/synthetic sessions.");
        var logs = store.LoadCoverageDecisions<AnkiCorrectionLog>(MappingScope, "note-corrections");
        var log = new AnkiCorrectionLog(Guid.NewGuid().ToString("N"), receipt, "Pending verification", DateTimeOffset.UtcNow);
        logs.Add(log);
        store.SaveCoverageDecisions(MappingScope, "note-corrections", logs);
        await AnkiNoteCorrection.Apply(client, receipt);
        PatchMappingNote(receipt.AfterNote);
        logs[^1] = log with { State = "Applied" };
        store.SaveCoverageDecisions(MappingScope, "note-corrections", logs);
    }
    private void PatchMappingNote(NoteRecord note)
    {
        var sources = new[] { importedData, data, mappingCollection, mediaComparisonCollection }
            .Concat(profileViews.Select(v => v.Data)).Where(s => s is not null && s.CollectionKey == MappingScope).Distinct();
        foreach (var source in sources)
            for (var i = 0; i < source!.Notes.Count; i++) if (source.Notes[i].Id == note.Id) source.Notes[i] = note;
        // The cache timestamp still describes the original import; this patches only
        // the verified note content, not scheduling or collection provenance.
        if (importedData is not null) store.SaveCache(importedData);
        profileViews = profileViews.Select(v => v with { Analysis = Analyzer.Analyze(v.Data, kind: null) }).ToList();
    }
    private async void CheckMappingCorrection(object sender, RoutedEventArgs e) => await MediaAction(async _ =>
    {
        if (MappingCorrectionGrid.SelectedItem is not AnkiCorrectionLog log) return;
        if (store.IsIsolated || MappingSource?.IsSynthetic == true) throw new InvalidOperationException("Live correction checks are disabled in isolated/synthetic sessions.");
        using var client = new AnkiConnectClient(AnkiApiKey.Password);
        var state = await AnkiNoteCorrection.Inspect(client, log.Receipt);
        var logs = store.LoadCoverageDecisions<AnkiCorrectionLog>(MappingScope, "note-corrections");
        var index = logs.FindIndex(l => l.Id == log.Id);
        if (index >= 0) { logs[index] = log with { State = state }; store.SaveCoverageDecisions(MappingScope, "note-corrections", logs); }
        await UpdateMappings(); MappingStatus.Text = $"Correction check: {state}. This check did not write to Anki. Refresh mappings to import current fields.";
    });
}
