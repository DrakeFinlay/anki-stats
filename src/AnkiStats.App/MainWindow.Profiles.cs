using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AnkiStats.Core;

namespace AnkiStats.App;

public partial class MainWindow
{
    private sealed record ProfileView(DeckProfile Profile, ImportedCollection Data, AnalysisResult Analysis);
    private ImportedCollection? importedData;
    private List<ProfileView> profileViews = [];
    private readonly ObservableCollection<DeckProfile> editorProfiles = [];
    private DeckProfile? editingProfile;
    private bool populatingProfiles;
    private readonly Dictionary<string, DateOnly> selectedStreaks = [];
    private string ActiveProfileId => (ActiveProfilePicker.SelectedItem as DeckProfile)?.Id ?? "legacy";

    private static List<ProfileView> PrepareProfiles(ImportedCollection source)
    {
        if (source.Profiles.Count == 0)
            return [new(new() { Id = "legacy", Name = "Saved selection" }, source, Analyzer.Analyze(source, kind: null))];
        return source.Profiles.Where(p => p.Enabled).Select(profile =>
        {
            var selected = DeckProfiles.Select(source, profile);
            return new ProfileView(profile, selected, Analyzer.Analyze(selected, kind: null));
        }).ToList();
    }
    private async Task DisplayImport(ImportedCollection source, List<ProfileView>? prepared = null)
    {
        var views = prepared ?? await Task.Run(() => PrepareProfiles(source));
        importedData = source;
        profileViews = views;
        ActiveProfilePicker.ItemsSource = views.Select(v => v.Profile).ToList();
        ActiveProfilePicker.SelectedItem = views.Find(v => v.Profile.Id == settings?.ActiveProfileId)?.Profile ?? views.FirstOrDefault()?.Profile;
        DrawRhythms();
        await DisplayActiveProfile();
    }
    private async Task DisplayActiveProfile()
    {
        var view = profileViews.Find(v => v.Profile.Id == ActiveProfileId);
        if (view is null) return;
        data = view.Data;
        await AnalyzeAndDisplay(view.Analysis);
    }
    private async void ActiveProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || busy || importedData is null || ActiveProfilePicker.SelectedItem is not DeckProfile profile) return;
        await Guard(async () =>
        {
            await DisplayActiveProfile();
            // Keep draft settings and failed refreshes from being persisted by a view switch.
            if (settings is not null && settings.CollectionKey == importedData.CollectionKey && settings.Signature == importedData.Signature)
            {
                var next = DeckProfiles.Copy(settings);
                next.ActiveProfileId = profile.Id;
                await Task.Run(() => store.SaveSettings(next));
                settings = next;
            }
        });
    }
    private void DrawRhythms()
    {
        RhythmCharts.Children.Clear();
        foreach (var view in profileViews)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "Review rhythm · " + view.Profile.Name, FontSize = 20, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = $"{view.Analysis.TotalAnswers:N0} rated answers · all history · midnight-based calendar weeks\n{view.Data.Scope}",
                Foreground = (System.Windows.Media.Brush)FindResource("Muted"), FontSize = 12, Margin = new Thickness(0, 6, 0, 12), TextWrapping = TextWrapping.Wrap });
            var chart = new ReviewChart { Height = 180 };
            chart.SetRows(view.Analysis.Daily, true);
            panel.Children.Add(chart);
            if (view.Analysis.TotalAnswers == 0)
                panel.Children.Add(new TextBlock { Text = "No rated answers in this profile. Check its deck rules and shared field mappings in Settings.", TextWrapping = TextWrapping.Wrap });
            RhythmCharts.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = panel });
        }
    }
    private void SaveEditingRules()
    {
        DeckGrid.CommitEdit();
        if (editingProfile is not null)
            editingProfile.DeckRules = deckChoices.Where(d => d.Mode != "Inherit").Select(d => new DeckRule(d.Name, d.Mode == "Include")).ToList();
    }
    private void LoadEditingRules()
    {
        deckChoices.Clear();
        foreach (var deck in metadata?.Decks ?? [])
        {
            var rule = editingProfile?.DeckRules.Find(r => r.Name == deck.Name);
            deckChoices.Add(new() { Name = deck.Name, Cards = deck.Cards, Mode = rule is null ? "Inherit" : rule.Include ? "Include" : "Exclude" });
        }
        EditingProfileLabel.Text = editingProfile is null ? "Select a deck profile" : "Deck rules · " + editingProfile.Name;
    }
    private void EditorProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (populatingProfiles || !ReferenceEquals(e.OriginalSource, ProfilesGrid)) return;
        SaveEditingRules();
        editingProfile = ProfilesGrid.SelectedItem as DeckProfile;
        LoadEditingRules();
    }
    private void AddProfile(object sender, RoutedEventArgs e)
    {
        SaveEditingRules();
        var number = 1;
        while (editorProfiles.Any(p => p.Name == "Profile " + number)) number++;
        var profile = new DeckProfile { Name = "Profile " + number, DeckRules = editingProfile?.DeckRules.ToList() ?? [] };
        editorProfiles.Add(profile);
        ProfilesGrid.SelectedItem = profile;
        ProfilesGrid.ScrollIntoView(profile);
    }
    private void RemoveProfile(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not DeckProfile profile) return;
        editorProfiles.Remove(profile);
        ProfilesGrid.SelectedItem = editorProfiles.FirstOrDefault();
    }
    private void PopulateProfiles(CollectionMetadata found, Settings? previous)
    {
        populatingProfiles = true;
        try
        {
            editingProfile = null;
            editorProfiles.Clear();
            var draft = previous is null ? new Settings() : DeckProfiles.Copy(previous);
            DeckProfiles.Ensure(draft, found);
            foreach (var profile in draft.Profiles) editorProfiles.Add(profile);
            editingProfile = editorProfiles.FirstOrDefault(p => p.Id == draft.ActiveProfileId) ?? editorProfiles.FirstOrDefault();
            ProfilesGrid.SelectedItem = editingProfile;
            LoadEditingRules();
        }
        finally { populatingProfiles = false; }
    }
}
